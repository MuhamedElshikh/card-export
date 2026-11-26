namespace IdCardReader.Infrastructure;

/// <summary>Tuning knobs for the offline Tesseract OCR pipeline.</summary>
public sealed class TesseractOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ocr";

    /// <summary>Maximum number of scans processed at the same time (1 on dual core machines).</summary>
    public int MaxConcurrentScans { get; set; } = 1;

    /// <summary>Directory holding the bundled traineddata files. Empty means next to the executable.</summary>
    public string? TessdataPath { get; set; }

    /// <summary>Longest edge of the image used to locate the 14 digit number.</summary>
    public int NumberLongEdge { get; set; } = 1100;

    /// <summary>Longest edge of the image used to read the name and address.</summary>
    public int TextLongEdge { get; set; } = 1800;

}
