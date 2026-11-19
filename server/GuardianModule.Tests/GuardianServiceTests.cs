using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GuardianModule;
using NUnit.Framework;
using TheArtifacts.Core.Combat;
using TheArtifacts.Core.Guardian;

namespace GuardianModule.Tests
{
    /// <summary>Stands in for Claude; never calls the network.</summary>
    sealed class FakeModel : IGuardianModel
    {
        readonly Func<GuardianRequest, CancellationToken, Task<ModelAnswer>> _answer;
        public int Calls;
        public GuardianRequest LastRequest;

        public FakeModel(Func<GuardianRequest, CancellationToken, Task<ModelAnswer>> answer) => _answer = answer;

        public static FakeModel Returning(string tier, string strategy, params string[] skills) =>
            new FakeModel((_, __) => Task.FromResult(new ModelAnswer
            {
                Proposal = new GuardianSetup(tier, skills, strategy), InputTokens = 900, OutputTokens = 40, StopReason = "tool_use",
            }));

        public string ModelId => "fake";

        public Task<ModelAnswer> ProposeAsync(GuardianRequest request, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            return _answer(request, ct);
        }
    }

    public class GuardianServiceTests
    {
        static readonly string[] Starter = new string[0];
        static readonly string[] Geared = { "wreath_of_alma_mater", "columbia_lions_fang", "columbia_blue_robe" };

        [Test]
        public async Task ScriptedMode_UsesDefaultGuardian_WithoutCallingTheModel()
        {
            var model = FakeModel.Returning("T1", "steady", "slam", "cleave", "charge");

            var d = await new GuardianService(model).DecideAsync(Starter, 0, adaptive: false);

            Assert.That(model.Calls, Is.Zero);
            Assert.That((d.StatTier, d.StrategyId), Is.EqualTo(("T3", "steady")));
            Assert.That(d.SkillIds, Is.EqualTo(new[] { "cleave", "slam", "charge" }));
            Assert.That(d.UsedFallback, Is.False);
        }

        [Test]
        public async Task ValidProposal_IsUsed_AndTraced()
        {
            var model = FakeModel.Returning("T1", "steady", "slam", "cleave", "charge");

            var d = await new GuardianService(model).DecideAsync(Starter, 2, adaptive: true);

            Assert.That(model.Calls, Is.EqualTo(1));
            Assert.That(d.UsedFallback, Is.False);
            Assert.That(d.StatTier, Is.EqualTo("T1"));
            Assert.That(d.FallbackReason, Is.EqualTo("None"));
            Assert.That(d.InputTokens, Is.EqualTo(900));
            Assert.That(d.SnapshotHash, Has.Length.EqualTo(16));
            Assert.That(d.AttemptsOnThisGuardian, Is.EqualTo(2));
        }

        [Test]
        public async Task ForcedTimeout_UsesDefaultGuardian()
        {
            var model = new FakeModel(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return null;
            });

            var d = await new GuardianService(model).DecideAsync(Starter, 0, adaptive: true);

            Assert.That(d.UsedFallback, Is.True);
            Assert.That(d.FallbackReason, Is.EqualTo("Timeout"));
            Assert.That(d.StatTier, Is.EqualTo("T3"));
            Assert.That(d.LatencyMs, Is.InRange(3000, 4500));
        }

        [Test]
        public async Task ForcedOutOfRange_UsesDefaultGuardian()
        {
            // A starter (gear score 0) may face T1–T3 only.
            var model = FakeModel.Returning("T5", "steady", "slam", "cleave", "charge");

            var d = await new GuardianService(model).DecideAsync(Starter, 0, adaptive: true);

            Assert.That(d.UsedFallback, Is.True);
            Assert.That(d.FallbackReason, Is.EqualTo("TierOutOfRange"));
            Assert.That(d.ProposedSetup, Does.Contain("T5"));
            Assert.That(d.StatTier, Is.EqualTo("T3"));
        }

        [Test]
        public async Task MalformedAnswer_UsesDefaultGuardian()
        {
            var model = FakeModel.Returning(null, "steady", "slam");

            var d = await new GuardianService(model).DecideAsync(Starter, 0, adaptive: true);

            Assert.That(d.FallbackReason, Is.EqualTo("Malformed"));
        }

