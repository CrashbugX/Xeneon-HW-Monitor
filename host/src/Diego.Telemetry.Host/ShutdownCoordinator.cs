namespace Diego.Telemetry.Host;

/// <summary>
/// Bounded PC-shutdown helper. It runs only when Windows delivers
/// SERVICE_CONTROL_PRESHUTDOWN. Stopping or restarting the host service
/// must not call this. A dropped telemetry poll must not call this.
/// </summary>
/// <remarks>
/// Windows waits only until the service enters SERVICE_STOPPED or until
/// dwPreshutdownTimeout elapses. On builds since the Creators Update (15063)
/// that default is 10 seconds unless install sets SERVICE_CONFIG_PRESHUTDOWN_INFO.
/// When the timer ends, shutdown continues even if the Pi is not halted.
/// A SATA power cut never delivers preshutdown at all.
/// </remarks>
public static class ShutdownLimits
{
    /// <summary>Longest grace the install is allowed to ask Windows to wait.</summary>
    public const int MaxGraceSeconds = 90;

    /// <summary>
    /// Default grace. A Pi 4 desktop halt is often tens of seconds.
    /// Forty-five seconds covers a short connect, sync, the poweroff request,
    /// and about fifteen seconds of settle, without holding shutdown for minutes.
    /// </summary>
    public const int DefaultGraceSeconds = 45;

    /// <summary>How long to wait when the Pi is not configured or never answers.</summary>
    public const int DefaultAbsentBoundSeconds = 5;

    /// <summary>Extra wait after the Pi says halt was issued, so ACK is not treated as done.</summary>
    public const int DefaultSettleSeconds = 15;

    public const int DefaultPollSeconds = 1;

    public const int DefaultRequestTimeoutSeconds = 3;

    /// <summary>Status updates must arrive before this hint, or SCM may treat the service as hung.</summary>
    public const int WaitHintMilliseconds = 4000;
}

/// <summary>What the coordinator last observed from the Pi. Network loss is not a success phase.</summary>
public enum PiShutdownPhase
{
    Unreachable,
    Unauthorized,
    Rejected,
    Accepted,
    Syncing,
    HaltIssued,
    HelperMissing,
    Replay
}

/// <summary>One HTTP observation. Detail is safe to log; it must not contain a token.</summary>
public sealed record PiShutdownObservation(PiShutdownPhase Phase, string Detail);

/// <summary>Why Run returned. None of these mean the outlet power was removed safely.</summary>
public enum ShutdownOutcome
{
    NotConfigured,
    AbsentProceeded,
    AuthRejected,
    HelperMissing,
    HaltIssuedAndSettled,
    HaltIssuedSettleCutShort,
    GraceExpired,
    Aborted
}

/// <summary>Result of one preshutdown attempt.</summary>
public sealed record ShutdownResult(ShutdownOutcome Outcome, TimeSpan Elapsed, int Posts, int StatusPolls, string Detail);

/// <summary>Checkpoint reported to the SCM while the service stays in STOP_PENDING.</summary>
public sealed record ShutdownProgress(int Checkpoint, int WaitHintMilliseconds, string Detail);

/// <summary>Clock that tests can advance without sleeping.</summary>
public interface IShutdownClock
{
    TimeSpan Elapsed { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

/// <summary>Stopwatch plus Task.Delay. Used by the real service, not by unit tests.</summary>
public sealed class SystemShutdownClock : IShutdownClock
{
    private readonly System.Diagnostics.Stopwatch _watch = System.Diagnostics.Stopwatch.StartNew();

    public TimeSpan Elapsed => _watch.Elapsed;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }
}

/// <summary>Talks to the Pi poweroff route. Implementations must not run a shell.</summary>
public interface IPiShutdownChannel
{
    Task<PiShutdownObservation> PostPoweroffAsync(string nonce, DateTimeOffset issuedAtUtc, CancellationToken cancellationToken);

    Task<PiShutdownObservation> GetStatusAsync(CancellationToken cancellationToken);
}

/// <summary>Validated numbers for one coordinator run.</summary>
public sealed class ShutdownPolicy
{
    public TimeSpan Grace { get; init; } = TimeSpan.FromSeconds(ShutdownLimits.DefaultGraceSeconds);

    public TimeSpan AbsentBound { get; init; } = TimeSpan.FromSeconds(ShutdownLimits.DefaultAbsentBoundSeconds);

    public TimeSpan SettleAfterHaltIssued { get; init; } = TimeSpan.FromSeconds(ShutdownLimits.DefaultSettleSeconds);

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(ShutdownLimits.DefaultPollSeconds);

    /// <summary>
    /// Milliseconds to store in SERVICE_PRESHUTDOWN_INFO. Five seconds of margin
    /// past Grace so the last status update can still report SERVICE_STOPPED.
    /// </summary>
    public int PreshutdownTimeoutMilliseconds =>
        checked((int)Grace.TotalMilliseconds + 5000);

