using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TheArtifacts.Core.Guardian;
using Unity.Services.CloudCode.Apis;
using Unity.Services.CloudCode.Apis.Extensions;
using Unity.Services.CloudCode.Core;

namespace GuardianModule
{
    /// <summary>Cloud Code entry points. Module name: GuardianModule.</summary>
    public sealed class GuardianFunctions
    {
        /// <summary>Name of the Anthropic API key in the UGS Secret Manager.</summary>
        public const string ApiKeySecret = "ANTHROPIC_API_KEY";

        // Module state may not persist, but reusing one HTTP client per key while it does is cheaper.
        static readonly ConcurrentDictionary<string, ClaudeGuardianModel> Models = new ConcurrentDictionary<string, ClaudeGuardianModel>();

        readonly ILogger<GuardianFunctions> _logger;

        public GuardianFunctions(ILogger<GuardianFunctions> logger) => _logger = logger;

        /// <summary>
        /// Called by the game before each attempt. The client sends only artifact ids, the Guardian, and the
        /// failed attempts on it since the last win;
        /// stats are recomputed here from content so the client cannot send its own numbers.
        /// </summary>
        [CloudCodeFunction("ConfigureGuardian")]
        public async Task<GuardianDecision> ConfigureGuardian(
            IExecutionContext context,
            ISecretClient secrets,
            string[] equippedArtifactIds,
            int attemptsOnThisGuardian,
            bool adaptive,
            string guardianId = null)
        {
            IGuardianModel model = null;
            if (adaptive)
            {
                try
                {
                    var key = (await secrets.GetSecret(context, ApiKeySecret)).Value;
                    model = Models.GetOrAdd(key, k => new ClaudeGuardianModel(k));
                }
                catch (System.Exception e)
                {
                    _logger.LogWarning(e, "Guardian API key unavailable; using the default Guardian.");
                }
            }

            var decision = model == null && adaptive
                ? Unavailable(await new GuardianService(null).DecideAsync(equippedArtifactIds, attemptsOnThisGuardian, adaptive: false, guardianId))
                : await new GuardianService(model).DecideAsync(equippedArtifactIds, attemptsOnThisGuardian, adaptive, guardianId);

            _logger.LogInformation(
                "guardian_trace player={Player} guardian={Guardian} adaptive={Adaptive} gear={Gear} attempts={Attempts} eased={Eased} snapshot={Hash} " +
                "proposed={Proposed} used={Tier}/{Strategy}/{Skills} fallback={Fallback} reason={Reason} " +
                "latency_ms={Latency} in={In} out={Out} stop={Stop}",
                context.PlayerId, decision.GuardianId, decision.Adaptive, decision.GearScore, decision.AttemptsOnThisGuardian, decision.Eased, decision.SnapshotHash,
                decision.ProposedSetup, decision.StatTier, decision.StrategyId, string.Join(",", decision.SkillIds),
                decision.UsedFallback, decision.FallbackReason, decision.LatencyMs, decision.InputTokens, decision.OutputTokens,
                decision.StopReason);

            return decision;
        }

        static GuardianDecision Unavailable(GuardianDecision d)
        {
            d.Adaptive = true;
            d.UsedFallback = true;
            d.FallbackReason = nameof(TheArtifacts.Core.Guardian.FallbackReason.ServiceUnavailable);
            return d;
        }
    }

    public sealed class ModuleSetup : ICloudCodeSetup
    {
        public void Setup(ICloudCodeConfig config)
        {
            config.AddSecretClient();
        }
    }
}
