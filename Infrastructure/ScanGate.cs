namespace IdCardReader.Infrastructure;

/// <summary>
/// Bounds how many scans may be running or waiting for a free OCR slot at the same time.
/// A scan that cannot get in is refused immediately instead of waiting for minutes behind others,
/// which keeps a CPU-only server responsive when many people upload at once.
/// </summary>
public sealed class ScanGate
{
    private readonly int _capacity;
    private int _admitted;

    /// <summary>Creates a gate that admits at most <paramref name="capacity"/> scans (minimum 1).</summary>
    public ScanGate(int capacity)
    {
        _capacity = Math.Max(1, capacity);
    }

    /// <summary>Scans currently admitted (running plus waiting).</summary>
    public int Admitted => Volatile.Read(ref _admitted);

    /// <summary>Tries to admit one scan. Every successful call must be paired with <see cref="Exit"/>.</summary>
    public bool TryEnter()
    {
        if (Interlocked.Increment(ref _admitted) > _capacity)
        {
            Interlocked.Decrement(ref _admitted);
            return false;
        }

        return true;
    }

    /// <summary>Releases a slot taken by <see cref="TryEnter"/>.</summary>
    public void Exit()
    {
        Interlocked.Decrement(ref _admitted);
    }
}