    /// <summary>Rejects a policy that could hang shutdown past the cap.</summary>
    public static void Validate(ShutdownPolicy policy)
    {
        if (policy.Grace <= TimeSpan.Zero || policy.Grace > TimeSpan.FromSeconds(ShutdownLimits.MaxGraceSeconds))
        {
            throw new ConfigException(
                $"Shutdown grace must be from 1 to {ShutdownLimits.MaxGraceSeconds} seconds. " +
                $"{policy.Grace.TotalSeconds} is outside that bound. Windows will continue shutdown when the preshutdown timeout ends anyway.");
        }

        if (policy.AbsentBound <= TimeSpan.Zero || policy.AbsentBound > policy.Grace)
        {
            throw new ConfigException("absentBound must be positive and no larger than grace.");
        }

        if (policy.SettleAfterHaltIssued < TimeSpan.Zero || policy.SettleAfterHaltIssued > policy.Grace)
        {
            throw new ConfigException("settleAfterHaltIssued must be from zero through grace.");
        }

        if (policy.PollInterval <= TimeSpan.Zero || policy.PollInterval > policy.Grace)
        {
            throw new ConfigException("shutdown poll interval must be positive and no larger than grace.");
        }
    }
}

/// <summary>
/// Asks the Pi to power off, then waits until the Pi reports halt-issued and a settle
/// period has elapsed, or until a shorter bound for an absent Pi, or until grace ends.
/// </summary>
public sealed class ShutdownCoordinator
{
    private readonly ShutdownPolicy _policy;
    private readonly IPiShutdownChannel _channel;
    private readonly IShutdownClock _clock;
    private readonly bool _piConfigured;

    public ShutdownCoordinator(ShutdownPolicy policy, IPiShutdownChannel channel, IShutdownClock clock, bool piConfigured)
    {
        ShutdownPolicy.Validate(policy);
        _policy = policy;
        _channel = channel;
        _clock = clock;
        _piConfigured = piConfigured;
    }

    /// <summary>
    /// Runs one preshutdown sequence.
    /// </summary>
    /// <param name="progress">Receives checkpoint and wait hint. Detail is token-free.</param>
    /// <param name="cancellationToken">Process cancel. This is not a promise that the Windows shutdown UI can abort.</param>
    /// <returns>Why the wait ended. HaltIssuedAndSettled still does not prove the PSU stayed up.</returns>
    public async Task<ShutdownResult> RunAsync(Action<ShutdownProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var posts = 0;
        var polls = 0;
        if (!_piConfigured)
        {
            progress(new ShutdownProgress(1, ShutdownLimits.WaitHintMilliseconds, "No Pi control URL is configured."));
            return new ShutdownResult(ShutdownOutcome.NotConfigured, _clock.Elapsed, 0, 0, "unpaired");
        }

        var nonce = Guid.NewGuid().ToString("N");
        var issuedAt = DateTimeOffset.UtcNow;
        var posted = false;
        var haltIssued = false;
        var haltAt = TimeSpan.Zero;
        var checkpoint = 1;

        while (_clock.Elapsed < _policy.Grace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress(new ShutdownProgress(checkpoint, ShutdownLimits.WaitHintMilliseconds, posted ? "Waiting for halt-issued." : "Contacting the Pi."));
            checkpoint++;

            if (!posted)
            {
                posts++;
                PiShutdownObservation post;
                try
                {
                    post = await _channel.PostPoweroffAsync(nonce, issuedAt, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new ShutdownResult(ShutdownOutcome.Aborted, _clock.Elapsed, posts, polls, "cancelled");
                }

                if (post.Phase is PiShutdownPhase.Unauthorized)
                {
                    return new ShutdownResult(ShutdownOutcome.AuthRejected, _clock.Elapsed, posts, polls, "unauthorized");
                }

                if (post.Phase is PiShutdownPhase.HelperMissing)
                {
                    return new ShutdownResult(ShutdownOutcome.HelperMissing, _clock.Elapsed, posts, polls, "helper-missing");
                }

                if (post.Phase is PiShutdownPhase.Unreachable or PiShutdownPhase.Rejected)
                {
                    if (_clock.Elapsed >= _policy.AbsentBound)
                    {
                        return new ShutdownResult(ShutdownOutcome.AbsentProceeded, _clock.Elapsed, posts, polls, "absent");
                    }
                }
                else
                {
                    // Accepted, syncing, halt-issued, or replay all mean the Pi saw the request.
                    posted = true;
                    if (post.Phase == PiShutdownPhase.HaltIssued)
                    {
                        haltIssued = true;
                        haltAt = _clock.Elapsed;
                    }
                }
            }
            else
            {
                polls++;
                PiShutdownObservation status;
                try
                {
                    status = await _channel.GetStatusAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new ShutdownResult(ShutdownOutcome.Aborted, _clock.Elapsed, posts, polls, "cancelled");
                }

                if (status.Phase == PiShutdownPhase.HelperMissing)
                {
                    return new ShutdownResult(ShutdownOutcome.HelperMissing, _clock.Elapsed, posts, polls, "helper-missing");
                }

                if (status.Phase == PiShutdownPhase.HaltIssued)
                {
                    if (!haltIssued)
                    {
                        haltIssued = true;
                        haltAt = _clock.Elapsed;
                    }
                }
            }

            if (haltIssued && _clock.Elapsed - haltAt >= _policy.SettleAfterHaltIssued)
            {
                return new ShutdownResult(ShutdownOutcome.HaltIssuedAndSettled, _clock.Elapsed, posts, polls, "settled");
            }

            var remaining = _policy.Grace - _clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var slice = _policy.PollInterval < remaining ? _policy.PollInterval : remaining;
            try
            {
                await _clock.DelayAsync(slice, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new ShutdownResult(ShutdownOutcome.Aborted, _clock.Elapsed, posts, polls, "cancelled");
            }
        }

        if (haltIssued)
        {
            return new ShutdownResult(ShutdownOutcome.HaltIssuedSettleCutShort, _clock.Elapsed, posts, polls, "settle cut short by grace");
        }

        return new ShutdownResult(
            posted ? ShutdownOutcome.GraceExpired : ShutdownOutcome.AbsentProceeded,
            _clock.Elapsed,
            posts,
            polls,
            posted ? "grace expired before halt-issued" : "absent");
    }
}
