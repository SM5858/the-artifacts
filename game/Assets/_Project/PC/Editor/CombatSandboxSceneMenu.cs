using System.IO;
using System.Linq;
using TheArtifacts.PC.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheArtifacts.PC.Editor
{
    /// <summary>Creates the graybox combat scene so it never has to be hand-edited as YAML.</summary>
    public static class CombatSandboxSceneMenu
    {
        const string ScenePath = "Assets/_Project/PC/Scenes/CombatSandbox.unity";

        [MenuItem("The Artifacts/Create Combat Sandbox Scene")]
        public static void Create()
        {
            if (File.Exists(ScenePath) &&
                !EditorUtility.DisplayDialog("Combat Sandbox", $"{ScenePath} already exists. Replace it?", "Replace", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("CombatSandbox").AddComponent<CombatSandbox>();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);

            var scenes = EditorBuildSettings.scenes.Where(s => s.path != ScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            Debug.Log($"Created {ScenePath} and made it the first scene in Build Settings. Press Play to fight.");
        }

        [MenuItem("The Artifacts/Open Combat Sandbox Scene")]
        public static void Open()
        {
            if (!File.Exists(ScenePath)) { Create(); return; }
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }
    }
}
