using System;
using System.Collections.Generic;

namespace Elysium.Prototype.Inventory
{
    /// In-memory inventory service for the prototype lane.
    /// Manages per-character ProtoInventoryRecord instances, item definition
    /// lookup, pick-up, drop, equip and unequip operations.
    ///
    /// Persistence boundary: call TrySerializeInventory / TryRestoreInventory
    /// to exchange JSON blobs with CampaignPersistenceService campaign_metadata
    /// rows (key prefix "proto.inventory.<characterId>").
    public sealed class ProtoInventoryService
    {
        private readonly Dictionary<string, ProtoItemDefinition> _definitions =
            new Dictionary<string, ProtoItemDefinition>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, ProtoInventoryRecord> _inventories =
            new Dictionary<string, ProtoInventoryRecord>(StringComparer.Ordinal);

        // ------------------------------------------------------------------ //
        // Item Definition Registry
        // ------------------------------------------------------------------ //

        /// Register a shareable item definition. Safe to call multiple times
        /// with the same id — later registration wins.
        public void RegisterItem(ProtoItemDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            _definitions[definition.ItemId] = definition;
        }

        public bool TryGetDefinition(string itemId, out ProtoItemDefinition definition)
            => _definitions.TryGetValue(itemId ?? string.Empty, out definition);

        public IReadOnlyDictionary<string, ProtoItemDefinition> AllDefinitions => _definitions;

        // ------------------------------------------------------------------ //
        // Inventory Access
        // ------------------------------------------------------------------ //

        /// Return the inventory for a character (creates empty record on first access).
        public ProtoInventoryRecord GetOrCreate(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must not be empty.", nameof(characterId));

            if (!_inventories.TryGetValue(characterId, out var record))
            {
                record = new ProtoInventoryRecord(characterId);
                _inventories[characterId] = record;
            }

            return record;
        }

        // ------------------------------------------------------------------ //
        // Pick-up / Drop
        // ------------------------------------------------------------------ //

        public bool TryPickUp(
            string characterId,
            string itemId,
            int quantity,
            out string error)
        {
            error = string.Empty;

            if (!_definitions.ContainsKey(itemId ?? string.Empty))
            {
                error = $"Unknown item '{itemId}'. Register the definition first.";
                return false;
            }

            if (quantity <= 0)
            {
                error = "Quantity must be > 0.";
                return false;
            }

            GetOrCreate(characterId).AddItem(itemId, quantity);
            return true;
        }

        public bool TryDrop(
            string characterId,
            string itemId,
            int quantity,
            out string error)
        {
            var record = GetOrCreate(characterId);
            return record.TryRemoveItem(itemId, quantity, out error);
        }

        // ------------------------------------------------------------------ //
        // Equip / Unequip
        // ------------------------------------------------------------------ //

        public bool TryEquip(
            string characterId,
            string itemId,
            out string error)
        {
            error = string.Empty;

            if (!TryGetDefinition(itemId, out var def))
            {
                error = $"Unknown item '{itemId}'.";
                return false;
            }

            if (!def.IsEquippable)
            {
                error = $"Item '{itemId}' is not equippable (EquipSlot = None).";
                return false;
            }

            return GetOrCreate(characterId).TryEquip(itemId, def.EquipSlot, out error);
        }

        public bool TryUnequip(
            string characterId,
            ProtoEquipSlot slot,
            out string unequippedItemId,
            out string error)
        {
            return GetOrCreate(characterId).TryUnequip(slot, out unequippedItemId, out error);
        }

        // ------------------------------------------------------------------ //
        // Simple JSON persistence helpers
        // (Actual write is done by CampaignPersistenceService; these helpers
        //  produce/consume the value blob stored under the metadata key.)
        // ------------------------------------------------------------------ //

        public bool TrySerializeInventory(
            string characterId,
            out string json,
            out string error)
        {
            error = string.Empty;
            json  = string.Empty;

            if (!_inventories.TryGetValue(characterId ?? string.Empty, out var record))
            {
                error = $"No inventory loaded for character '{characterId}'.";
                return false;
            }

            var snapshot = ProtoInventorySnapshot.From(record);
            try
            {
                json = UnityEngine.JsonUtility.ToJson(snapshot);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Serialisation failed: {ex.Message}";
                return false;
            }
        }

        public bool TryRestoreInventory(
            string characterId,
            string json,
            out string error)
        {
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "json blob is empty.";
                return false;
            }

            ProtoInventorySnapshot snapshot;
            try
            {
                snapshot = UnityEngine.JsonUtility.FromJson<ProtoInventorySnapshot>(json);
            }
            catch (Exception ex)
            {
                error = $"Deserialisation failed: {ex.Message}";
                return false;
            }

            var record = GetOrCreate(characterId);
            if (snapshot.items != null)
            {
                for (var i = 0; i < snapshot.items.Count; i++)
                    record.AddItem(snapshot.items[i].itemId, snapshot.items[i].quantity);
            }

            if (snapshot.equipped != null)
            {
                for (var i = 0; i < snapshot.equipped.Count; i++)
                {
                    var e = snapshot.equipped[i];
                    if (Enum.TryParse<ProtoEquipSlot>(e.slot, out var slotEnum))
                        record.TryEquip(e.itemId, slotEnum, out _);
                }
            }

            return true;
        }
    }

    // ---------------------------------------------------------------------- //
    // JSON-friendly snapshot structures (serialised by JsonUtility)
    // ---------------------------------------------------------------------- //

    [Serializable]
    internal sealed class ProtoInventorySnapshot
    {
        public string characterId;
        public List<ItemEntry> items    = new List<ItemEntry>();
        public List<SlotEntry> equipped = new List<SlotEntry>();

        [Serializable]
        internal sealed class ItemEntry
        {
            public string itemId;
            public int    quantity;
        }

        [Serializable]
        internal sealed class SlotEntry
        {
            public string slot;
            public string itemId;
        }

        internal static ProtoInventorySnapshot From(ProtoInventoryRecord record)
        {
            var snap = new ProtoInventorySnapshot { characterId = record.CharacterId };

            for (var i = 0; i < record.Entries.Count; i++)
            {
                snap.items.Add(new ItemEntry
                {
                    itemId   = record.Entries[i].ItemId,
                    quantity = record.Entries[i].Quantity,
                });
            }

            foreach (var kv in record.EquippedSlots)
            {
                snap.equipped.Add(new SlotEntry
                {
                    slot   = kv.Key.ToString(),
                    itemId = kv.Value,
                });
            }

            return snap;
        }
    }
}
