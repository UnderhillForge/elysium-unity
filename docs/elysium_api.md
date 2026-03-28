# Elysium API Reference

Version: 1.7
Status: Implemented Contract Only
Last Updated: March 2026

## Scope

This document is the source of truth for implemented public API contracts in the current repository.

Included:
- Implemented Lua runtime API
- Implemented Unity C# service APIs
- Implemented networking, persistence, and packaging contracts

Excluded:
- Planned, speculative, or future APIs
- Proposed web endpoints not implemented in code

For planned and future-facing interfaces, see [docs/planned_api.md](planned_api.md).

## Contract Rules

- API blocks in this file must match implemented signatures and field names.
- Promote APIs from planned docs only after implementation lands.
- Keep conceptual examples separate from signature blocks.

## Lua Scripting API

### LuaHostContext

Source: [Assets/Scripts/World/Lua/LuaHostContext.cs](../Assets/Scripts/World/Lua/LuaHostContext.cs#L8)

```csharp
public sealed class LuaHostContext
{
    public void log(string message);
    public void start_encounter(string encounterId);

    public AttackRoll roll_attack(CharacterRecord attacker, CharacterRecord defender, bool touch = false);
    public SavingThrow roll_save(CharacterRecord actor, int saveTypeIndex, int dc);
    public SkillCheck roll_skill(CharacterRecord actor, string skillName, int dc);
    public int get_ability_mod(CharacterRecord actor, int abilityIndex);
    public bool is_alive(CharacterRecord actor);
    public void apply_damage(CharacterRecord actor, int amount);
    public void heal_damage(CharacterRecord actor, int amount);

    public LuaCombatStateSnapshot get_combat_state();
    public bool is_combat_active();
    public int get_current_round();
    public string get_current_combatant_id();
    public string get_current_combatant_name();
    public int get_pending_action_count();
    public bool is_gm_guided_combat();

    public LuaSessionStateSnapshot get_session_state();
    public string get_session_id();
    public string get_world_project_id();
    public string get_gm_player_id();
    public int get_player_count();
    public int get_connected_player_count();
    public int get_session_state_index();
    public LuaPlayerBindingSnapshot get_combatant_owner(string combatantId);
    public LuaPlayerBindingSnapshot get_player_binding(string playerId);
    public string get_combatant_owner_player_id(string combatantId);
    public string get_player_role_name(string playerId);
}
```

### Lua Runtime and Metadata

Sources:
- [Assets/Scripts/World/Lua/LuaRuntimeService.cs](../Assets/Scripts/World/Lua/LuaRuntimeService.cs#L11)
- [Assets/Scripts/World/Lua/LuaMetadataParser.cs](../Assets/Scripts/World/Lua/LuaMetadataParser.cs#L9)
- [Assets/Scripts/World/Lua/LuaSandboxPolicy.cs](../Assets/Scripts/World/Lua/LuaSandboxPolicy.cs#L7)
- [Assets/Scripts/World/Lua/LuaScriptReference.cs](../Assets/Scripts/World/Lua/LuaScriptReference.cs#L7)
- [Assets/Scripts/World/Lua/LuaAttachmentKind.cs](../Assets/Scripts/World/Lua/LuaAttachmentKind.cs#L3)

```csharp
public sealed class LuaRuntimeService
{
    public LuaExecutionResult Execute(
        string scriptAbsolutePath,
        string functionName,
        LuaHostContext context,
        LuaExecutionActor actor,
        LuaScriptReference scriptReference,
        LuaSandboxPolicy policy);
}

public static class LuaMetadataParser
{
    public static LuaScriptReference ParseScriptReference(string projectRootPath, string scriptRelativePath);
}

public sealed class LuaSandboxPolicy
{
    public string HostApiVersion;
    public bool AllowWorldRead;
    public bool AllowWorldWrite;
    public bool AllowQuestWrite;
    public bool AllowEncounterControl;
    public bool AllowInventoryWrite;
    public bool AllowCombatRead;
    public bool AllowSessionRead;

    public IReadOnlyList<string> EnumerateGrantedCapabilities();
}

public sealed class LuaScriptReference
{
    public string Id;
    public string RelativePath;
    public LuaAttachmentKind AttachmentKind;
    public List<string> Capabilities;
}

public enum LuaAttachmentKind
{
    Asset = 0,
    Placeable = 1,
    Trigger = 2,
    Npc = 3,
    World = 4,
}
```

## Combat and Character APIs

Sources:
- [Assets/Scripts/Combat/CombatResolver.cs](../Assets/Scripts/Combat/CombatResolver.cs#L11)
- [Assets/Scripts/Combat/CombatStateService.cs](../Assets/Scripts/Combat/CombatStateService.cs#L9)
- [Assets/Scripts/Combat/CombatTurnTracker.cs](../Assets/Scripts/Combat/CombatTurnTracker.cs#L9)
- [Assets/Scripts/Combat/Combatant.cs](../Assets/Scripts/Combat/Combatant.cs#L9)
- [Assets/Scripts/Combat/TurnAction.cs](../Assets/Scripts/Combat/TurnAction.cs#L7)
- [Assets/Scripts/Characters/CharacterRecord.cs](../Assets/Scripts/Characters/CharacterRecord.cs#L7)

```csharp
public sealed class CombatResolver
{
    public static int RollD20();
    public static int RollDie(int sides);
    public static InitiativeRoll RollInitiative(string actorId, string actorName, int dexterityModifier = 0);
    public static List<InitiativeRoll> SortInitiative(List<InitiativeRoll> rolls);
    public static AttackRoll PerformAttack(
        string attackerId,
        string defenderId,
        CharacterRecord attacker,
        CharacterRecord defender,
        int weaponDamageBonus = 0,
        DamageType damageType = DamageType.Bludgeoning,
        bool isTouchAttack = false,
        bool isFlatFooted = false);
    public static SavingThrow PerformSavingThrow(
        string actorId,
        string actorName,
        CharacterRecord character,
        SavingThrowType saveType,
        int difficultyClass = 10,
        int circumstanceBonus = 0);
    public static SkillCheck PerformSkillCheck(
        string actorId,
        string actorName,
        CharacterRecord character,
        string skillName,
        int difficultyClass = 10,
        int circumstanceBonus = 0);
    public static void ApplyDamage(CharacterRecord character, int damageAmount);
    public static void HealDamage(CharacterRecord character, int healAmount);
    public static bool IsAlive(CharacterRecord character);
    public static bool IsConscious(CharacterRecord character);
}

public sealed class CombatStateService
{
    public static CombatStateService CreateForEncounter(string encounterId, CombatMode mode, string gmId = "");
    public void InitializeCombat(List<Combatant> combatants);
    public string GetInitiativeDisplay();
    public ActionResolution AttemptAction(TurnActionRequest request, out string validationError);
    public bool ResolveActionApproval(string actionId, bool approved, string gmResolution = "");
    public void EndCurrentTurn();
    public void SkipCurrentTurn();
    public void ProcessDamage(string targetId, int damageAmount);
    public void ProcessHealing(string targetId, int healAmount);
    public List<TurnAction> GetRoundActions(int round);
    public List<TurnAction> GetCombatantHistory(string combatantId);
    public EncounterSnapshot CreateEncounterSnapshot();
    public CombatPersistenceSnapshot CreatePersistenceSnapshot();
    public static CombatStateService RestoreFromPersistenceSnapshot(CombatPersistenceSnapshot snapshot);
}

public sealed class CombatTurnTracker
{
    public void InitializeFromInitiative(List<Combatant> combatantList);
    public void InitializeFromState(
        List<Combatant> combatantList,
        int persistedRound,
        string currentCombatantId,
        List<TurnAction> persistedActionHistory);
    public void RecordAction(TurnAction action);
    public void AdvanceToNextTurn();
    public void SkipTurn();
    public Combatant GetCombatant(string id);
    public void AddCombatantMidCombat(Combatant combatant);
    public void RemoveCombatant(string combatantId);
    public List<TurnAction> GetRoundActions(int round);
    public List<TurnAction> GetCombatantActions(string combatantId);
    public bool HasActiveCombatants();
    public Combatant PeekNextCombatant();
    public string GetInitiativeDisplay();
}
```

## Networking and Session APIs

Sources:
- [Assets/Scripts/Networking/SessionService.cs](../Assets/Scripts/Networking/SessionService.cs#L9)
- [Assets/Scripts/Networking/ElysiumSessionManager.cs](../Assets/Scripts/Networking/ElysiumSessionManager.cs#L14)
- [Assets/Scripts/Networking/CombatNetworkSnapshot.cs](../Assets/Scripts/Networking/CombatNetworkSnapshot.cs#L8)
- [Assets/Scripts/Networking/CombatNetworkService.cs](../Assets/Scripts/Networking/CombatNetworkService.cs#L7)
- [Assets/Scripts/Networking/CombatNetworkCoordinator.cs](../Assets/Scripts/Networking/CombatNetworkCoordinator.cs#L10)

```csharp
public sealed class SessionService
{
    public bool TryOpenSession(string sessionId, string worldProjectId, out string error);
    public bool TryRegisterPlayer(PlayerSessionRecord record, out string error);
    public bool TryDisconnectPlayer(string playerId, out string error);
    public bool TrySetRole(string requesterId, string targetPlayerId, PlayerRole newRole, out string error);
    public bool TryAssignCombatant(string requesterId, string targetPlayerId, string combatantId, out string error);
    public PlayerSessionRecord GetCombatantOwner(string combatantId);
    public PlayerSessionRecord GetPlayerByClientId(ulong networkClientId);
    public PlayerSessionRecord GetPlayer(string playerId);
    public bool TryStartCombat(string requesterId, out string error);
    public void EndCombat();
    public void CloseSession();
    public SessionPersistenceSnapshot CreateSnapshot();
    public void RestoreFromSnapshot(SessionPersistenceSnapshot snapshot);
}

public sealed class ElysiumSessionManager : NetworkBehaviour
{
    public bool OpenSession(string sessionId, string worldProjectId, out string error);
    public bool AssignGM(string hostPlayerId, string targetPlayerId, out string error);
    public bool AssignCombatant(string gmPlayerId, string targetPlayerId, string combatantId, out string error);
    public bool StartEncounter(string gmPlayerId, CombatStateService combatState, out string error);
    public void EndEncounter();
    public bool TrySaveCampaignState(string campaignDatabasePath, string encounterInstanceId, string areaId, out string error);
    public bool TryLoadCampaignState(string campaignDatabasePath, string encounterInstanceId, out string error);

    public void RegisterPlayerServerRpc(string playerId, string displayName, ServerRpcParams rpcParams = default);
    public void SubmitActionServerRpc(
        string requesterId,
        string combatantId,
        string actionName,
        string description,
        int actionTypeIndex,
        string targetCombatantId,
        ServerRpcParams rpcParams = default);
    public void ResolveActionServerRpc(
        string gmPlayerId,
        string actionId,
        bool approved,
        string resolutionText,
        ServerRpcParams rpcParams = default);
    public void EndTurnServerRpc(string requesterId, ServerRpcParams rpcParams = default);
}

public sealed class CombatNetworkService
{
    public void HostEncounter(CombatStateService combatState, string statusMessage = "Combat synchronized.");
    public void PublishStatus(string statusMessage);
    public bool TrySubmitAction(
        string requesterId,
        TurnActionRequest request,
        out ActionResolution resolution,
        out string error);
    public bool TryResolvePendingAction(
        string requesterId,
        string actionId,
        bool approved,
        string gmResolution,
        out string error);
    public bool TryEndTurn(string requesterId, out string error);
    public bool TrySkipTurn(string requesterId, out string error);
}

public sealed class CombatNetworkCoordinator : NetworkBehaviour
{
    public bool StartHostedEncounter(CombatStateService combatState, out string error);
    public void SubmitActionRequest(string requesterId, TurnActionRequest request);
    public void ResolvePendingAction(string requesterId, string actionId, bool approved, string resolutionText);
    public void EndTurn(string requesterId);
}
```

## Persistence APIs

Source: [Assets/Scripts/Persistence/CampaignPersistenceService.cs](../Assets/Scripts/Persistence/CampaignPersistenceService.cs#L14)

```csharp
public sealed class CampaignPersistenceService
{
    public CampaignPersistenceService(string campaignDatabasePath);

    public bool TrySaveSession(SessionService session, out string error);
    public bool TryLoadSession(out SessionService session, out string error);

    public bool TrySaveEncounterState(
        string encounterInstanceId,
        string encounterId,
        string areaId,
        CombatStateService combatState,
        out string error);

    public bool TryLoadEncounterState(
        string encounterInstanceId,
        out CombatStateService combatState,
        out string error);

    public bool TryGetNormalizedCounts(
        string encounterInstanceId,
        out int sessionPlayerCount,
        out int combatantCount,
        out int actionCount,
        out string error);

    public bool TrySaveCampaignState(
        SessionService session,
        string encounterInstanceId,
        string encounterId,
        string areaId,
        CombatStateService combatState,
        out string error);

    public bool TryLoadCampaignState(
        string encounterInstanceId,
        out SessionService session,
        out CombatStateService combatState,
        out string error);
}
```

## Packaging and World Project APIs

Sources:
- [Assets/Scripts/Packaging/WorldProjectLoader.cs](../Assets/Scripts/Packaging/WorldProjectLoader.cs#L8)
- [Assets/Scripts/Packaging/WorldProjectValidator.cs](../Assets/Scripts/Packaging/WorldProjectValidator.cs#L8)
- [Assets/Scripts/Packaging/EwmPackageService.cs](../Assets/Scripts/Packaging/EwmPackageService.cs#L13)
- [Assets/Scripts/Packaging/EwmExportResult.cs](../Assets/Scripts/Packaging/EwmExportResult.cs#L5)
- [Assets/Scripts/Packaging/EwmImportResult.cs](../Assets/Scripts/Packaging/EwmImportResult.cs#L5)
- [Assets/Scripts/Packaging/WorldProject.cs](../Assets/Scripts/Packaging/WorldProject.cs#L5)

```csharp
public static class WorldProjectLoader
{
    public static bool TryLoadFromStreamingAssets(
        string projectFolderName,
        out WorldProject worldProject,
        out string error);

    public static bool TryLoadFromPath(
        string projectRootPath,
        out WorldProject worldProject,
        out string error);
}

public static class WorldProjectValidator
{
    public static WorldProjectValidationResult Validate(WorldProject worldProject, EwmPackageMode packageMode);
}

public static class EwmPackageService
{
    public static EwmExportResult TryExportFromStreamingAssets(
        string projectFolderName,
        EwmPackageMode packageMode,
        string outputDirectory,
        string gameVersion,
        string apiVersion);

    public static EwmExportResult TryExportFromProject(
        WorldProject worldProject,
        EwmPackageMode packageMode,
        string outputDirectory,
        string gameVersion,
        string apiVersion);

    public static EwmImportResult TryImportToStreamingAssets(
        string packagePath,
        string targetFolderName,
        bool overwrite);
}

public sealed class EwmExportResult
{
    public bool Success;
    public string OutputPackagePath;
    public string Error;
    public List<string> Warnings;
}

public sealed class EwmImportResult
{
    public bool Success;
    public string ImportedProjectPath;
    public string Error;
    public List<string> Warnings;
    public bool IntegrityVerified;
    public bool IntegrityCheckSkipped;
    public string IntegrityMessage;
    public bool DependencyCompatible;
    public bool DependencyCheckSkipped;
    public string DependencyMessage;
}

public sealed class WorldProject
{
    public string RootPath { get; }
    public WorldProjectDefinition Definition { get; }
}
```

## Core Enums and IDs

Sources:
- [Assets/Scripts/Rules/RulesetId.cs](../Assets/Scripts/Rules/RulesetId.cs#L3)
- [Assets/Scripts/Networking/SessionTopology.cs](../Assets/Scripts/Networking/SessionTopology.cs#L3)
- [Assets/Scripts/Shared/WorldConstants.cs](../Assets/Scripts/Shared/WorldConstants.cs#L3)

```csharp
public static class RulesetId
{
    public const string Pathfinder1e = "Pathfinder1e";
}

public enum SessionTopology
{
    HostMode = 0,
    DedicatedServer = 1,
}

public static class WorldConstants
{
    public const string PackageExtension = ".ewm";
    public const int CurrentPackageFormatVersion = 1;
}
```

## Changelog (Implemented Releases)

- v1.7.0: Lua combat/session query bindings and MoonSharp userdata registration improvements
- v1.6.0: Host auto-persistence and normalized runtime persistence tables
- v1.5.0: Campaign persistence save/load pipeline
- v1.4.0: Session lifecycle and player registry
- v1.3.0: Host-authoritative combat networking
- v1.2.0: Turn-based combat tracking
- v1.1.0: PF1e combat services and models
- v1.0.0: Foundation services (loader, validator, package service, Lua runtime)
