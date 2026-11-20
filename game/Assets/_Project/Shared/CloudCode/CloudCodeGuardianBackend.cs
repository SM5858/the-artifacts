using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TheArtifacts.Core.Guardian;
using Unity.Services.Authentication;
using Unity.Services.CloudCode;
using Unity.Services.Core;
using UnityEngine;

namespace TheArtifacts.Services
{
    /// <summary>
    /// Calls the GuardianModule Cloud Code function before each attempt (server/README.md). Signs in anonymously on
    /// startup so the first fight does not wait for it. Errors reach <see cref="GuardianClient"/>, which falls back to
    /// the local default Guardian and flags the session.
    /// </summary>
    public sealed class CloudCodeGuardianBackend : IGuardianBackend
    {
        public const string Module = "GuardianModule";
        public const string Function = "ConfigureGuardian";

        static Task _signIn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Shared.GuardianBackends.Live = new CloudCodeGuardianBackend();
            _signIn = SignInAsync();
        }

        static async Task SignInAsync()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
                if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
            catch (System.Exception e)
            {
                // Not linked to a Unity Cloud project, offline, or signed out: fights use the local default Guardian.
                Debug.LogWarning($"Guardian server unavailable, using the default Guardian: {e.Message}");
                throw;
            }
        }

        public async Task<GuardianDecision> ConfigureAsync(GuardianCall call, CancellationToken cancellationToken)
        {
            await (_signIn ??= SignInAsync());
            cancellationToken.ThrowIfCancellationRequested();
            var args = new Dictionary<string, object>
            {
                { "equippedArtifactIds", call.EquippedArtifactIds },
                { "attemptsOnThisGuardian", call.AttemptsOnThisGuardian },
                { "adaptive", call.Adaptive },
                { "guardianId", call.GuardianId },
            };
            return await CloudCodeService.Instance.CallModuleEndpointAsync<GuardianDecision>(Module, Function, args);
        }
    }
}
