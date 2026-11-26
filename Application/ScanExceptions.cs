namespace IdCardReader.Application;

/// <summary>
/// The server is already reading as many cards as it can run and queue.
/// Surfaced to the client as a 503 so it can retry, instead of piling up requests on a weak CPU.
/// </summary>
public sealed class ScanBusyException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ScanBusyException()
        : base("The OCR engine is busy. Try again in a moment.")
    {
    }
}

/// <summary>
/// Reading one card used up its whole time budget. Surfaced to the client as a 504.
/// </summary>
public sealed class ScanTimeoutException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ScanTimeoutException(TimeSpan budget)
        : base($"Reading the card took longer than {budget.TotalSeconds:F0} seconds.")
    {
    }
}
