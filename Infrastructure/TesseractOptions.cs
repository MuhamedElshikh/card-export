namespace IdCardReader.Infrastructure;

/// <summary>Tuning knobs for the offline Tesseract OCR pipeline.</summary>
public sealed class TesseractOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ocr";

    /// <summary>Maximum number of scans processed at the same time (1 on dual core machines).</summary>
    public int MaxConcurrentScans { get; set; } = 1;

    /// <summary>
    /// How many more scans may wait for a free slot while <see cref="MaxConcurrentScans"/> are running.
    /// Further uploads are refused at once (HTTP 503) instead of queueing without limit.
    /// </summary>
    public int MaxQueuedScans { get; set; } = 4;

    /// <summary>
    /// Time budget for reading one card, counted from when its scan starts running. The budget is
    /// checked between OCR passes, so a scan can overshoot by at most one pass. Zero disables it.
    /// </summary>
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Directory holding the bundled traineddata files. Empty means next to the executable.</summary>
    public string? TessdataPath { get; set; }

    /// <summary>Longest edge of the image used to locate the 14 digit number.</summary>
    public int NumberLongEdge { get; set; } = 1100;

    /// <summary>Longest edge of the image used to read the name and address.</summary>
    public int TextLongEdge { get; set; } = 1800;

}
