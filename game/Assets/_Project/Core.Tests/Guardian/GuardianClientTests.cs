using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TheArtifacts.Core.Combat;
using TheArtifacts.Core.Guardian;

namespace TheArtifacts.Core.Tests.Guardian
{
    public class GuardianClientTests
    {
        const string Lion = "columbia_lion";

        sealed class FakeBackend : IGuardianBackend
        {
            public Func<GuardianCall, CancellationToken, Task<GuardianDecision>> Answer;
            public int Calls;

            public Task<GuardianDecision> ConfigureAsync(GuardianCall call, CancellationToken cancellationToken)
            {
                Calls++;
                return Answer(call, cancellationToken);
            }
        }

        static GuardianCall Call(bool adaptive = true) =>
            new GuardianCall(Lion, PrototypeContent.StarterBuild.Artifacts.Select(a => a.Id), 0, adaptive);

        /// <summary>A setup inside the starter build's allowed range that is not the default.</summary>
        static GuardianDecision InRange(string announcement = null)
        {
            var space = PrototypeContent.AllowedSpaceFor(PrototypeContent.StarterBuild.GearScore);
            return new GuardianDecision
            {
                StatTier = space.StatTiers.OrderBy(t => t).First(),
                SkillIds = space.SkillPool.OrderBy(s => s).Take(space.MinSkills).ToArray(),
                StrategyId = space.Strategies.OrderBy(s => s).First(),
                Adaptive = true,
                FallbackReason = nameof(FallbackReason.None),
                Announcement = announcement,
            };
        }

        static FakeBackend Returning(GuardianDecision d) =>
            new FakeBackend { Answer = (c, t) => Task.FromResult(d) };

        [Test]
        public async Task ScriptedMode_UsesTheDefault_WithoutCallingTheServer()
        {
            var backend = Returning(InRange());

            var fight = await new GuardianClient(backend).ConfigureAsync(Call(adaptive: false));

            Assert.That(backend.Calls, Is.EqualTo(0));
            Assert.That(fight.Source, Is.EqualTo(SetupSource.Scripted));
            Assert.That(fight.Setup.ToString(), Is.EqualTo(PrototypeContent.DefaultSetup.ToString()));
        }

        [Test]
        public async Task NoServer_UsesTheLocalDefault()
        {
            var fight = await new GuardianClient(null).ConfigureAsync(Call());

            Assert.That(fight.Source, Is.EqualTo(SetupSource.LocalFallback));
            Assert.That(fight.FallbackReason, Is.EqualTo(nameof(FallbackReason.ServiceUnavailable)));
            Assert.That(fight.Setup.ToString(), Is.EqualTo(PrototypeContent.DefaultSetup.ToString()));
        }

        [Test]
        public async Task ServerSetup_InRange_IsUsed_WithItsAnnouncement()
        {
            var answer = InRange("At last, a worthy challenger.");

            var fight = await new GuardianClient(Returning(answer)).ConfigureAsync(Call());

            Assert.That(fight.Source, Is.EqualTo(SetupSource.Server));
            Assert.That(fight.FallbackReason, Is.EqualTo(nameof(FallbackReason.None)));
            Assert.That(fight.Setup.ToString(), Is.EqualTo(answer.ToSetup().ToString()));
            Assert.That(fight.Announcement, Is.EqualTo("At last, a worthy challenger."));
        }

        [Test]
        public async Task ServerFallback_UsesTheDefault_AndSaysNothing()
        {
            var answer = InRange("ignored");
            answer.UsedFallback = true;
            answer.FallbackReason = nameof(FallbackReason.Timeout);

            var fight = await new GuardianClient(Returning(answer)).ConfigureAsync(Call());

            Assert.That(fight.Source, Is.EqualTo(SetupSource.Server));
            Assert.That(fight.FallbackReason, Is.EqualTo(nameof(FallbackReason.Timeout)));
            Assert.That(fight.Setup.ToString(), Is.EqualTo(PrototypeContent.DefaultSetup.ToString()));
            Assert.That(fight.Announcement, Is.Null);
        }

        [Test]
        public async Task SlowServer_FallsBackAtTheClientTimeout()
        {
            var backend = new FakeBackend
            {
                Answer = async (c, t) => { await Task.Delay(TimeSpan.FromSeconds(5), t); return InRange(); },
            };
            var client = new GuardianClient(backend, TimeSpan.FromMilliseconds(50));

            var fight = await client.ConfigureAsync(Call());

            Assert.That(fight.Source, Is.EqualTo(SetupSource.LocalFallback));
            Assert.That(fight.FallbackReason, Is.EqualTo(nameof(FallbackReason.Timeout)));
            Assert.That(fight.LatencyMs, Is.LessThan(2000));
            Assert.That(client.SessionFlagged, Is.False, "a late answer is not an outage");
        }

        [Test]
        public async Task ServerError_FlagsTheSession_AndLaterAttemptsSkipTheCall()
        {
            var backend = new FakeBackend { Answer = (c, t) => throw new InvalidOperationException("offline") };
            var client = new GuardianClient(backend);

            var first = await client.ConfigureAsync(Call());
            var second = await client.ConfigureAsync(Call());

            Assert.That(first.FallbackReason, Is.EqualTo(nameof(FallbackReason.ServiceUnavailable)));
            Assert.That(second.Source, Is.EqualTo(SetupSource.LocalFallback));
            Assert.That(client.SessionFlagged, Is.True);
            Assert.That(backend.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task ServerUnavailableReply_AlsoFlagsTheSession()
        {
            var answer = InRange();
            answer.UsedFallback = true;
            answer.FallbackReason = nameof(FallbackReason.ServiceUnavailable);
            var backend = Returning(answer);
            var client = new GuardianClient(backend);

            await client.ConfigureAsync(Call());
            await client.ConfigureAsync(Call());

            Assert.That(client.SessionFlagged, Is.True);
            Assert.That(backend.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task OutOfRangeReply_IsReplacedByTheLocalDefault()
        {
            var answer = InRange("ignored");
            answer.StatTier = "T9";

            var fight = await new GuardianClient(Returning(answer)).ConfigureAsync(Call());

            Assert.That(fight.Source, Is.EqualTo(SetupSource.LocalFallback));
            Assert.That(fight.FallbackReason, Is.EqualTo(nameof(FallbackReason.TierOutOfRange)));
            Assert.That(fight.Announcement, Is.Null);
        }

        [Test]
        public async Task EmptyReply_IsMalformed()
        {
            var fight = await new GuardianClient(Returning(null)).ConfigureAsync(Call());

            Assert.That(fight.FallbackReason, Is.EqualTo(nameof(FallbackReason.Malformed)));
        }

        [Test]
        public void AttemptRecord_CountsLosses_UntilAWin()
        {
            var record = new AttemptRecord();
            record.RecordLoss(Lion);
            record.RecordLoss(Lion);
            record.RecordLoss("book_keeper");

            Assert.That(record.FailedAttempts(Lion), Is.EqualTo(2));
            record.RecordWin(Lion);
            Assert.That(record.FailedAttempts(Lion), Is.EqualTo(0));
            Assert.That(record.FailedAttempts("book_keeper"), Is.EqualTo(1));
        }
    }
}
