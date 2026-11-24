using System.IO;
using System.Linq;
using TheArtifacts.PC.Combat;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TheArtifacts.PC.Editor
{
    /// <summary>
    /// Turns the CC0 texture sets in Art/Materials into URP Lit materials and a <see cref="SandboxArt"/> asset,
    /// and gives it to the combat sandbox. Each set is a folder named Name with Name_BaseColor, Name_Normal,
    /// Name_Roughness, and optionally Name_Metallic and Name_AO. Sources and licenses: GDD Section 12.
    /// </summary>
    public static class SandboxArtMenu
    {
        public const string MaterialsRoot = "Assets/_Project/Art/Materials";
        public const string ArtAssetPath = "Assets/_Project/Art/SandboxArt.asset";
        /// <summary>Guardian blockouts exported by tools/blender/guardian_blockouts.py, one FBX per Guardian id.</summary>
        public const string GuardianModelsRoot = "Assets/_Project/Art/Models/Guardians";
        /// <summary>The player's rigged model (tools/blender/prepare_generated.py and rig_guardians.py).</summary>
        public const string PlayerModelPath = "Assets/_Project/Art/Models/Player/player.fbx";

        // (texture set, tiling)
        static readonly (string Name, float Tiling)[] Sets =
        {
            ("StoneFloor", 10f), ("OakWood", 1f), ("GolemStone", 1f), ("AgedBronze", 1f), ("DarkBronze", 1f), ("Verdigris", 1f),
            // Generated Guardian models' own texture sets (UV-mapped, so no tiling): tools/blender/prepare_generated.py.
            ("AlmaMater", 1f), ("ColumbiaLion", 1f), ("BookKeeper", 1f), ("Player", 1f),
        };

        [MenuItem("The Artifacts/Build Sandbox Materials")]
        public static void Build()
        {
            foreach (var (name, tiling) in Sets) BuildMaterial(name, tiling);

            var art = AssetDatabase.LoadAssetAtPath<SandboxArt>(ArtAssetPath);
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<SandboxArt>();
                AssetDatabase.CreateAsset(art, ArtAssetPath);
            }
            art.Arena = MaterialAt("StoneFloor");
            art.Bookshelf = MaterialAt("OakWood");
            foreach (var id in new[] { "alma_mater", "columbia_lion", "book_keeper" }) EnsureSkinnedImport($"{GuardianModelsRoot}/{id}.fbx");
            EnsureSkinnedImport(PlayerModelPath);
            art.Guardians = new[]
            {
                new SandboxArt.GuardianMaterial { GuardianId = "alma_mater", Material = MaterialAt("AlmaMater"), Model = ModelAt("alma_mater") },
                new SandboxArt.GuardianMaterial { GuardianId = "columbia_lion", Material = MaterialAt("ColumbiaLion"), Model = ModelAt("columbia_lion") },
                new SandboxArt.GuardianMaterial { GuardianId = "book_keeper", Material = MaterialAt("BookKeeper"), Model = ModelAt("book_keeper") },
            };
            art.PlayerModel = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModelPath);
            art.Player = MaterialAt("Player");
            EditorUtility.SetDirty(art);
            AssetDatabase.SaveAssets();

            var assigned = AssignToOpenSandbox(art);
            Debug.Log($"Built sandbox materials and {ArtAssetPath}." +
                      (assigned ? " Assigned to the open combat sandbox scene." : " Open the combat sandbox scene and run this again to assign it."));
        }

        /// <summary>Gives the art asset to the CombatSandbox in the open scene, if there is one, and saves the scene.</summary>
        public static bool AssignToOpenSandbox(SandboxArt art)
        {
            var sandbox = Object.FindFirstObjectByType<CombatSandbox>();
            if (sandbox == null || art == null) return false;
            var so = new SerializedObject(sandbox);
            so.FindProperty("art").objectReferenceValue = art;
            so.ApplyModifiedProperties();
            EditorSceneManager.MarkSceneDirty(sandbox.gameObject.scene);
            EditorSceneManager.SaveScene(sandbox.gameObject.scene);
            return true;
        }

        static string Map(string name, string map) =>
            Directory.GetFiles(Path.Combine(MaterialsRoot, name), $"{name}_{map}.*")
                .Where(p => !p.EndsWith(".meta")).Select(p => p.Replace('\\', '/')).FirstOrDefault();

        /// <summary>
        /// Writes the rigged-model import settings into the FBX's .meta and reimports it if they differ. An older .meta
        /// with no rig (animationType None) imports the model as a plain mesh, and its bones then move nothing.
        /// </summary>
        static void EnsureSkinnedImport(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) return;
            if (importer.animationType == ModelImporterAnimationType.Generic && importer.bakeAxisConversion &&
                !importer.optimizeGameObjects && !importer.importAnimation) return;
            ArtTextureImporter.ApplyGuardianModelSettings(importer);
            importer.SaveAndReimport();
            Debug.Log($"{path}: import settings set to a generic rig (skinned) and reimported.");
        }

        static GameObject ModelAt(string guardianId) =>
            AssetDatabase.LoadAssetAtPath<GameObject>($"{GuardianModelsRoot}/{guardianId}.fbx");

        static Material MaterialAt(string name) =>
            AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsRoot}/{name}/{name}.mat");

        static void BuildMaterial(string name, float tiling)
        {
            var baseColor = Map(name, "BaseColor");
            var normal = Map(name, "Normal");
            var roughness = Map(name, "Roughness");
            var metallic = Map(name, "Metallic");
            var ao = Map(name, "AO");
            if (baseColor == null) { Debug.LogWarning($"No base color for {name}; skipped."); return; }

            // Make sure the import settings from ArtTextureImporter are applied before the textures are used.
            foreach (var p in new[] { baseColor, normal, roughness, metallic, ao }.Where(p => p != null))
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);

            var packed = roughness != null ? PackMetallicSmoothness(name, metallic, roughness) : null;

            var path = $"{MaterialsRoot}/{name}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }

            SetTexture(mat, "_BaseMap", baseColor);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));

            if (normal != null)
            {
                SetTexture(mat, "_BumpMap", normal);
                mat.SetFloat("_BumpScale", 1f);
                mat.EnableKeyword("_NORMALMAP");
            }

            if (packed != null)
            {
                // URP Lit reads metallic from R and smoothness from A of this map.
                SetTexture(mat, "_MetallicGlossMap", packed);
                mat.SetFloat("_SmoothnessTextureChannel", 0f);
                mat.SetFloat("_Smoothness", 1f);
                mat.SetFloat("_Metallic", 1f);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            }

            if (ao != null)
            {
                SetTexture(mat, "_OcclusionMap", ao);
                mat.SetFloat("_OcclusionStrength", 1f);
                mat.EnableKeyword("_OCCLUSIONMAP");
            }

            EditorUtility.SetDirty(mat);
        }

        /// <summary>
        /// Sets a texture slot, keeping the current one if the file does not load as a texture
        /// (for example, a .meta file without texture import settings).
        /// </summary>
        static void SetTexture(Material mat, string property, string path)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex != null) mat.SetTexture(property, tex);
            else Debug.LogWarning($"{path} did not load as a texture; {mat.name}.{property} left unchanged. Check its .meta import settings.");
        }

        /// <summary>Writes Name_MetallicSmoothness.png: R = metallic (0 without a map), A = 1 − roughness.</summary>
        static string PackMetallicSmoothness(string name, string metallicPath, string roughnessPath)
        {
            var rough = Load(roughnessPath);
            var metal = metallicPath != null ? Load(metallicPath) : null;
            if (metal != null && (metal.width != rough.width || metal.height != rough.height))
            {
                Debug.LogWarning($"{name}: metallic and roughness sizes differ; metallic ignored.");
                metal = null;
            }

            var r = rough.GetPixels32();
            var m = metal?.GetPixels32();
            var outPixels = new Color32[r.Length];
            for (var i = 0; i < r.Length; i++)
                outPixels[i] = new Color32(m != null ? m[i].r : (byte)0, 0, 0, (byte)(255 - r[i].r));

            var packed = new Texture2D(rough.width, rough.height, TextureFormat.RGBA32, false);
            packed.SetPixels32(outPixels);
            var path = $"{MaterialsRoot}/{name}/{name}_MetallicSmoothness.png";
            File.WriteAllBytes(path, packed.EncodeToPNG());
            Object.DestroyImmediate(packed);
            Object.DestroyImmediate(rough);
            if (metal != null) Object.DestroyImmediate(metal);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return path;
        }

        /// <summary>Reads the source file directly, so the texture's import settings do not matter.</summary>
        static Texture2D Load(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear: true);
            tex.LoadImage(File.ReadAllBytes(path));
            return tex;
        }
    }

    /// <summary>Import settings for the texture sets: normal maps as normal maps, data maps in linear space.</summary>
    public sealed class ArtTextureImporter : AssetPostprocessor
    {
        /// <summary>Guardian models: metres as exported, skinned to their bones (GuardianMotion turns them), no clips.</summary>
        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(SandboxArtMenu.GuardianModelsRoot) && assetPath != SandboxArtMenu.PlayerModelPath) return;
            ApplyGuardianModelSettings((ModelImporter)assetImporter);
        }

        public static void ApplyGuardianModelSettings(ModelImporter importer)
        {
            importer.useFileScale = true;
            importer.globalScale = 1f;
            // Keep the Blender material slot names (Robe, Mantle, Bone, Metal, Hair): CombatSandbox colours by them.
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            // A generic rig keeps the skeleton (tools/blender/rig_guardians.py); the Animator has no controller, so
            // nothing overrides GuardianMotion. Axis conversion is baked so the bones are not under a -90° root turn.
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.bakeAxisConversion = true;
            importer.optimizeGameObjects = false;
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(SandboxArtMenu.MaterialsRoot)) return;
            var importer = (TextureImporter)assetImporter;
            // Always a flat 2D texture: a meta written without import settings once imported these as cubemaps.
            importer.textureShape = TextureImporterShape.Texture2D;
            var file = Path.GetFileNameWithoutExtension(assetPath);
            if (file.EndsWith("_Normal")) importer.textureType = TextureImporterType.NormalMap;
            else if (file.EndsWith("_Roughness") || file.EndsWith("_Metallic") || file.EndsWith("_AO") || file.EndsWith("_MetallicSmoothness"))
                importer.sRGBTexture = false;
        }
    }
}
