using UnityEngine;

namespace Elysium.WorldEditor
{
    /// Helpers for modifying TerrainData safely inside a world-space radius.
    public static class WorldEditorTerrainBrushes
    {
        public static bool TryApplySculpt(
            Terrain terrain,
            in TerrainBrushRequest request,
            out RectInt changedHeightRect)
        {
            changedHeightRect = default;
            if (terrain == null || terrain.terrainData == null)
            {
                return false;
            }

            if (request.RadiusMeters <= 0.01f)
            {
                return false;
            }

            var data = terrain.terrainData;
            var rect = ResolveHeightRect(terrain, request.WorldCenter, request.RadiusMeters);
            if (rect.width <= 0 || rect.height <= 0)
            {
                return false;
            }

            var heights = data.GetHeights(rect.x, rect.y, rect.width, rect.height);
            var targetHeight = ResolveNormalizedTargetHeight(terrain, request.WorldCenter.y);
            var signedStrength = Mathf.Clamp(request.Strength, 0f, 1f) * Mathf.Max(0.005f, request.DeltaTime);

            for (var y = 0; y < rect.height; y++)
            {
                for (var x = 0; x < rect.width; x++)
                {
                    var tx = (float)(rect.x + x) / (data.heightmapResolution - 1);
                    var ty = (float)(rect.y + y) / (data.heightmapResolution - 1);

                    var worldX = terrain.transform.position.x + tx * data.size.x;
                    var worldZ = terrain.transform.position.z + ty * data.size.z;
                    var distance = Vector2.Distance(new Vector2(worldX, worldZ), new Vector2(request.WorldCenter.x, request.WorldCenter.z));
                    if (distance > request.RadiusMeters)
                    {
                        continue;
                    }

                    var falloff = 1f - Mathf.Clamp01(distance / request.RadiusMeters);
                    var weight = falloff * falloff;
                    var current = heights[y, x];
                    float next;
                    switch (request.Operation)
                    {
                        case SculptOperation.Raise:
                            next = current + signedStrength * weight;
                            break;
                        case SculptOperation.Lower:
                            next = current - signedStrength * weight;
                            break;
                        case SculptOperation.Smooth:
                            next = Mathf.Lerp(current, targetHeight, signedStrength * 0.5f * weight);
                            break;
                        case SculptOperation.Flatten:
                            next = Mathf.Lerp(current, targetHeight, signedStrength * weight);
                            break;
                        default:
                            next = current;
                            break;
                    }

                    heights[y, x] = Mathf.Clamp01(next);
                }
            }

            data.SetHeightsDelayLOD(rect.x, rect.y, heights);
            changedHeightRect = rect;
            return true;
        }

        public static bool TryApplyTexturePaint(
            Terrain terrain,
            in TexturePaintRequest request,
            out RectInt changedAlphaRect)
        {
            changedAlphaRect = default;
            if (terrain == null || terrain.terrainData == null)
            {
                return false;
            }

            var data = terrain.terrainData;
            if (request.LayerIndex < 0 || request.LayerIndex >= data.alphamapLayers || request.RadiusMeters <= 0.01f)
            {
                return false;
            }

            var rect = ResolveAlphaRect(terrain, request.WorldCenter, request.RadiusMeters);
            if (rect.width <= 0 || rect.height <= 0)
            {
                return false;
            }

            var maps = data.GetAlphamaps(rect.x, rect.y, rect.width, rect.height);
            for (var y = 0; y < rect.height; y++)
            {
                for (var x = 0; x < rect.width; x++)
                {
                    var tx = (float)(rect.x + x) / Mathf.Max(1, data.alphamapWidth - 1);
                    var ty = (float)(rect.y + y) / Mathf.Max(1, data.alphamapHeight - 1);
                    var worldX = terrain.transform.position.x + tx * data.size.x;
                    var worldZ = terrain.transform.position.z + ty * data.size.z;
                    var distance = Vector2.Distance(new Vector2(worldX, worldZ), new Vector2(request.WorldCenter.x, request.WorldCenter.z));
                    if (distance > request.RadiusMeters)
                    {
                        continue;
                    }

                    var falloff = 1f - Mathf.Clamp01(distance / request.RadiusMeters);
                    var blend = Mathf.Clamp01(request.Opacity) * falloff;

                    var remainder = 0f;
                    for (var layer = 0; layer < data.alphamapLayers; layer++)
                    {
                        if (layer == request.LayerIndex)
                        {
                            continue;
                        }

                        remainder += maps[y, x, layer];
                    }

                    maps[y, x, request.LayerIndex] = Mathf.Clamp01(maps[y, x, request.LayerIndex] + blend);
                    var targetRemainder = Mathf.Max(0f, 1f - maps[y, x, request.LayerIndex]);
                    if (remainder <= 0.0001f)
                    {
                        for (var layer = 0; layer < data.alphamapLayers; layer++)
                        {
                            if (layer != request.LayerIndex)
                            {
                                maps[y, x, layer] = targetRemainder / Mathf.Max(1, data.alphamapLayers - 1);
                            }
                        }
                    }
                    else
                    {
                        var scale = targetRemainder / remainder;
                        for (var layer = 0; layer < data.alphamapLayers; layer++)
                        {
                            if (layer != request.LayerIndex)
                            {
                                maps[y, x, layer] *= scale;
                            }
                        }
                    }
                }
            }

            data.SetAlphamaps(rect.x, rect.y, maps);
            changedAlphaRect = rect;
            return true;
        }

