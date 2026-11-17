using System;
using System.Collections.Generic;
using System.Linq;

namespace TheArtifacts.Core.Guardian
{
    /// <summary>Why the default Guardian was used instead of the model's proposal.</summary>
    public enum FallbackReason
    {
        None,
        ServiceUnavailable,
        Timeout,
        Malformed,
        TierOutOfRange,
        SkillOutOfRange,
        DuplicateSkill,
        SkillCountOutOfRange,
        StrategyOutOfRange,
    }

    /// <summary>The setup the fight will actually use, and whether it came from a fallback.</summary>
    public sealed class SetupDecision
    {
        public GuardianSetup Setup { get; }
        public FallbackReason Reason { get; }
        public bool UsedFallback => Reason != FallbackReason.None;

        public SetupDecision(GuardianSetup setup, FallbackReason reason)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            Reason = reason;
        }
    }

    /// <summary>
    /// Validates the model's proposed setup before each attempt (Configuration Document 2.5).
    /// Any invalid, late, or missing proposal is replaced by the default Guardian; there are no retries.
    /// </summary>
    public static class GuardianSetupValidator
    {
        /// <summary>Deadline for the model's answer at fight start (Configuration Document 2.7).</summary>
        public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

        /// <param name="proposal">The model's setup, or null if the call failed or the service was unavailable.</param>
        /// <param name="latency">Time the model call took.</param>
        /// <param name="space">Options the engine allows for this challenger.</param>
        /// <param name="defaultSetup">The scripted baseline's setup.</param>
        public static SetupDecision Decide(
            GuardianSetup proposal,
            TimeSpan latency,
            AllowedSetupSpace space,
            GuardianSetup defaultSetup)
        {
            if (space == null) throw new ArgumentNullException(nameof(space));
            if (defaultSetup == null) throw new ArgumentNullException(nameof(defaultSetup));

            var reason = Check(proposal, latency, space);
            return reason == FallbackReason.None
                ? new SetupDecision(proposal, FallbackReason.None)
                : new SetupDecision(defaultSetup, reason);
        }

        public static FallbackReason Check(GuardianSetup proposal, TimeSpan latency, AllowedSetupSpace space)
        {
            if (proposal == null) return FallbackReason.ServiceUnavailable;
            if (latency > Deadline) return FallbackReason.Timeout;

            if (string.IsNullOrEmpty(proposal.StatTier)
                || string.IsNullOrEmpty(proposal.StrategyId)
                || proposal.SkillIds == null
                || proposal.SkillIds.Any(string.IsNullOrEmpty))
                return FallbackReason.Malformed;

            if (!space.StatTiers.Contains(proposal.StatTier)) return FallbackReason.TierOutOfRange;
            if (proposal.SkillIds.Any(id => !space.SkillPool.Contains(id))) return FallbackReason.SkillOutOfRange;
            if (new HashSet<string>(proposal.SkillIds).Count != proposal.SkillIds.Count) return FallbackReason.DuplicateSkill;
            if (proposal.SkillIds.Count < space.MinSkills || proposal.SkillIds.Count > space.MaxSkills)
                return FallbackReason.SkillCountOutOfRange;
            if (!space.Strategies.Contains(proposal.StrategyId)) return FallbackReason.StrategyOutOfRange;

            return FallbackReason.None;
        }
    }
}
