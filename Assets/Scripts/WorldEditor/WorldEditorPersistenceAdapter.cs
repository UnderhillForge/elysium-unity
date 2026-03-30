using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Elysium.World;
using Elysium.World.Lua;
using UnityEngine;

namespace Elysium.WorldEditor
{
    /// Persists terrain and placeable state through WorldMutationService, backed by campaign.db.
    public sealed class WorldEditorPersistenceAdapter
    {
        private const string SnapshotKeyPrefix = "world.editor.snapshot";

        private readonly WorldMutationService worldMutationService;
        private readonly LuaSandboxPolicy writePolicy = new LuaSandboxPolicy
        {
            AllowWorldRead = true,
            AllowWorldWrite = true,
        };

        private readonly LuaSandboxPolicy readPolicy = new LuaSandboxPolicy
        {
            AllowWorldRead = true,
            AllowWorldWrite = false,
        };

        public WorldEditorPersistenceAdapter(WorldMutationService worldMutationService)
        {
            this.worldMutationService = worldMutationService ?? throw new ArgumentNullException(nameof(worldMutationService));
        }

        public bool TrySave(
            string requesterPlayerId,
            string areaId,
            Terrain terrain,
            IReadOnlyCollection<PlacedObjectSnapshot> placedObjects,
            out string error)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                error = "Terrain is not available.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(areaId))
            {
                error = "AreaId is required.";
                return false;
            }

            if (!TryCaptureSnapshot(areaId, terrain, placedObjects, out var snapshot, out error))
            {
                return false;
            }

            snapshot.editorPlayerId = requesterPlayerId ?? string.Empty;

            var json = JsonUtility.ToJson(snapshot);
            var key = BuildSnapshotKey(areaId);
            return worldMutationService.TryWriteState(requesterPlayerId, key, json, writePolicy, out error);
        }

        public bool TryLoad(
            string requesterPlayerId,
            string areaId,
            Terrain terrain,
            out WorldEditorAreaSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            if (terrain == null || terrain.terrainData == null)
            {
                error = "Terrain is not available.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(areaId))
            {
                error = "AreaId is required.";
                return false;
            }

            var key = BuildSnapshotKey(areaId);
            if (!worldMutationService.TryReadState(requesterPlayerId, key, readPolicy, enforceOwnership: false, out var json, out error))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                error = "No world editor snapshot found.";
                return false;
            }

            snapshot = JsonUtility.FromJson<WorldEditorAreaSnapshot>(json);
            if (snapshot == null)
            {
                error = "World editor snapshot json is invalid.";
                return false;
            }

            if (!TryApplyHeightSnapshot(terrain.terrainData, snapshot.terrainHeights, out error))
            {
                return false;
            }

            if (!TryApplyTextureSnapshot(terrain.terrainData, snapshot.terrainTextures, out error))
            {
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool TryCaptureSnapshot(
            string areaId,
            Terrain terrain,
            IReadOnlyCollection<PlacedObjectSnapshot> placedObjects,
            out WorldEditorAreaSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            if (terrain == null || terrain.terrainData == null)
            {
                error = "Terrain is not available.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(areaId))
            {
                error = "AreaId is required.";
                return false;
            }

            snapshot = new WorldEditorAreaSnapshot
            {
                areaId = areaId,
                updatedUtc = DateTime.UtcNow.ToString("O"),
                terrainHeights = CaptureHeightSnapshot(terrain.terrainData),
                terrainTextures = CaptureTextureSnapshot(terrain.terrainData),
                placedObjects = placedObjects == null
                    ? new List<PlacedObjectSnapshot>()
                    : ClonePlacedObjects(placedObjects),
            };

            error = string.Empty;
            return true;
        }

        public bool TryApplySnapshot(Terrain terrain, WorldEditorAreaSnapshot snapshot, out string error)
        {
            if (terrain == null || terrain.terrainData == null)
            {
                error = "Terrain is not available.";
                return false;
            }

            if (snapshot == null)
            {
                error = "Snapshot is null.";
                return false;
            }

            if (!TryApplyHeightSnapshot(terrain.terrainData, snapshot.terrainHeights, out error))
            {
                return false;
            }

            if (!TryApplyTextureSnapshot(terrain.terrainData, snapshot.terrainTextures, out error))
            {
                return false;
            }

            error = string.Empty;
            return true;
        }

        public static WorldEditorAreaSnapshot CloneSnapshot(WorldEditorAreaSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return null;
            }

            var json = JsonUtility.ToJson(snapshot);
            return JsonUtility.FromJson<WorldEditorAreaSnapshot>(json);
        }

        private static string BuildSnapshotKey(string areaId)
        {
            return $"{SnapshotKeyPrefix}.{areaId}";
        }

        private static List<PlacedObjectSnapshot> ClonePlacedObjects(IReadOnlyCollection<PlacedObjectSnapshot> source)
        {
            var list = new List<PlacedObjectSnapshot>(source.Count);
            foreach (var item in source)
            {
                if (item == null)
                {
                    continue;
                }

                list.Add(new PlacedObjectSnapshot
                {
                    placeableId = item.placeableId,
                    position = item.position,
                    rotation = item.rotation,
                    scale = item.scale,
                });
            }

            return list;
        }

        private static TerrainHeightSnapshot CaptureHeightSnapshot(TerrainData data)
        {
            var heights = data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution);
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                for (var y = 0; y < data.heightmapResolution; y++)
                {
                    for (var x = 0; x < data.heightmapResolution; x++)
                    {
                        writer.Write(heights[y, x]);
                    }
                }
            }

            return new TerrainHeightSnapshot
            {
                width = data.heightmapResolution,
                height = data.heightmapResolution,
                compressedBytesBase64 = CompressToBase64(stream.ToArray()),
            };
        }

