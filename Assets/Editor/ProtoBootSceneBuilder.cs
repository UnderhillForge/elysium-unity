using Elysium.Boot;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Elysium.Editor
{
    public static class ProtoBootSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Proto/ProtoBoot.unity";
        private const string SourceScenePath = "Assets/RPGPP_LT/Scene/rpgpp_lt_scene_1.0.unity";
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

            var scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);

            // Remove previous bootstrap instances if this builder is run repeatedly.
            var existingBootstraps = Object.FindObjectsByType<ProtoPlayableBootstrap>();
            for (var i = 0; i < existingBootstraps.Length; i++)
            {
                Object.DestroyImmediate(existingBootstraps[i].gameObject);
            }

            EnsureMainCamera();

            var bootstrapObject = new GameObject("ProtoPlayableBootstrap");
            var bootstrap = bootstrapObject.AddComponent<ProtoPlayableBootstrap>();

            var donorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DonorPrefabPath);
            if (donorPrefab == null)
            {
                throw new System.InvalidOperationException($"Could not load donor prefab at '{DonorPrefabPath}'.");
            }

            var bootstrapSerialized = new SerializedObject(bootstrap);
            bootstrapSerialized.FindProperty("donorCharacterPrefab").objectReferenceValue = donorPrefab;
            bootstrapSerialized.FindProperty("donorPrefabReferencePath").stringValue = DonorPrefabPath;
            bootstrapSerialized.FindProperty("worldProjectFolder").stringValue = "proto_village_square";
            bootstrapSerialized.FindProperty("sessionId").stringValue = "proto_playable_001";
            bootstrapSerialized.FindProperty("localPlayerId").stringValue = "gm_001";
            bootstrapSerialized.FindProperty("localDisplayName").stringValue = "Local Prototype Host";
            bootstrapSerialized.FindProperty("localPlayerActsAsGm").boolValue = true;
            bootstrapSerialized.FindProperty("moveSpeed").floatValue = 4.5f;
            bootstrapSerialized.FindProperty("cameraOffset").vector3Value = new Vector3(0f, 3.25f, -5.5f);
            bootstrapSerialized.FindProperty("bootOnStart").boolValue = true;
            bootstrapSerialized.ApplyModifiedPropertiesWithoutUndo();

            var hudObject = new GameObject("ProtoPlayableDebugHud");
            var hud = hudObject.AddComponent<ProtoPlayableDebugHud>();
            var hudSerialized = new SerializedObject(hud);
            hudSerialized.FindProperty("bootstrap").objectReferenceValue = bootstrap;
            hudSerialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[ProtoBootSceneBuilder] Scene created from '{SourceScenePath}': {ScenePath}");
        }

        private static void EnsureMainCamera()
        {
            if (Camera.main != null)
            {
                return;
            }

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.transform.position = new Vector3(0f, 3.25f, -5.5f);
            cameraObject.transform.rotation = Quaternion.Euler(18f, 0f, 0f);
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