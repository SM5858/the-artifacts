using System.Threading.Tasks;

namespace TheArtifacts.Core.Account
{
    /// <summary>
    /// One account for mobile and PC (GDD 3): a username and password, and the profile stored with it. The online
    /// version uses Unity Authentication and Cloud Save; the offline one keeps profiles on this machine.
    /// </summary>
    public interface IAccountService
    {
        /// <summary>True when profiles are stored online and shared between devices.</summary>
        bool IsOnline { get; }
        /// <summary>The signed-in username, or null.</summary>
        string Username { get; }

        /// <summary>Throws <see cref="AccountException"/> with a message for the player when sign-in fails.</summary>
        Task SignInAsync(string username, string password);
        /// <summary>Creates the account and signs in. Throws <see cref="AccountException"/> on failure.</summary>
        Task SignUpAsync(string username, string password);
        void SignOut();

        /// <summary>The signed-in player's profile; a new, empty one for a new account.</summary>
        Task<PlayerProfile> LoadProfileAsync();
        Task SaveProfileAsync(PlayerProfile profile);
    }

    public sealed class AccountException : System.Exception
    {
        public AccountException(string message, System.Exception inner = null) : base(message, inner) { }
    }
}
