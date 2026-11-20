using System;
using System.Linq;
using UnityEngine;

namespace TheArtifacts.PC.Combat
{
    /// <summary>
    /// Materials the graybox sandbox uses when they exist (GDD 9, visual direction). Built by
    /// The Artifacts → Build Sandbox Materials from the CC0 textures in Art/Materials. Any missing
    /// material falls back to the flat graybox colors.
    /// </summary>
    [CreateAssetMenu(menuName = "The Artifacts/Sandbox Art")]
    public sealed class SandboxArt : ScriptableObject
    {
        [Serializable]
        public struct GuardianMaterial
        {
            public string GuardianId;
            public Material Material;
        }

        public Material Arena;
        public Material Bookshelf;
        public GuardianMaterial[] Guardians = new GuardianMaterial[0];

        /// <summary>The material for this Guardian, or null.</summary>
        public Material ForGuardian(string guardianId) =>
            Guardians?.FirstOrDefault(g => g.GuardianId == guardianId).Material;
    }
}
