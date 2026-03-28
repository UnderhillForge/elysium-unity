using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Elysium.Characters;
using Elysium.Combat;
using UnityEngine;

namespace Elysium.World.Lua
{
    public sealed class LuaRuntimeService
    {
        public LuaExecutionResult Execute(
            string scriptAbsolutePath,
            string functionName,
            LuaHostContext context,
            LuaExecutionActor actor,
            LuaScriptReference scriptReference,
            LuaSandboxPolicy policy)
        {
            var result = new LuaExecutionResult();

            if (!File.Exists(scriptAbsolutePath))
            {
                result.Error = $"Lua script missing: {scriptAbsolutePath}";
                return result;
            }

            if (!ValidateCapabilities(scriptReference.Capabilities, policy, out var capabilityError))
            {
                result.Error = capabilityError;
                return result;
            }

            var scriptSource = File.ReadAllText(scriptAbsolutePath);

            if (TryExecuteWithMoonSharp(scriptSource, functionName, context, actor, out var moonSharpResult))
            {
                return moonSharpResult;
            }

            result.Error = BuildMoonSharpUnavailableError();
            return result;
        }

        private static bool ValidateCapabilities(
            IReadOnlyList<string> requiredCapabilities,
            LuaSandboxPolicy policy,
            out string error)
        {
            error = string.Empty;
            if (requiredCapabilities == null || requiredCapabilities.Count == 0)
            {
                return true;
            }

            var allowed = new HashSet<string>(policy.EnumerateGrantedCapabilities(), StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < requiredCapabilities.Count; i++)
            {
                var capability = requiredCapabilities[i];
                if (!allowed.Contains(capability))
                {
                    error = $"Lua capability denied by policy: {capability}";
                    return false;
                }
            }

            return true;
        }