        private static TerrainTextureSnapshot CaptureTextureSnapshot(TerrainData data)
        {
            var maps = data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                for (var y = 0; y < data.alphamapHeight; y++)
                {
                    for (var x = 0; x < data.alphamapWidth; x++)
                    {
                        for (var layer = 0; layer < data.alphamapLayers; layer++)
                        {
                            writer.Write(maps[y, x, layer]);
                        }
                    }
                }
            }

            return new TerrainTextureSnapshot
            {
                width = data.alphamapWidth,
                height = data.alphamapHeight,
                layers = data.alphamapLayers,
                compressedBytesBase64 = CompressToBase64(stream.ToArray()),
            };
        }

        private static bool TryApplyHeightSnapshot(TerrainData data, TerrainHeightSnapshot snapshot, out string error)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.compressedBytesBase64))
            {
                error = "Terrain height snapshot is missing.";
                return false;
            }

            if (snapshot.width != data.heightmapResolution || snapshot.height != data.heightmapResolution)
            {
                error = "Terrain height snapshot resolution does not match active terrain.";
                return false;
            }

            var rawBytes = DecompressFromBase64(snapshot.compressedBytesBase64);
            var heights = new float[snapshot.height, snapshot.width];
            using (var stream = new MemoryStream(rawBytes))
            using (var reader = new BinaryReader(stream))
            {
                for (var y = 0; y < snapshot.height; y++)
                {
                    for (var x = 0; x < snapshot.width; x++)
                    {
                        heights[y, x] = reader.ReadSingle();
                    }
                }
            }

            data.SetHeights(0, 0, heights);
            data.SyncHeightmap();
            error = string.Empty;
            return true;
        }

        private static bool TryApplyTextureSnapshot(TerrainData data, TerrainTextureSnapshot snapshot, out string error)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.compressedBytesBase64))
            {
                error = "Terrain texture snapshot is missing.";
                return false;
            }

            if (snapshot.width != data.alphamapWidth
                || snapshot.height != data.alphamapHeight
                || snapshot.layers != data.alphamapLayers)
            {
                error = "Terrain texture snapshot dimensions do not match active terrain.";
                return false;
            }

            var rawBytes = DecompressFromBase64(snapshot.compressedBytesBase64);
            var maps = new float[snapshot.height, snapshot.width, snapshot.layers];
            using (var stream = new MemoryStream(rawBytes))
            using (var reader = new BinaryReader(stream))
            {
                for (var y = 0; y < snapshot.height; y++)
                {
                    for (var x = 0; x < snapshot.width; x++)
                    {
                        for (var layer = 0; layer < snapshot.layers; layer++)
                        {
                            maps[y, x, layer] = reader.ReadSingle();
                        }
                    }
                }
            }

            data.SetAlphamaps(0, 0, maps);
            error = string.Empty;
            return true;
        }

        private static string CompressToBase64(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
            {
                return string.Empty;
            }

            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            {
                gzip.Write(bytes, 0, bytes.Length);
            }

            return Convert.ToBase64String(output.ToArray());
        }

        private static byte[] DecompressFromBase64(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
            {
                return Array.Empty<byte>();
            }

            var compressed = Convert.FromBase64String(base64);
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return output.ToArray();
        }
    }
}
