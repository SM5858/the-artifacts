using System;
using System.Linq;
using UnityEngine;

namespace TheArtifacts.PC.Combat
{
    /// <summary>
    /// Materials the graybox sandbox uses when they exist (GDD 9, visual direction). Built by
    /// The Artifacts → Build Sandbox Materials from the CC0 textures in Art/Materials. Any missing
    /// material falls back to the flat graybox colors, and a Guardian without a model stays a capsule.
    /// </summary>
    [CreateAssetMenu(menuName = "The Artifacts/Sandbox Art")]
    public sealed class SandboxArt : ScriptableObject
    {
        [Serializable]
        public struct GuardianMaterial
        {
            public string GuardianId;
            public Material Material;
            /// <summary>
            /// Blockout model from tools/blender/guardian_blockouts.py: origin at the feet, facing +Z. A child named
            /// "Owl" is the Guardian's weak point.
            /// </summary>
            public GameObject Model;
        }

        public Material Arena;
        public Material Bookshelf;
        public GuardianMaterial[] Guardians = new GuardianMaterial[0];

        /// <summary>The material for this Guardian, or null.</summary>
        public Material ForGuardian(string guardianId) =>
            Guardians?.FirstOrDefault(g => g.GuardianId == guardianId).Material;

        /// <summary>The model for this Guardian, or null.</summary>
        public GameObject ModelFor(string guardianId) =>
            Guardians?.FirstOrDefault(g => g.GuardianId == guardianId).Model;
    }
}
