using TheArtifacts.Core.Guardian;

namespace TheArtifacts.Shared
{
    /// <summary>
    /// The live Guardian server, when this build has one. The Cloud Code assembly (Shared/CloudCode) registers it
    /// only when the Cloud Code package is installed, so the game compiles and runs without it and falls back to the
    /// local default Guardian.
    /// </summary>
    public static class GuardianBackends
    {
        public static IGuardianBackend Live { get; set; }
    }
}
