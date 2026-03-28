#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
WORLD_PROJECT_DIR="$ROOT_DIR/Assets/StreamingAssets/WorldProjects/starter_forest_edge"
DB_DIR="$WORLD_PROJECT_DIR/Databases"

if ! command -v sqlite3 >/dev/null 2>&1; then
  echo "sqlite3 is required but was not found on PATH."
  exit 1
fi

mkdir -p "$DB_DIR"

sqlite3 "$DB_DIR/world.db" < "$ROOT_DIR/Database/sql/shared/000_sqlite_pragmas.sql"
sqlite3 "$DB_DIR/world.db" < "$ROOT_DIR/Database/sql/world/001_init_world.sql"

sqlite3 "$DB_DIR/campaign.db" < "$ROOT_DIR/Database/sql/shared/000_sqlite_pragmas.sql"
sqlite3 "$DB_DIR/campaign.db" < "$ROOT_DIR/Database/sql/campaign/001_init_campaign.sql"
sqlite3 "$DB_DIR/campaign.db" < "$ROOT_DIR/Database/sql/campaign/002_runtime_persistence.sql"

echo "Initialized world.db and campaign.db at: $DB_DIR"
