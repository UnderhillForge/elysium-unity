using Elysium.Boot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Elysium.Editor
{
    public static class ProtoBootSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Proto/ProtoBoot.unity";
        private const string DonorPrefabPath = "Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack FREE/Prefabs/Character/RPG-Character.prefab";

        [MenuItem("Elysium/Prototype/Build Proto Boot Scene")]
        public static void CreateOrUpdateProtoBootSceneMenu()
        {
            CreateOrUpdateProtoBootScene();
        }

        public static void CreateOrUpdateProtoBootScene()
        {
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/Scenes/Proto");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var lightObject = new GameObject("Directional Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(4f, 1f, 4f);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.transform.position = new Vector3(0f, 3.25f, -5.5f);
            cameraObject.transform.rotation = Quaternion.Euler(18f, 0f, 0f);

            var bootstrapObject = new GameObject("ProtoPlayableBootstrap");
            var bootstrap = bootstrapObject.AddComponent<ProtoPlayableBootstrap>();

            var donorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPrefabPath);
            if (donorPrefab == null)
            {
                throw new System.InvalidOperationException($"Could not load donor prefab at '{DonorPrefabPath}'.");
            }

            var bootstrapSerialized = new SerializedObject(bootstrap);
            bootstrapSerialized.FindProperty("donorCharacterPrefab").objectReferenceValue = donorPrefab;
            bootstrapSerialized.FindProperty("worldProjectFolder").stringValue = "proto_village_square";
            bootstrapSerialized.FindProperty("sessionId").stringValue = "proto_playable_001";
            bootstrapSerialized.FindProperty("localPlayerId").stringValue = "gm_001";
            bootstrapSerialized.FindProperty("localDisplayName").stringValue = "Local Prototype Host";
            bootstrapSerialized.FindProperty("localPlayerActsAsGm").boolValue = true;
            bootstrapSerialized.FindProperty("moveSpeed").floatValue = 4.5f;
            bootstrapSerialized.FindProperty("cameraOffset").vector3Value = new Vector3(0f, 3.25f, -5.5f);
            bootstrapSerialized.FindProperty("bootOnStart").boolValue = true;
            bootstrapSerialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ProtoBootSceneBuilder] Scene created: {ScenePath}");
        }

        private static void EnsureFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            var lastSlash = folderPath.LastIndexOf('/');
            if (lastSlash <= 0)
            {
                return;
            }

            var parent = folderPath.Substring(0, lastSlash);
            var child = folderPath.Substring(lastSlash + 1);
            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, child);
        }
    }
}