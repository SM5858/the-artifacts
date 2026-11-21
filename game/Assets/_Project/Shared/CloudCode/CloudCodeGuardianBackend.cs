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
    /// Calls the GuardianModule Cloud Code function before each attempt (server/README.md) as the signed-in player
    /// (<see cref="Shared.Accounts"/>). A test fight with nobody signed in signs in anonymously first, so that call is
    /// slower. Errors reach <see cref="GuardianClient"/>, which falls back to the local default Guardian and flags
    /// the session.
    /// </summary>
    public sealed class CloudCodeGuardianBackend : IGuardianBackend
    {
        public const string Module = "GuardianModule";
        public const string Function = "ConfigureGuardian";

        static Task _initialize;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Shared.GuardianBackends.Live = new CloudCodeGuardianBackend();
            _initialize = InitializeAsync();  // early, so the first fight does not wait for it
        }

        static async Task InitializeAsync()
        {
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized) await UnityServices.InitializeAsync();
            }
            catch (System.Exception e)
            {
                // Not linked to a Unity Cloud project, or offline: fights use the local default Guardian.
                Debug.LogWarning($"Unity Gaming Services unavailable, using the default Guardian: {e.Message}");
                throw;
            }
        }

        public async Task<GuardianDecision> ConfigureAsync(GuardianCall call, CancellationToken cancellationToken)
        {
            await (_initialize ??= InitializeAsync());
            if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
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
