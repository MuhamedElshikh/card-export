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

    /// <summary>
    /// Engine used to read the front name/address fields: "paddle" (default) uses the bundled
    /// PP-OCRv5 Arabic model, whose neural line detector finds the text on the card's patterned
    /// background that Tesseract's segmentation misses; "tesseract" keeps the old Tesseract-only
    /// behavior. With "paddle", Tesseract still runs when PP-OCR leaves a field empty.
    /// </summary>
    public string TextEngine { get; set; } = "paddle";

    /// <summary>
    /// When true, the PP-OCR front read also logs the raw text of every line. Off by default:
    /// the lines contain the card holder's personal data. Enable only while tuning a scan, on
    /// your own machine, and turn it back off afterwards.
    /// </summary>
    public bool LogRawText { get; set; } = false;

    /// <summary>
    /// Longest edge of the image used to read the name and address. The card is cropped before
    /// this read, so 1400 px of card shows the text larger than 1800 px of the whole photo did.
    /// </summary>
    public int TextLongEdge { get; set; } = 1400;

    /// <summary>
    /// When true (default), the front text read and the back-side read run on parallel lanes while
    /// the number is being located. On a 4+ core machine this nearly halves the wall time of a scan;
    /// set false on very weak dual-core boxes.
    /// </summary>
    public bool ParallelLanes { get; set; } = true;

    /// <summary>
    /// Directory holding the slower, more accurate models (tessdata_best) used only for the
    /// name/address read. Empty means a "tessdata-best" folder next to the executable; when the
    /// folder or model is missing the fast bundled model is used instead.
    /// </summary>
    public string? BestTessdataPath { get; set; }

}
