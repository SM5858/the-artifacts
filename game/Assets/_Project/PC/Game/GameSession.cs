using System.Linq;
using System.Threading.Tasks;
using TheArtifacts.Core.Account;
using TheArtifacts.Core.Guardian;
using TheArtifacts.Shared;
using UnityEngine;

namespace TheArtifacts.PC.Game
{
    /// <summary>
    /// What lives for the whole PC session, across scene reloads: the signed-in account and its profile, the Guardian
    /// client (whose session flag must survive a reload).
    /// </summary>
    public static class GameSession
    {
        public static IAccountService Account { get; private set; }
        public static PlayerProfile Profile { get; private set; }
        /// <summary>The last save error, shown until the next save succeeds; null when saved.</summary>
        public static string SaveError { get; private set; }

        static GuardianClient _guardian;
        public static GuardianClient Guardian => _guardian ??= new GuardianClient(GuardianBackends.Live);

        public static bool SignedIn => Account != null && Profile != null;

        public static async Task SignInAsync(IAccountService account, string username, string password, bool create)
        {
            if (create) await account.SignUpAsync(username, password);
            else await account.SignInAsync(username, password);
            Profile = await account.LoadProfileAsync();
            Account = account;
            // Logged so a check-in that never reached the PC (GDD 7, handoff failure) can be found.
            Debug.Log($"profile_loaded user={account.Username} online={account.IsOnline} " +
                      $"unlocked=[{string.Join(",", Profile.UnlockedGuardians)}] visits={Profile.Visits.Count} " +
                      $"artifacts=[{string.Join(",", Profile.Artifacts.Select(a => $"{a.Id}x{a.Count}"))}]");
        }

        public static void SignOut()
        {
            Account?.SignOut();
            Account = null;
            Profile = null;
        }

        /// <summary>Saves the profile; a failure is kept in <see cref="SaveError"/> and retried on the next save.</summary>
        public static async Task SaveAsync()
        {
            if (!SignedIn) return;
            try
            {
                await Account.SaveProfileAsync(Profile);
                SaveError = null;
            }
            catch (System.Exception e)
            {
                SaveError = e.Message;
                Debug.LogWarning($"profile_save_failed user={Account.Username}: {e.Message}");
            }
        }
    }
}
