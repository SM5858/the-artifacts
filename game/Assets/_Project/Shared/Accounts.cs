using TheArtifacts.Core.Account;

namespace TheArtifacts.Shared
{
    /// <summary>
    /// The account services this build has. <see cref="Online"/> is registered by the Cloud Save assembly
    /// (Shared/CloudSave) only when the Authentication and Cloud Save packages are installed; <see cref="Offline"/>
    /// always works and keeps profiles on this machine.
    /// </summary>
    public static class Accounts
    {
        public static IAccountService Online { get; set; }
        public static readonly IAccountService Offline = new LocalAccountService();
    }
}
