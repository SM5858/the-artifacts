using System.Collections.Generic;
using System.Threading.Tasks;
using TheArtifacts.Core.Account;
using Unity.Services.Authentication;
using Unity.Services.CloudSave;
using Unity.Services.Core;
using UnityEngine;

namespace TheArtifacts.Services
{
    /// <summary>
    /// The online account (GDD 3): Unity Authentication username and password, with the profile in Cloud Save player
    /// data, so the mobile check-in and the PC client see the same unlocks and artifacts.
    /// </summary>
    public sealed class UgsAccountService : IAccountService
    {
        /// <summary>Cloud Save key that holds the <see cref="PlayerProfile"/>.</summary>
        public const string ProfileKey = "profile";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register() => Shared.Accounts.Online = new UgsAccountService();

        public bool IsOnline => true;
        public string Username { get; private set; }

        public Task SignInAsync(string username, string password) =>
            Run(async auth => await auth.SignInWithUsernamePasswordAsync(username, password), username);

        public Task SignUpAsync(string username, string password)
        {
            var problem = AccountRules.Validate(username, password);
            if (problem != null) throw new AccountException(problem);
            return Run(async auth => await auth.SignUpWithUsernamePasswordAsync(username, password), username);
        }

        public void SignOut()
        {
            if (UnityServices.State == ServicesInitializationState.Initialized && AuthenticationService.Instance.IsSignedIn)
                AuthenticationService.Instance.SignOut();
            Username = null;
        }

        public async Task<PlayerProfile> LoadProfileAsync()
        {
            RequireSignedIn();
            var items = await CloudSaveService.Instance.Data.Player.LoadAsync(new HashSet<string> { ProfileKey });
            return items.TryGetValue(ProfileKey, out var item) ? item.Value.GetAs<PlayerProfile>() ?? new PlayerProfile() : new PlayerProfile();
        }

        public async Task SaveProfileAsync(PlayerProfile profile)
        {
            RequireSignedIn();
            await CloudSaveService.Instance.Data.Player.SaveAsync(new Dictionary<string, object> { { ProfileKey, profile } });
        }

        async Task Run(System.Func<IAuthenticationService, Task> signIn, string username)
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
                var auth = AuthenticationService.Instance;
                if (auth.IsSignedIn) auth.SignOut();  // e.g. an anonymous session from a test fight
                await signIn(auth);
                Username = username;
            }
            catch (AccountException) { throw; }
            catch (System.Exception e)
            {
                throw new AccountException($"Online sign-in failed: {e.Message}", e);
            }
        }

        void RequireSignedIn()
        {
            if (Username == null || !AuthenticationService.Instance.IsSignedIn) throw new AccountException("Not signed in.");
        }
    }
}
