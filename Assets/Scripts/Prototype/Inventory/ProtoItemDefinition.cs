using System;

namespace Elysium.Prototype.Inventory
{
    /// Equipment slot categories supported by the prototype inventory layer.
    public enum ProtoEquipSlot
    {
        None      = 0,
        MainHand  = 1,
        OffHand   = 2,
        Head      = 3,
        Body      = 4,
        Feet      = 5,
        Accessory = 6,
    }

    /// Immutable item definition. Shared across all character inventories in
    /// the prototype; does not carry per-character mutable state.
    [Serializable]
    public sealed class ProtoItemDefinition
    {
        public string ItemId      { get; }
        public string DisplayName { get; }
        public ProtoEquipSlot EquipSlot { get; }
        public string Description { get; }

        public ProtoItemDefinition(
            string itemId,
            string displayName,
            ProtoEquipSlot equipSlot       = ProtoEquipSlot.None,
            string description             = "")
        {
            if (string.IsNullOrWhiteSpace(itemId))
                throw new ArgumentException("itemId must not be empty.", nameof(itemId));
            if (string.IsNullOrWhiteSpace(displayName))
                throw new ArgumentException("displayName must not be empty.", nameof(displayName));

            ItemId      = itemId;
            DisplayName = displayName;
            EquipSlot   = equipSlot;
            Description = description ?? string.Empty;
        }

        public bool IsEquippable => EquipSlot != ProtoEquipSlot.None;
    }
}