        [Test]
        public async Task ServiceError_UsesDefaultGuardian()
        {
            var model = new FakeModel((_, __) => Task.FromException<ModelAnswer>(new InvalidOperationException("503")));

            var d = await new GuardianService(model).DecideAsync(Starter, 0, adaptive: true);

            Assert.That(d.FallbackReason, Is.EqualTo("ServiceUnavailable"));
            Assert.That(d.StatTier, Is.EqualTo("T3"));
        }

        [Test]
        public void UnknownArtifactFromClient_IsRejected()
        {
            var model = FakeModel.Returning("T1", "steady", "slam", "cleave", "charge");

            Assert.ThrowsAsync<ArgumentException>(() => new GuardianService(model).DecideAsync(new[] { "god_sword" }, 0, true));
        }

        // ---------- What the model sees ----------

        [Test]
        public async Task Request_ContainsOnlyAllowedChallengerFields()
        {
            var model = FakeModel.Returning("T3", "steady", "slam", "cleave", "charge");
            await new GuardianService(model).DecideAsync(Geared, 1, adaptive: true);

            using var doc = JsonDocument.Parse(model.LastRequest.UserMessage);
            var root = doc.RootElement.EnumerateObject().Select(p => p.Name);
            var challenger = doc.RootElement.GetProperty("challenger").EnumerateObject().Select(p => p.Name);

            Assert.That(root, Is.EquivalentTo(new[] { "challenger", "allowed" }));
            Assert.That(challenger, Is.EquivalentTo(new[] { "stats", "abilities", "equipped_artifacts", "attempts_on_this_guardian" }));

            var text = model.LastRequest.UserMessage.ToLowerInvariant();
            foreach (var blocked in new[] { "gps", "latitude", "longitude", "location", "inventory", "reward", "email", "player_id" })
                Assert.That(text, Does.Not.Contain(blocked), blocked);
        }

        [Test]
        public async Task Request_ToolEnumsMatchTheChallengersAllowedRange()
        {
            var model = FakeModel.Returning("T3", "steady", "slam", "cleave", "charge");
            await new GuardianService(model).DecideAsync(Geared, 0, adaptive: true);

            var props = model.LastRequest.ToolInputSchema["properties"]!.AsObject();
            var tiers = props["stat_tier"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>());

            Assert.That(tiers, Is.EqualTo(new[] { "T3", "T4", "T5" }));
            Assert.That(model.LastRequest.ToolInputSchema["additionalProperties"]!.GetValue<bool>(), Is.False);
            Assert.That(model.LastRequest.ToolInputSchema.ToJsonString(), Does.Not.Contain("$ALLOWED"));
        }

        [Test]
        public void Request_IsDeterministic()
        {
            var build = PrototypeContent.GearedBuild;
            var a = GuardianRequest.Build(GuardianRequest.Snapshot(build, 3), PrototypeContent.AllowedSpaceFor(build.GearScore));
            var b = GuardianRequest.Build(GuardianRequest.Snapshot(build, 3), PrototypeContent.AllowedSpaceFor(build.GearScore));

            Assert.That(a.UserMessage, Is.EqualTo(b.UserMessage));
            Assert.That(a.SnapshotHash, Is.EqualTo(b.SnapshotHash));
            Assert.That(a.SystemPrompt, Does.Contain("configure_guardian"));
        }

        // ---------- Parsing the tool call ----------

        [Test]
        public void Parse_ReadsAWellFormedToolCall()
        {
            var input = Input(@"{""stat_tier"":""T2"",""skill_ids"":[""slam"",""cleave""],""strategy_id"":""steady""}");

            var setup = ClaudeGuardianModel.Parse(input);

            Assert.That(setup.StatTier, Is.EqualTo("T2"));
            Assert.That(setup.SkillIds, Is.EqualTo(new[] { "slam", "cleave" }));
            Assert.That(setup.StrategyId, Is.EqualTo("steady"));
        }

        [Test]
        public void Parse_WrongTypesBecomeMalformed()
        {
            var input = Input(@"{""stat_tier"":3,""skill_ids"":""slam"",""strategy_id"":null}");

            var setup = ClaudeGuardianModel.Parse(input);
            var reason = GuardianSetupValidator.Check(setup, TimeSpan.Zero, PrototypeContent.AllowedSpaceFor(0));

            Assert.That(reason, Is.EqualTo(FallbackReason.Malformed));
        }

        static IReadOnlyDictionary<string, JsonElement> Input(string json) =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
    }
}
