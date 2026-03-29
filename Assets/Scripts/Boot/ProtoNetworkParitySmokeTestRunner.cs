using System;
using System.Text;
using Elysium.Networking;
using Elysium.Packaging;
using Elysium.Prototype.Networking;
using UnityEngine;

namespace Elysium.Boot
{
    /// Verifies networked parity for prototype-adapted flows under Elysium authority.
    ///
    /// Guardrails validated here:
    /// 1) External/prototype requests mutate state only through Elysium services.
    /// 2) Sender client IDs cannot impersonate other player IDs.
    /// 3) Character assignment remains GM-authoritative.
    /// 4) Movement remains host-authoritative and locked during combat.
    public sealed class ProtoNetworkParitySmokeTestRunner : MonoBehaviour
    {
        private const string ProtoProjectFolder = "proto_village_square";

        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        public void RunProtoNetworkParitySmokeTest()
        {
            try
            {
                LastSummary = RunInternal();
                LastSuccess = true;
            }
            catch (Exception ex)
            {
                LastSuccess = false;
                LastSummary = $"Error: {ex.Message}";
                Debug.LogError($"[ProtoNetworkParity] Smoke test failed: {ex}");
            }
        }

        private static string RunInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Proto Network Parity Smoke Test ===");

            Require(
                WorldProjectLoader.TryLoadFromStreamingAssets(ProtoProjectFolder, out var project, out var loadErr),
                $"Could not load proto world project: {loadErr}");

            var gallery = new Characters.CharacterGalleryService();
            Require(gallery.TryLoadFromStreamingAssets(ProtoProjectFolder, out var characters, out var galleryErr), galleryErr);
            Require(characters.Count >= 2, "Expected at least 2 characters in proto gallery.");
            var fighterId = characters[0].Id;
            var clericId = characters[1].Id;

            var session = new SessionService();
            var exploration = new ExplorationSyncService();
            var adapter = new ProtoNetworkProtocolAdapter(session, exploration);

            // 1. Session open via adapter.
            Require(adapter.TryOpenSessionAsHost("proto_net_001", project.Definition.ProjectId, out var error), error);
            log.AppendLine("Session opened through adapter -> SessionService — ok");

            // 2. Register players through adapter (bootstrap GM must be local host client 0).
            Require(adapter.TryRegisterPlayer(new ProtoJoinRequest
            {
                SenderClientId = 0,
                PlayerId = "gm_001",
                DisplayName = "GM",
                RequestedRole = PlayerRole.GameMaster,
            }, out error), error);

            Require(adapter.TryRegisterPlayer(new ProtoJoinRequest
            {
                SenderClientId = 1,
                PlayerId = "player_001",
                DisplayName = "Alice",
                RequestedRole = PlayerRole.Player,
            }, out error), error);

            Require(adapter.TryRegisterPlayer(new ProtoJoinRequest
            {
                SenderClientId = 2,
                PlayerId = "player_002",
                DisplayName = "Bob",
                RequestedRole = PlayerRole.Player,
            }, out error), error);

            Require(string.Equals(session.GMPlayerId, "gm_001", StringComparison.Ordinal),
                $"Expected GM 'gm_001', got '{session.GMPlayerId}'.");
            log.AppendLine("Players registered and GM assigned through adapter — ok");

            // 3. Sender identity guard: client 2 cannot act as player_001.
            var impersonationRejected = !adapter.TrySubmitMovement(new ProtoMovementRequest
            {
                SenderClientId = 2,
                RequesterPlayerId = "player_001",
                AreaId = "area_proto_village",
                Position = new Vector3(1f, 0f, 1f),
                FacingYaw = 45f,
            }, out var impersonationErr);
            Require(impersonationRejected, "Impersonation movement request should be rejected.");
            log.AppendLine($"Impersonation blocked: {impersonationErr} — ok");

            // 4. GM-only character assignment guard.
            var unauthorizedAssignRejected = !adapter.TryAssignCharacter(new ProtoCharacterAssignRequest
            {
                SenderClientId = 1,
                RequesterPlayerId = "player_001",
                TargetPlayerId = "player_002",
                CharacterId = clericId,
            }, out var unauthorizedAssignErr);
            Require(unauthorizedAssignRejected, "Non-GM character assignment should be rejected.");
            log.AppendLine($"Non-GM assignment blocked: {unauthorizedAssignErr} — ok");

            // 5. GM assigns characters.
            Require(adapter.TryAssignCharacter(new ProtoCharacterAssignRequest
            {
                SenderClientId = 0,
                RequesterPlayerId = "gm_001",
                TargetPlayerId = "player_001",
                CharacterId = fighterId,
            }, out error), error);

            Require(adapter.TryAssignCharacter(new ProtoCharacterAssignRequest
            {
                SenderClientId = 0,
                RequesterPlayerId = "gm_001",
                TargetPlayerId = "player_002",
                CharacterId = clericId,
            }, out error), error);
            log.AppendLine("GM character assignment via adapter -> SessionService — ok");

            // 6. Host area and accept movement in Lobby.
            Require(adapter.TryHostArea(project.Definition.EntryAreaId, out error), error);

            Require(adapter.TrySubmitMovement(new ProtoMovementRequest
            {
                SenderClientId = 1,
                RequesterPlayerId = "player_001",
                AreaId = project.Definition.EntryAreaId,
                Position = new Vector3(3f, 0f, 2f),
                FacingYaw = 90f,
            }, out error), error);

            var p1State = exploration.GetParticipant("player_001");
            Require(p1State != null, "player_001 movement state not found.");
            Require(p1State.Position == new Vector3(3f, 0f, 2f),
                $"Unexpected player_001 position: {p1State.Position}");
            log.AppendLine("Lobby movement accepted through adapter -> ExplorationSyncService — ok");

            // 7. Combat lockout: movement blocked during combat.
            Require(session.TryStartCombat("gm_001", out error), error);
            var combatMovementRejected = !adapter.TrySubmitMovement(new ProtoMovementRequest
            {
                SenderClientId = 2,
                RequesterPlayerId = "player_002",
                AreaId = project.Definition.EntryAreaId,
                Position = new Vector3(5f, 0f, -1f),
                FacingYaw = 180f,
            }, out var combatMoveErr);
            Require(combatMovementRejected, "Movement during combat should be rejected.");
            log.AppendLine($"Combat movement lockout enforced: {combatMoveErr} — ok");

            // 8. Back to Lobby, movement works again.
            session.EndCombat();
            Require(adapter.TrySubmitMovement(new ProtoMovementRequest
            {
                SenderClientId = 2,
                RequesterPlayerId = "player_002",
                AreaId = project.Definition.EntryAreaId,
                Position = new Vector3(5f, 0f, -1f),
                FacingYaw = 180f,
            }, out error), error);
            log.AppendLine("Post-combat movement accepted — ok");

            // 9. Verify ownership mapping remains consistent.
            var ownerFighter = session.GetCharacterOwner(fighterId);
            var ownerCleric = session.GetCharacterOwner(clericId);
            Require(ownerFighter != null && ownerFighter.PlayerId == "player_001",
                "fighterId ownership mismatch.");
            Require(ownerCleric != null && ownerCleric.PlayerId == "player_002",
                "clericId ownership mismatch.");
            log.AppendLine("Character ownership mapping preserved — ok");

            log.AppendLine("=== Proto Network Parity Smoke Test COMPLETE ===");
            return log.ToString();
        }

        private static void Require(bool condition, string error)
        {
            if (!condition)
            {
                throw new InvalidOperationException(error);
            }
        }
    }
}
