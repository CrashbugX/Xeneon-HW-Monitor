namespace Diego.Telemetry.Host;

/// <summary>
/// One process owns sensor collection. The lock is a file opened with no
/// sharing, which works on Linux and Windows. A second UI or the service
/// does not open LibreHardwareMonitor and does not kill the owner.
/// </summary>
public sealed class CollectorOwner : IDisposable
{
    private readonly FileStream? _stream;

    private CollectorOwner(FileStream? stream, bool acquired)
    {
        _stream = stream;
        Acquired = acquired;
    }

    public bool Acquired { get; }

    public static CollectorOwner TryAcquire(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        try
        {
            var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return new CollectorOwner(stream, true);
        }
        catch (IOException)
        {
            return new CollectorOwner(null, false);
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
    }
}