        private static RectInt ResolveHeightRect(Terrain terrain, Vector3 worldCenter, float radiusMeters)
        {
            var data = terrain.terrainData;
            var minX = WorldToHeightIndexX(terrain, worldCenter.x - radiusMeters);
            var maxX = WorldToHeightIndexX(terrain, worldCenter.x + radiusMeters);
            var minY = WorldToHeightIndexY(terrain, worldCenter.z - radiusMeters);
            var maxY = WorldToHeightIndexY(terrain, worldCenter.z + radiusMeters);

            minX = Mathf.Clamp(minX, 0, data.heightmapResolution - 1);
            maxX = Mathf.Clamp(maxX, 0, data.heightmapResolution - 1);
            minY = Mathf.Clamp(minY, 0, data.heightmapResolution - 1);
            maxY = Mathf.Clamp(maxY, 0, data.heightmapResolution - 1);

            return new RectInt(minX, minY, Mathf.Max(1, maxX - minX + 1), Mathf.Max(1, maxY - minY + 1));
        }

        private static RectInt ResolveAlphaRect(Terrain terrain, Vector3 worldCenter, float radiusMeters)
        {
            var data = terrain.terrainData;
            var minX = WorldToAlphaIndexX(terrain, worldCenter.x - radiusMeters);
            var maxX = WorldToAlphaIndexX(terrain, worldCenter.x + radiusMeters);
            var minY = WorldToAlphaIndexY(terrain, worldCenter.z - radiusMeters);
            var maxY = WorldToAlphaIndexY(terrain, worldCenter.z + radiusMeters);

            minX = Mathf.Clamp(minX, 0, data.alphamapWidth - 1);
            maxX = Mathf.Clamp(maxX, 0, data.alphamapWidth - 1);
            minY = Mathf.Clamp(minY, 0, data.alphamapHeight - 1);
            maxY = Mathf.Clamp(maxY, 0, data.alphamapHeight - 1);

            return new RectInt(minX, minY, Mathf.Max(1, maxX - minX + 1), Mathf.Max(1, maxY - minY + 1));
        }

        private static int WorldToHeightIndexX(Terrain terrain, float worldX)
        {
            var data = terrain.terrainData;
            var localX = worldX - terrain.transform.position.x;
            var normalizedX = Mathf.Clamp01(localX / Mathf.Max(0.001f, data.size.x));
            return Mathf.RoundToInt(normalizedX * (data.heightmapResolution - 1));
        }

        private static int WorldToHeightIndexY(Terrain terrain, float worldZ)
        {
            var data = terrain.terrainData;
            var localZ = worldZ - terrain.transform.position.z;
            var normalizedZ = Mathf.Clamp01(localZ / Mathf.Max(0.001f, data.size.z));
            return Mathf.RoundToInt(normalizedZ * (data.heightmapResolution - 1));
        }

        private static int WorldToAlphaIndexX(Terrain terrain, float worldX)
        {
            var data = terrain.terrainData;
            var localX = worldX - terrain.transform.position.x;
            var normalizedX = Mathf.Clamp01(localX / Mathf.Max(0.001f, data.size.x));
            return Mathf.RoundToInt(normalizedX * (data.alphamapWidth - 1));
        }

        private static int WorldToAlphaIndexY(Terrain terrain, float worldZ)
        {
            var data = terrain.terrainData;
            var localZ = worldZ - terrain.transform.position.z;
            var normalizedZ = Mathf.Clamp01(localZ / Mathf.Max(0.001f, data.size.z));
            return Mathf.RoundToInt(normalizedZ * (data.alphamapHeight - 1));
        }

        private static float ResolveNormalizedTargetHeight(Terrain terrain, float worldHeight)
        {
            var data = terrain.terrainData;
            var localHeight = worldHeight - terrain.transform.position.y;
            return Mathf.Clamp01(localHeight / Mathf.Max(0.001f, data.size.y));
        }
    }
}
