using System;
using System.Collections.Generic;

namespace Elysium.Prototype.Inventory
{
    /// Per-character inventory state for the prototype lane.
    /// Tracks carried items and which equipment slots are filled.
    /// Designed to serialise to JSON for SQLite storage via CampaignPersistenceService.
    public sealed class ProtoInventoryRecord
    {
        private readonly string _characterId;
        private readonly List<ProtoInventoryEntry> _entries = new List<ProtoInventoryEntry>();
        private readonly Dictionary<ProtoEquipSlot, string> _equippedSlots =
            new Dictionary<ProtoEquipSlot, string>();

        public string CharacterId => _characterId;
        public IReadOnlyList<ProtoInventoryEntry> Entries => _entries;
        public IReadOnlyDictionary<ProtoEquipSlot, string> EquippedSlots => _equippedSlots;

        public ProtoInventoryRecord(string characterId)
        {
            if (string.IsNullOrWhiteSpace(characterId))
                throw new ArgumentException("characterId must not be empty.", nameof(characterId));
            _characterId = characterId;
        }

        /// Total count of items in the inventory across all stacks.
        public int TotalItemCount
        {
            get
            {
                var total = 0;
                for (var i = 0; i < _entries.Count; i++)
                    total += _entries[i].Quantity;
                return total;
            }
        }

        /// Add <paramref name="quantity"/> units of item to the inventory.
        /// Stacks with an existing entry for the same itemId, or adds a new entry.
        internal void AddItem(string itemId, int quantity)
        {
            if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0)
                return;

            for (var i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].ItemId, itemId, StringComparison.Ordinal))
                {
                    _entries[i] = new ProtoInventoryEntry(itemId, _entries[i].Quantity + quantity);
                    return;
                }
            }

            _entries.Add(new ProtoInventoryEntry(itemId, quantity));
        }

        /// Remove <paramref name="quantity"/> units. Returns false if insufficient stock.
        internal bool TryRemoveItem(string itemId, int quantity, out string error)
        {
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(itemId) || quantity <= 0)
            {
                error = "itemId must not be empty and quantity must be > 0.";
                return false;
            }

            for (var i = 0; i < _entries.Count; i++)
            {
                if (!string.Equals(_entries[i].ItemId, itemId, StringComparison.Ordinal))
                    continue;

                if (_entries[i].Quantity < quantity)
                {
                    error = $"Insufficient quantity of '{itemId}' (have {_entries[i].Quantity}, need {quantity}).";
                    return false;
                }

                var newQty = _entries[i].Quantity - quantity;
                if (newQty == 0)
                    _entries.RemoveAt(i);
                else
                    _entries[i] = new ProtoInventoryEntry(itemId, newQty);
                return true;
            }

            error = $"Item '{itemId}' not found in inventory.";
            return false;
        }

        /// Equip an item into its designated slot. Item must be in inventory.
        internal bool TryEquip(string itemId, ProtoEquipSlot slot, out string error)
        {
            error = string.Empty;
            if (slot == ProtoEquipSlot.None)
            {
                error = "Cannot equip to the None slot.";
                return false;
            }

            var found = false;
            for (var i = 0; i < _entries.Count; i++)
            {
                if (string.Equals(_entries[i].ItemId, itemId, StringComparison.Ordinal))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                error = $"Item '{itemId}' is not in this inventory.";
                return false;
            }

            _equippedSlots[slot] = itemId;
            return true;
        }

        /// Unequip whatever is in the given slot. No-ops if slot is empty.
        internal bool TryUnequip(ProtoEquipSlot slot, out string unequippedItemId, out string error)
        {
            error = string.Empty;
            unequippedItemId = string.Empty;

            if (slot == ProtoEquipSlot.None)
            {
                error = "Cannot unequip from the None slot.";
                return false;
            }

            if (!_equippedSlots.TryGetValue(slot, out var current))
            {
                error = $"Slot '{slot}' is already empty.";
                return false;
            }

            unequippedItemId = current;
            _equippedSlots.Remove(slot);
            return true;
        }

        /// Returns the itemId equipping the slot, or empty string if vacant.
        public string GetEquippedItem(ProtoEquipSlot slot)
        {
            _equippedSlots.TryGetValue(slot, out var id);
            return id ?? string.Empty;
        }
    }

    /// Single item stack entry inside a ProtoInventoryRecord.
    public readonly struct ProtoInventoryEntry
    {
        public string ItemId   { get; }
        public int    Quantity { get; }

        public ProtoInventoryEntry(string itemId, int quantity)
        {
            ItemId   = itemId;
            Quantity = quantity;
        }
    }
}