        private static bool TryExecuteWithMoonSharp(
            string scriptSource,
            string functionName,
            LuaHostContext context,
            LuaExecutionActor actor,
            out LuaExecutionResult result)
        {
            result = null;

            var scriptType = Type.GetType("MoonSharp.Interpreter.Script, MoonSharp.Interpreter");
            if (scriptType == null)
            {
                return false;
            }

            try
            {
                RegisterMoonSharpTypes();

                // Reflection-based MoonSharp execution
                // Create a new Script instance via reflection
                var scriptInstance = Activator.CreateInstance(scriptType);
                
                // Resolve DoString overload to support different MoonSharp package variants.
                var doStringMethods = scriptType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.Name == "DoString")
                    .Where(m =>
                    {
                        var parameters = m.GetParameters();
                        return parameters.Length >= 1 && parameters[0].ParameterType == typeof(string);
                    })
                    .OrderBy(m => m.GetParameters().Length)
                    .ToArray();
                if (doStringMethods.Length == 0)
                {
                    result = new LuaExecutionResult
                    {
                        Success = false,
                        UsedMoonSharp = true,
                        Error = "MoonSharp: DoString method not found (incompatible version). "
                            + $"Available instance methods: {DescribeMethodSignatures(scriptType, "DoString")}",
                    };
                    return true;
                }

                // Load the script source
                var loaded = false;
                Exception lastDoStringException = null;
                for (var i = 0; i < doStringMethods.Length; i++)
                {
                    try
                    {
                        doStringMethods[i].Invoke(scriptInstance, BuildInvokeArguments(doStringMethods[i], scriptSource));
                        loaded = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        lastDoStringException = ex;
                    }
                }

                if (!loaded)
                {
                    var root = UnwrapException(lastDoStringException);
                    result = new LuaExecutionResult
                    {
                        Success = false,
                        UsedMoonSharp = true,
                        Error = "MoonSharp: failed to execute DoString overloads. "
                            + $"Tried signatures: {DescribeMethods(doStringMethods)}. "
                            + $"Root error: {root?.Message ?? "unknown"}",
                    };
                    return true;
                }

                // Register context and actor as globals
                object globals = null;
                Type globalsType = null;
                var globalsProperty = scriptType.GetProperty("Globals");
                if (globalsProperty != null)
                {
                    globals = globalsProperty.GetValue(scriptInstance);
                    globalsType = globals?.GetType();
                    var dynValueType = Type.GetType("MoonSharp.Interpreter.DynValue, MoonSharp.Interpreter");
                    var fromObjectMethod = dynValueType?.GetMethod(
                        "FromObject",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { scriptType, typeof(object) },
                        null);
                    var setDynValueMethod = dynValueType == null
                        ? null
                        : globalsType.GetMethod(
                            "Set",
                            BindingFlags.Public | BindingFlags.Instance,
                            null,
                            new[] { typeof(string), dynValueType },
                            null);
                    var setObjectMethod = globalsType.GetMethod(
                        "Set",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new[] { typeof(string), typeof(object) },
                        null);
                    
                    if (globals != null && fromObjectMethod != null && setDynValueMethod != null)
                    {
                        var contextValue = fromObjectMethod.Invoke(null, new object[] { scriptInstance, context });
                        var actorValue = fromObjectMethod.Invoke(null, new object[] { scriptInstance, actor });
                        setDynValueMethod.Invoke(globals, new[] { "context", contextValue });
                        setDynValueMethod.Invoke(globals, new[] { "actor", actorValue });
                    }
                    else if (globals != null && setObjectMethod != null)
                    {
                        setObjectMethod.Invoke(globals, new object[] { "context", context });
                        setObjectMethod.Invoke(globals, new object[] { "actor", actor });
                    }
                }

                // Call the function via reflection
                var callMethods = scriptType.GetMethods().Where(m => m.Name == "Call").ToArray();
                var callMethod = callMethods.FirstOrDefault(m =>
                {
                    var parameters = m.GetParameters();
                    return parameters.Length == 2 && parameters[1].ParameterType == typeof(object[]);
                }) ?? callMethods.FirstOrDefault(m => m.GetParameters().Length >= 1);

                if (callMethod == null)
                {
                    result = new LuaExecutionResult
                    {
                        Success = false,
                        UsedMoonSharp = true,
                        Error = "MoonSharp: Call method not found (incompatible version). "
                            + $"Available instance methods: {DescribeMethodSignatures(scriptType, "Call")}",
                    };
                    return true;
                }

                // Get the function from globals
                var getGlobalMethod = scriptType.GetMethod("GetGlobal", new[] { typeof(string) });
                object functionObject = null;
                if (getGlobalMethod != null)
                {
                    functionObject = getGlobalMethod.Invoke(scriptInstance, new object[] { functionName });
                }
                else if (globals != null && globalsType != null)
                {
                    var getMethod = globalsType.GetMethod("Get", new[] { typeof(string) });
                    if (getMethod != null)
                    {
                        functionObject = getMethod.Invoke(globals, new object[] { functionName });
                    }
                    else
                    {
                        var indexer = globalsType.GetProperty("Item", new[] { typeof(string) });
                        if (indexer != null)
                        {
                            functionObject = indexer.GetValue(globals, new object[] { functionName });
                        }
                    }
                }

                if (functionObject == null)
                {
                    result = new LuaExecutionResult
                    {
                        Success = false,
                        UsedMoonSharp = true,
                        Error = "MoonSharp: could not resolve function from globals (incompatible version). "
                            + $"Missing API for function '{functionName}'.",
                    };
                    return true;
                }

                if (functionObject is string && functionObject.ToString() == "nil")
                {
                    result = new LuaExecutionResult
                    {
                        Success = false,
                        UsedMoonSharp = true,
                        Error = $"MoonSharp: Function '{functionName}' not found in script.",
                    };
                    return true;
                }

                // Invoke the function with context and actor parameters
                object[] invokeArgs;
                var callParameters = callMethod.GetParameters();
                if (callParameters.Length == 2 && callParameters[1].ParameterType == typeof(object[]))
                {
                    invokeArgs = new object[] { functionObject, new object[] { context, actor } };
                }
                else if (callParameters.Length >= 3)
                {
                    invokeArgs = new object[] { functionObject, context, actor };
                }
                else
                {
                    invokeArgs = new object[] { functionObject };
                }

                callMethod.Invoke(scriptInstance, invokeArgs);
                
                result = new LuaExecutionResult
                {
                    Success = true,
                    UsedMoonSharp = true,
                };
                
                return true;
            }
            catch (Exception ex)
            {
                var root = UnwrapException(ex);
                result = new LuaExecutionResult
                {
                    Success = false,
                    UsedMoonSharp = true,
                    Error = "MoonSharp execution failed: "
                        + $"{root?.Message ?? ex.Message} "
                        + $"(function='{functionName}')",
                };
                return true;
            }
        }

