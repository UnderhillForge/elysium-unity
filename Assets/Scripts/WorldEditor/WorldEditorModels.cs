using System;
using System.Collections.Generic;
using UnityEngine;

namespace Elysium.WorldEditor
{
    public enum WorldEditorTool
    {
        Sculpt = 0,
        Paint = 1,
        Place = 2,
        Water = 3,
    }

    public enum SculptOperation
    {
        Raise = 0,
        Lower = 1,
        Smooth = 2,
        Flatten = 3,
    }

    [Serializable]
    public struct TerrainBrushRequest
    {
        public Vector3 WorldCenter;
        public float RadiusMeters;
        public float Strength;
        public float DeltaTime;
        public SculptOperation Operation;
    }

    [Serializable]
    public struct TexturePaintRequest
    {
        public Vector3 WorldCenter;
        public float RadiusMeters;
        public int LayerIndex;
        public float Opacity;
    }

    [Serializable]
    public struct PlaceableSpawnRequest
    {
        public string PlaceableId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }

    [Serializable]
    public struct WaterPlacementRequest
    {
        public Vector3 WorldCenter;
        public float RadiusMeters;
        public float WaterSurfaceY;
        public float CarveDepth;
    }

    [Serializable]
    public sealed class PlaceableDefinition
    {
        public string Id = string.Empty;
        public GameObject Prefab;
    }

    [Serializable]
    public sealed class PlacedObjectSnapshot
    {
        public string placeableId = string.Empty;
        public SerializableVector3 position;
        public SerializableQuaternion rotation;
        public SerializableVector3 scale;
    }

    [Serializable]
    public sealed class TerrainHeightSnapshot
    {
        public int width;
        public int height;
        public string compressedBytesBase64 = string.Empty;
    }

    [Serializable]
    public sealed class TerrainTextureSnapshot
    {
        public int width;
        public int height;
        public int layers;
        public string compressedBytesBase64 = string.Empty;
    }

    [Serializable]
    public sealed class WorldEditorAreaSnapshot
    {
        public string schemaVersion = "1";
        public string areaId = string.Empty;
        public string editorPlayerId = string.Empty;
        public string updatedUtc = string.Empty;
        public TerrainHeightSnapshot terrainHeights = new TerrainHeightSnapshot();
        public TerrainTextureSnapshot terrainTextures = new TerrainTextureSnapshot();
        public List<PlacedObjectSnapshot> placedObjects = new List<PlacedObjectSnapshot>();
    }

    [Serializable]
    public struct SerializableVector3
    {
        public float x;
        public float y;
        public float z;

        public static SerializableVector3 FromVector3(Vector3 value)
        {
            return new SerializableVector3
            {
                x = value.x,
                y = value.y,
                z = value.z,
            };
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
    }

    [Serializable]
    public struct SerializableQuaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public static SerializableQuaternion FromQuaternion(Quaternion value)
        {
            return new SerializableQuaternion
            {
                x = value.x,
                y = value.y,
                z = value.z,
                w = value.w,
            };
        }

        public Quaternion ToQuaternion()
        {
            return new Quaternion(x, y, z, w);
        }
    }
}
