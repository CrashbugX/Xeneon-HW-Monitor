// Repeats a sample until count or cancel, then closes the hardware source.
namespace Diego.Telemetry;

public enum WatchStop
{
    Completed,
    Cancelled
}

public sealed class WatchSession
{
    public async Task<WatchStop> RunAsync(
        IHardwareSource source,
        HostConfig config,
        int? count,
        Func<TelemetryDocument, CancellationToken, Task> onSample,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(onSample);
        try
        {
            var sequence = 0;
            while (count is null || sequence < count.Value)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return WatchStop.Cancelled;
                }

                HardwareObservation observation;
                try
                {
                    observation = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return WatchStop.Cancelled;
                }

                var document = SnapshotBuilder.Build(
                    observation,
                    config,
                    clock.UtcNow,
                    sampledOnce: false,
                    sequence);
                try
                {
                    await onSample(document, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return WatchStop.Cancelled;
                }

                sequence++;
                if (count is not null && sequence >= count.Value)
                {
                    return WatchStop.Completed;
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(config.PollIntervalSeconds), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return WatchStop.Cancelled;
                }
            }

            return WatchStop.Completed;
        }
        finally
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }
}
