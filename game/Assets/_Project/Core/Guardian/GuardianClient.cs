using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TheArtifacts.Core.Combat;

namespace TheArtifacts.Core.Guardian
{
    /// <summary>What the client sends before an attempt: artifact ids, the Guardian, and failed attempts on it.</summary>
    public sealed class GuardianCall
    {
        public string GuardianId { get; }
        public string[] EquippedArtifactIds { get; }
        public int AttemptsOnThisGuardian { get; }
        public bool Adaptive { get; }

        public GuardianCall(string guardianId, IEnumerable<string> equippedArtifactIds, int attemptsOnThisGuardian, bool adaptive)
        {
            GuardianId = guardianId ?? throw new ArgumentNullException(nameof(guardianId));
            EquippedArtifactIds = (equippedArtifactIds ?? Enumerable.Empty<string>()).ToArray();
            AttemptsOnThisGuardian = attemptsOnThisGuardian;
            Adaptive = adaptive;
        }
    }

    /// <summary>The server behind the Guardian (the Cloud Code module in the game). Throws on service errors.</summary>
    public interface IGuardianBackend
    {
        Task<GuardianDecision> ConfigureAsync(GuardianCall call, CancellationToken cancellationToken);
    }

    /// <summary>Where the fight's setup came from.</summary>
    public enum SetupSource
    {
        /// <summary>The scripted default, chosen without calling the server.</summary>
        Scripted,
        /// <summary>The server's answer (the model's setup, or the server's own fallback).</summary>
        Server,
        /// <summary>The client's local default, because the server failed, was late, or answered out of range.</summary>
        LocalFallback,
    }

    /// <summary>The setup one attempt fights, and what the HUD and trace need about it.</summary>
    public sealed class FightSetup
    {
        public GuardianSetup Setup { get; }
        public SetupSource Source { get; }
        /// <summary>The server's or the client's fallback reason; None when the model's setup is used.</summary>
        public string FallbackReason { get; }
        /// <summary>The Guardian's line at fight start, or null.</summary>
        public string Announcement { get; }
        public bool Eased { get; }
        public long LatencyMs { get; }

        public FightSetup(GuardianSetup setup, SetupSource source, string fallbackReason, string announcement, bool eased, long latencyMs)
        {
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            Source = source;
            FallbackReason = fallbackReason ?? nameof(Guardian.FallbackReason.None);
            Announcement = announcement;
            Eased = eased;
            LatencyMs = latencyMs;
        }
    }

    /// <summary>
    /// Client side of the Guardian call (Configuration Document 2.5, 2.8): one call before each attempt, a 3.5 s client
    /// timeout (the server's 3 s model deadline plus the round trip), no retries, and the local default Guardian on
    /// any failure. After the service is unavailable once, the session is flagged and later attempts use the default
    /// without calling it.
    /// </summary>
    public sealed class GuardianClient
    {
        public static readonly TimeSpan ClientTimeout = TimeSpan.FromSeconds(3.5);

        readonly IGuardianBackend _backend;
        readonly TimeSpan _timeout;

        /// <param name="backend">The server, or null when it is not set up (every attempt uses the local default).</param>
        public GuardianClient(IGuardianBackend backend, TimeSpan? timeout = null)
        {
            _backend = backend;
            _timeout = timeout ?? ClientTimeout;
        }

        /// <summary>The service failed this session; every later attempt uses the default Guardian (Configuration Document 2.8).</summary>
        public bool SessionFlagged { get; private set; }

        public async Task<FightSetup> ConfigureAsync(GuardianCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            if (!call.Adaptive) return Scripted();
            if (_backend == null || SessionFlagged) return Local(FallbackReason.ServiceUnavailable, 0);

            var started = DateTime.UtcNow;
            GuardianDecision decision;
            using (var cts = new CancellationTokenSource())
            {
                try
                {
                    var request = _backend.ConfigureAsync(call, cts.Token);
                    if (await Task.WhenAny(request, Task.Delay(_timeout)).ConfigureAwait(false) != request)
                    {
                        cts.Cancel();
                        Observe(request);
                        return Local(FallbackReason.Timeout, Elapsed(started));
                    }
                    decision = await request.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    SessionFlagged = true;
                    return Local(FallbackReason.ServiceUnavailable, Elapsed(started));
                }
            }

            var latency = Elapsed(started);
            if (decision == null) return Local(FallbackReason.Malformed, latency);
            if (decision.FallbackReason == nameof(FallbackReason.ServiceUnavailable)) SessionFlagged = true;

            // The server's own fallback is the default Guardian; use this client's copy of it.
            if (decision.UsedFallback)
                return new FightSetup(PrototypeContent.DefaultSetup, SetupSource.Server, decision.FallbackReason, null, false, latency);

            // The server validated the model's setup already; check it again against the content this client ships with.
            var eased = PrototypeContent.IsEased(call.GuardianId, call.AttemptsOnThisGuardian);
            var space = PrototypeContent.AllowedSpaceFor(PrototypeContent.Build(call.EquippedArtifactIds).GearScore, eased);
            var setup = decision.ToSetup();
            var reason = GuardianSetupValidator.Check(setup, TimeSpan.Zero, space);
            if (reason != FallbackReason.None) return Local(reason, latency);

            return new FightSetup(setup, SetupSource.Server, nameof(FallbackReason.None), decision.Announcement, decision.Eased, latency);
        }

        static FightSetup Scripted() =>
            new FightSetup(PrototypeContent.DefaultSetup, SetupSource.Scripted, nameof(FallbackReason.None), null, false, 0);

        static FightSetup Local(FallbackReason reason, long latencyMs) =>
            new FightSetup(PrototypeContent.DefaultSetup, SetupSource.LocalFallback, reason.ToString(), null, false, latencyMs);

        static long Elapsed(DateTime started) => (long)(DateTime.UtcNow - started).TotalMilliseconds;

        /// <summary>A request abandoned at the timeout may still fail later; observe it so the error is not unobserved.</summary>
        static void Observe(Task task) => task.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Failed attempts on each Guardian since the last win (GDD 6.4). Kept in memory for now; Cloud Save keeps it once
    /// accounts exist.
    /// </summary>
    public sealed class AttemptRecord
    {
        readonly Dictionary<string, int> _failed = new Dictionary<string, int>();

        public int FailedAttempts(string guardianId) => _failed.TryGetValue(guardianId, out var n) ? n : 0;

        public void RecordLoss(string guardianId) => _failed[guardianId] = FailedAttempts(guardianId) + 1;

        public void RecordWin(string guardianId) => _failed.Remove(guardianId);
    }
}