        private static string BuildMoonSharpUnavailableError()
        {
            var loadedMoonSharpAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetName().Name)
                .Where(n => !string.IsNullOrEmpty(n) && n.IndexOf("MoonSharp", StringComparison.OrdinalIgnoreCase) >= 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var loadedText = loadedMoonSharpAssemblies.Length == 0
                ? "none"
                : string.Join(", ", loadedMoonSharpAssemblies);

            return "MoonSharp runtime is required but unavailable. "
                + "Ensure MoonSharp.Interpreter is present in the project. "
                + $"Loaded MoonSharp assemblies: {loadedText}.";
        }

        private static string DescribeMethodSignatures(Type type, string methodName)
        {
            if (type == null || string.IsNullOrEmpty(methodName))
            {
                return "none";
            }

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
                .ToArray();

            return DescribeMethods(methods);
        }

        private static string DescribeMethods(IReadOnlyList<MethodInfo> methods)
        {
            if (methods == null || methods.Count == 0)
            {
                return "none";
            }

            var signatures = new List<string>(methods.Count);
            for (var i = 0; i < methods.Count; i++)
            {
                signatures.Add(methods[i].ToString());
            }

            return string.Join(" | ", signatures);
        }

        private static void RegisterMoonSharpTypes()
        {
            var userDataType = Type.GetType("MoonSharp.Interpreter.UserData, MoonSharp.Interpreter");
            if (userDataType == null)
            {
                return;
            }

            var registerTypeMethod = userDataType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "RegisterType" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            var registerTypeByTypeMethod = userDataType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "RegisterType")
                .FirstOrDefault(m =>
                {
                    var parameters = m.GetParameters();
                    return parameters.Length >= 1 && parameters[0].ParameterType == typeof(Type);
                });

            if (registerTypeMethod == null && registerTypeByTypeMethod == null)
            {
                return;
            }

            var types = new[]
            {
                typeof(LuaHostContext),
                typeof(LuaExecutionActor),
                typeof(CharacterRecord),
                typeof(AttackRoll),
                typeof(SavingThrow),
                typeof(SkillCheck),
                typeof(LuaCombatStateSnapshot),
                typeof(LuaSessionStateSnapshot),
                typeof(LuaPlayerBindingSnapshot),
            };

            for (var i = 0; i < types.Length; i++)
            {
                try
                {
                    if (registerTypeMethod != null)
                    {
                        registerTypeMethod.MakeGenericMethod(types[i]).Invoke(null, Array.Empty<object>());
                    }
                    else
                    {
                        registerTypeByTypeMethod.Invoke(null, BuildTypeRegistrationArguments(registerTypeByTypeMethod, types[i]));
                    }
                }
                catch
                {
                    // MoonSharp throws if the type is already registered; ignore and continue.
                }
            }
        }

        private static object[] BuildTypeRegistrationArguments(MethodInfo method, Type type)
        {
            var parameters = method.GetParameters();
            var args = new object[parameters.Length];
            if (parameters.Length > 0)
            {
                args[0] = type;
            }

            for (var i = 1; i < parameters.Length; i++)
            {
                if (parameters[i].IsOptional)
                {
                    args[i] = Type.Missing;
                    continue;
                }

                var parameterType = parameters[i].ParameterType;
                if (!parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) != null)
                {
                    args[i] = null;
                }
                else
                {
                    args[i] = Activator.CreateInstance(parameterType);
                }
            }

            return args;
        }

        private static object[] BuildInvokeArguments(MethodInfo method, string source)
        {
            var parameters = method.GetParameters();
            var args = new object[parameters.Length];
            if (parameters.Length > 0)
            {
                args[0] = source;
            }

            for (var i = 1; i < parameters.Length; i++)
            {
                if (parameters[i].IsOptional)
                {
                    args[i] = Type.Missing;
                    continue;
                }

                var parameterType = parameters[i].ParameterType;
                if (!parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) != null)
                {
                    args[i] = null;
                }
                else
                {
                    args[i] = Activator.CreateInstance(parameterType);
                }
            }

            return args;
        }

        private static Exception UnwrapException(Exception ex)
        {
            var current = ex;
            while (current?.InnerException != null)
            {
                current = current.InnerException;
            }

            return current;
        }
    }
}