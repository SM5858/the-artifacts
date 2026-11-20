using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TheArtifacts.Core.Combat;
using TheArtifacts.Core.Guardian;

namespace GuardianModule
{
    /// <summary>What the model returned (fields may be null if malformed) and what it cost.</summary>
    public sealed class ModelAnswer
    {
        public GuardianSetup Proposal { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public string StopReason { get; set; }
    }

    /// <summary>The model behind the Guardian. Throws on service errors.</summary>
    public interface IGuardianModel
    {
        string ModelId { get; }
        Task<ModelAnswer> ProposeAsync(GuardianRequest request, CancellationToken cancellationToken);
    }

    /// <summary>The setup a fight uses plus its trace entry (Configuration Document 2.9).</summary>
    public sealed class GuardianDecision
    {
        public string StatTier { get; set; }
        public string[] SkillIds { get; set; }
        public string StrategyId { get; set; }
        public bool Adaptive { get; set; }
        public bool UsedFallback { get; set; }
        public string FallbackReason { get; set; }
        public string ProposedSetup { get; set; }
        public string SnapshotHash { get; set; }
        public int GearScore { get; set; }
        public int AttemptsOnThisGuardian { get; set; }
        public string GuardianId { get; set; }
        /// <summary>The single easing step is unlocked (GDD 6.4): enough failed attempts on this Guardian.</summary>
        public bool Eased { get; set; }
        /// <summary>The Guardian's line at fight start (adaptive mode only), or null.</summary>
        public string Announcement { get; set; }
        public string Model { get; set; }
        public long LatencyMs { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public string StopReason { get; set; }
    }

    /// <summary>
    /// One call at fight start: build the request from the allowed snapshot, call the model under the
    /// 3 s deadline with no retries, validate, and fall back to the default Guardian on any failure.
    /// </summary>
    public sealed class GuardianService
    {
        readonly IGuardianModel _model;

        public GuardianService(IGuardianModel model) => _model = model;

        /// <param name="attemptsOnThisGuardian">Failed attempts on this Guardian since the last win.</param>
        /// <param name="guardianId">The Guardian being fought; its loot pool sets the easing threshold.</param>
        public async Task<GuardianDecision> DecideAsync(string[] equippedArtifactIds, int attemptsOnThisGuardian, bool adaptive,
            string guardianId = null)
        {
            guardianId ??= PrototypeContent.Guardian.Id;
            var build = PrototypeContent.Build(equippedArtifactIds ?? Array.Empty<string>());
            var eased = PrototypeContent.IsEased(guardianId, attemptsOnThisGuardian);
            var space = PrototypeContent.AllowedSpaceFor(build.GearScore, eased);
            var defaultSetup = PrototypeContent.DefaultSetup;

            var decision = new GuardianDecision
            {
                Adaptive = adaptive,
                GearScore = build.GearScore,
                AttemptsOnThisGuardian = attemptsOnThisGuardian,
                GuardianId = guardianId,
            };

            if (!adaptive || _model == null)
            {
                // Scripted baseline (or the human override in Configuration Document 2.10): no model call.
                Fill(decision, new SetupDecision(defaultSetup, FallbackReason.None));
                decision.UsedFallback = false;
                return decision;
            }

            // The scripted baseline never eases or speaks; only the adaptive Guardian does.
            decision.Eased = eased;
            decision.Announcement = PrototypeContent.Announcement(build.GearScore, eased);
            var request = GuardianRequest.Build(GuardianRequest.Snapshot(build, eased), space);
            decision.SnapshotHash = request.SnapshotHash;
            decision.Model = _model.ModelId;

            ModelAnswer answer = null;
            var timer = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource();
            try
            {
                var call = _model.ProposeAsync(request, cts.Token);
                var deadline = Task.Delay(GuardianSetupValidator.Deadline + TimeSpan.FromMilliseconds(1));
                if (await Task.WhenAny(call, deadline).ConfigureAwait(false) == call)
                    answer = await call.ConfigureAwait(false);
                else
                    cts.Cancel();
            }
            catch (Exception)
            {
                answer = null; // service unavailable or request error
            }
            timer.Stop();

            decision.LatencyMs = timer.ElapsedMilliseconds;
            decision.InputTokens = answer?.InputTokens ?? 0;
            decision.OutputTokens = answer?.OutputTokens ?? 0;
            decision.StopReason = answer?.StopReason;
            decision.ProposedSetup = answer?.Proposal?.ToString();

            var result = GuardianSetupValidator.Decide(answer?.Proposal, timer.Elapsed, space, defaultSetup);
            Fill(decision, result);
            if (result.UsedFallback) decision.Announcement = null;  // the default Guardian did not adapt
            return decision;
        }

        static void Fill(GuardianDecision d, SetupDecision result)
        {
            d.StatTier = result.Setup.StatTier;
            d.SkillIds = new string[result.Setup.SkillIds.Count];
            for (var i = 0; i < d.SkillIds.Length; i++) d.SkillIds[i] = result.Setup.SkillIds[i];
            d.StrategyId = result.Setup.StrategyId;
            d.UsedFallback = result.UsedFallback;
            d.FallbackReason = result.Reason.ToString();
        }
    }
}
