using System;
using System.Text;
using Elysium.Prototype.Inventory;
using UnityEngine;

namespace Elysium.Boot
{
    /// Smoke test for the prototype inventory service.
    /// Validates pick-up, drop, equip, unequip, stacking, and error paths.
    public sealed class ProtoInventorySmokeTestRunner : MonoBehaviour
    {
        public bool LastSuccess { get; private set; }
        public string LastSummary { get; private set; } = "Not run";

        public void RunProtoInventorySmokeTest()
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
                Debug.LogError($"[ProtoInventory] Smoke test failed: {ex}");
            }
        }

        private static string RunInternal()
        {
            var log = new StringBuilder();
            log.AppendLine("=== Proto Inventory Smoke Test ===");

            var service = new ProtoInventoryService();

            // Register test items
            var sword = new ProtoItemDefinition("iron_sword",      "Iron Sword",       ProtoEquipSlot.MainHand, "A basic iron sword.");
            var shield = new ProtoItemDefinition("wooden_shield",   "Wooden Shield",    ProtoEquipSlot.OffHand,  "A battered wooden shield.");
            var potion = new ProtoItemDefinition("health_potion",   "Health Potion",    ProtoEquipSlot.None,     "Restores 1d8+1 HP.");
            service.RegisterItem(sword);
            service.RegisterItem(shield);
            service.RegisterItem(potion);
            log.AppendLine("  Items registered — ok");

            const string charId = "pc_test_hero";

            // ---- Pick up items ----
            Require(service.TryPickUp(charId, "iron_sword",    1, out var err), err);
            Require(service.TryPickUp(charId, "wooden_shield", 1, out err),     err);
            Require(service.TryPickUp(charId, "health_potion", 3, out err),     err);
            log.AppendLine("  Pick-ups succeeded — ok");

            var inv = service.GetOrCreate(charId);
            Require(inv.TotalItemCount == 5, $"Expected 5 total items, got {inv.TotalItemCount}.");
            log.AppendLine($"  TotalItemCount == 5 — ok");

            // ---- Potion stacking ----
            Require(service.TryPickUp(charId, "health_potion", 2, out err), err);
            Require(inv.TotalItemCount == 7, $"Expected 7 after stacking, got {inv.TotalItemCount}.");
            log.AppendLine("  Stack merge: 3+2 potions → 5 — ok");

            // ---- Equip ----
            Require(service.TryEquip(charId, "iron_sword",    out err), err);
            Require(service.TryEquip(charId, "wooden_shield", out err), err);
            Require(string.Equals(inv.GetEquippedItem(ProtoEquipSlot.MainHand), "iron_sword", StringComparison.Ordinal),
                "MainHand should contain iron_sword.");
            Require(string.Equals(inv.GetEquippedItem(ProtoEquipSlot.OffHand), "wooden_shield", StringComparison.Ordinal),
                "OffHand should contain wooden_shield.");
            log.AppendLine("  Equip sword + shield — ok");

            // ---- Equip non-equippable item should fail ----
            var potionEquipRejected = !service.TryEquip(charId, "health_potion", out var potionEquipErr);
            Require(potionEquipRejected, "health_potion is not equippable; equip should fail.");
            log.AppendLine($"  Non-equippable rejected: '{potionEquipErr}' — ok");

            // ---- Unequip ----
            Require(service.TryUnequip(charId, ProtoEquipSlot.OffHand, out var unequipped, out err), err);
            Require(string.Equals(unequipped, "wooden_shield", StringComparison.Ordinal),
                $"Expected 'wooden_shield' unequipped, got '{unequipped}'.");
            Require(string.IsNullOrEmpty(inv.GetEquippedItem(ProtoEquipSlot.OffHand)),
                "OffHand should be empty after unequip.");
            log.AppendLine("  Unequip shield — ok");

            // ---- Unequip already-empty slot should fail ----
            var emptyUnequipRejected = !service.TryUnequip(charId, ProtoEquipSlot.OffHand, out _, out var emptyErr);
            Require(emptyUnequipRejected, "Unequip of empty slot should be rejected.");
            log.AppendLine($"  Empty-slot unequip rejected: '{emptyErr}' — ok");

            // ---- Drop ----
            Require(service.TryDrop(charId, "health_potion", 3, out err), err);
            Require(inv.TotalItemCount == 4, $"Expected 4 after dropping 3 potions, got {inv.TotalItemCount}.");
            log.AppendLine("  Drop 3 of 5 potions → 2 remain — ok");

            // ---- Over-drop should fail ----
            var overDropRejected = !service.TryDrop(charId, "health_potion", 99, out var overDropErr);
            Require(overDropRejected, "Drop exceeding quantity should fail.");
            log.AppendLine($"  Over-drop rejected: '{overDropErr}' — ok");

            // ---- Unknown item pick-up should fail ----
            var unknownRejected = !service.TryPickUp(charId, "legendary_staff", 1, out var unknownErr);
            Require(unknownRejected, "Unknown item pick-up should fail.");
            log.AppendLine($"  Unknown item rejected: '{unknownErr}' — ok");

            // ---- Serialise / restore round-trip ----
            Require(service.TrySerializeInventory(charId, out var json, out err), err);
            Require(!string.IsNullOrWhiteSpace(json), "Serialised JSON should not be empty.");

            var service2 = new ProtoInventoryService();
            service2.RegisterItem(sword);
            service2.RegisterItem(shield);
            service2.RegisterItem(potion);
            Require(service2.TryRestoreInventory(charId, json, out err), err);
            var inv2 = service2.GetOrCreate(charId);
            Require(inv2.TotalItemCount == inv.TotalItemCount,
                $"Restored TotalItemCount {inv2.TotalItemCount} != original {inv.TotalItemCount}.");
            Require(string.Equals(inv2.GetEquippedItem(ProtoEquipSlot.MainHand), "iron_sword", StringComparison.Ordinal),
                "Serialise/restore did not preserve MainHand equipment.");
            log.AppendLine("  Serialise → restore round-trip — ok");

            log.AppendLine("=== Proto Inventory Smoke Test COMPLETE ===");
            return log.ToString();
        }

        private static void Require(bool condition, string error)
        {
            if (!condition)
                throw new InvalidOperationException(error);
        }
    }
}
