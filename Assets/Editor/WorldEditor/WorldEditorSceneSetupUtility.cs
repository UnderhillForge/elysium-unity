#if UNITY_EDITOR
using Elysium.Boot;
using Elysium.Networking;
using Elysium.WorldEditor;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Elysium.Editor
{
    public static class WorldEditorSceneSetupUtility
    {
        private const string MainScenePath = "Assets/main.unity";
        private const string ProtoBootScenePath = "Assets/Scenes/Proto/ProtoBoot.unity";
        private const string ToolbarAssetPath = "Assets/UI Toolkit/WorldEditorToolbar.uxml";
        private const string BrushMaterialPath = "Assets/Materials/WorldEditorBrushPreview.mat";

        [MenuItem("Elysium/World Editor/Setup In Active Scene")]
        private static void SetupInActiveScene()
        {
            SetupScene(openMainScene: false);
        }

        [MenuItem("Elysium/World Editor/Open main.unity And Setup")]
        private static void OpenMainAndSetup()
        {
            SetupScene(openMainScene: true);
        }

        [MenuItem("Elysium/World Editor/Open ProtoBoot.unity And Setup")]
        private static void OpenProtoBootAndSetup()
        {
            SetupSceneAtPath(ProtoBootScenePath);
        }

        // Public entrypoint for batchmode: -executeMethod Elysium.Editor.WorldEditorSceneSetupUtility.SetupProtoBootSceneBatch
        public static void SetupProtoBootSceneBatch()
        {
            SetupSceneAtPath(ProtoBootScenePath);
            AssetDatabase.SaveAssets();
        }

        private static void SetupScene(bool openMainScene)
        {
            if (openMainScene)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                {
                    return;
                }

                var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath);
                if (scene == null)
                {
                    Debug.LogError($"[WorldEditorSetup] Could not find scene at {MainScenePath}.");
                    return;
                }

                EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            }

            ApplyWorldEditorSetup();
        }

        private static void SetupSceneAtPath(string scenePath)
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            if (scene == null)
            {
                Debug.LogError($"[WorldEditorSetup] Could not find scene at {scenePath}.");
                return;
            }

            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            ApplyWorldEditorSetup();
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            Debug.Log($"[WorldEditorSetup] Saved scene at {scenePath}.");
        }

        private static void ApplyWorldEditorSetup()
        {
            var root = FindOrCreate("WorldEditor");
            var networkObject = root.GetComponent<NetworkObject>() ?? Undo.AddComponent<NetworkObject>(root);
            var manager = root.GetComponent<WorldEditorManager>() ?? Undo.AddComponent<WorldEditorManager>(root);
            var preview = root.GetComponent<WorldEditorBrushPreview>() ?? Undo.AddComponent<WorldEditorBrushPreview>(root);
            var document = root.GetComponent<UIDocument>() ?? Undo.AddComponent<UIDocument>(root);
            var ui = root.GetComponent<WorldEditorUI>() ?? Undo.AddComponent<WorldEditorUI>(root);

            var bootstrap = Object.FindAnyObjectByType<ElysiumBootstrap>();
            var sessionManager = Object.FindAnyObjectByType<ElysiumSessionManager>();
            var terrain = Terrain.activeTerrain != null ? Terrain.activeTerrain : Object.FindAnyObjectByType<Terrain>();

            var placeablesRoot = root.transform.Find("PlaceablesRoot");
            if (placeablesRoot == null)
            {
                var placeablesGo = new GameObject("PlaceablesRoot");
                Undo.RegisterCreatedObjectUndo(placeablesGo, "Create PlaceablesRoot");
                placeablesGo.transform.SetParent(root.transform, false);
                placeablesRoot = placeablesGo.transform;
            }

            SetObjectField(manager, "bootstrap", bootstrap);
            SetObjectField(manager, "sessionManager", sessionManager);
            SetObjectField(manager, "targetTerrain", terrain);
            SetObjectField(manager, "placeablesRoot", placeablesRoot);
            SetObjectField(manager, "editorUI", ui);
            SetObjectField(ui, "manager", manager);
            SetObjectField(preview, "manager", manager);

            var toolbarAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(ToolbarAssetPath);
            if (toolbarAsset != null)
            {
                document.visualTreeAsset = toolbarAsset;
            }
            else
            {
                Debug.LogWarning($"[WorldEditorSetup] Could not find toolbar at {ToolbarAssetPath}.");
            }

            EnsureBrushPreviewMaterial(root);

            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(preview);
            EditorUtility.SetDirty(document);
            EditorUtility.SetDirty(ui);
            EditorSceneManager.MarkSceneDirty(root.scene);

            if (networkObject == null)
            {
                Debug.LogWarning("[WorldEditorSetup] Failed to add NetworkObject component.");
            }

            Debug.Log(
                "[WorldEditorSetup] WorldEditor scene setup complete. " +
                "Next: fill placeableCatalog with AnyRPG prefabs, ensure each prefab has NetworkObject, and register them in DefaultNetworkPrefabs.");
        }

        private static void EnsureBrushPreviewMaterial(GameObject root)
        {
            var line = root.GetComponent<LineRenderer>() ?? Undo.AddComponent<LineRenderer>(root);
            if (line == null)
            {
                return;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(BrushMaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                if (shader == null)
                {
                    Debug.LogWarning("[WorldEditorSetup] Could not find an Unlit shader for brush preview material.");
                    return;
                }

                material = new Material(shader)
                {
                    color = new Color(0.05f, 0.85f, 1f, 0.9f),
                };

                var materialDirectory = System.IO.Path.GetDirectoryName(BrushMaterialPath);
                if (!string.IsNullOrEmpty(materialDirectory) && !AssetDatabase.IsValidFolder(materialDirectory))
                {
                    AssetDatabase.CreateFolder("Assets", "Materials");
                }

                AssetDatabase.CreateAsset(material, BrushMaterialPath);
                AssetDatabase.SaveAssets();
            }

            line.material = material;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Stretch;
        }

        private static GameObject FindOrCreate(string objectName)
        {
            var go = GameObject.Find(objectName);
            if (go != null)
            {
                return go;
            }

            go = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(go, $"Create {objectName}");
            return go;
        }

        private static void SetObjectField(Object target, string fieldName, Object value)
        {
            if (target == null)
            {
                return;
            }

            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(fieldName);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
            {
                Debug.LogWarning($"[WorldEditorSetup] Could not assign field {fieldName} on {target.name}.");
                return;
            }

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
