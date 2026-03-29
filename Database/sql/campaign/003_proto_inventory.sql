-- Migration 003: Prototype Inventory Tables
-- Additive only — no DROP, ALTER, or modification of existing rows.
-- Stores per-character item inventories and equipped-slot state for the
-- prototype lane.  The JSON blob columns mirror ProtoInventorySnapshot
-- produced by ProtoInventoryService.TrySerializeInventory.

BEGIN TRANSACTION;

CREATE TABLE IF NOT EXISTS proto_character_inventory (
    character_id  TEXT NOT NULL,
    item_id       TEXT NOT NULL,
    quantity      INTEGER NOT NULL DEFAULT 1 CHECK (quantity > 0),
    added_utc     TEXT NOT NULL,
    PRIMARY KEY (character_id, item_id)
);

CREATE TABLE IF NOT EXISTS proto_character_equipment (
    character_id  TEXT NOT NULL,
    equip_slot    TEXT NOT NULL,   -- matches ProtoEquipSlot enum name
    item_id       TEXT NOT NULL,
    equipped_utc  TEXT NOT NULL,
    PRIMARY KEY (character_id, equip_slot)
);

-- Indexes for fast per-character lookups.
CREATE INDEX IF NOT EXISTS idx_proto_inventory_character
    ON proto_character_inventory (character_id);

CREATE INDEX IF NOT EXISTS idx_proto_equipment_character
    ON proto_character_equipment (character_id);

INSERT OR REPLACE INTO schema_migrations (version, description, applied_utc)
VALUES (3, 'Prototype inventory and equipment tables',
        strftime('%Y-%m-%dT%H:%M:%fZ', 'now'));

COMMIT;
