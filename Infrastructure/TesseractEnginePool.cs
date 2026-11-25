using System.Collections.Concurrent;
using Tesseract;

namespace IdCardReader.Infrastructure;

/// <summary>
/// Reuses <see cref="TesseractEngine"/> instances instead of building one per scan.
/// Tesseract engines are not thread safe, so an engine is leased to exactly one caller at a time
/// and returned when the lease is disposed. The pool never holds more engines than the scan
/// concurrency limit, because no more leases than that can be outstanding.
/// </summary>
internal sealed class TesseractEnginePool : IDisposable
{
    private readonly ConcurrentBag<TesseractEngine> _engines = new();
    private readonly string _tessdataPath;
    private readonly string _language;
    private volatile bool _disposed;

    public TesseractEnginePool(string tessdataPath, string language)
    {
        _tessdataPath = tessdataPath;
        _language = language;
        IsAvailable = Directory.Exists(tessdataPath)
            && File.Exists(Path.Combine(tessdataPath, language + ".traineddata"));
    }

    /// <summary>False when the traineddata file is missing, so the caller can degrade gracefully.</summary>
    public bool IsAvailable { get; }

    /// <summary>Leases an engine, or null when the language data is not installed.</summary>
    public Lease? Rent()
    {
        if (!IsAvailable || _disposed)
        {
            return null;
        }

        if (_engines.TryTake(out var pooled))
        {
            return new Lease(this, pooled);
        }

        var engine = Create();
        return new Lease(this, engine);
    }

    private TesseractEngine Create()
    {
        // The bundled traineddata files are LSTM only, so OEM 1 is required (OEM 3 needs legacy data).
        var engine = new TesseractEngine(_tessdataPath, _language, EngineMode.LstmOnly);
        engine.SetVariable("user_defined_dpi", "300");
        engine.SetVariable("tessedit_do_osd", "0");
        engine.SetVariable("preserve_interword_spaces", "1");
        return engine;
    }

    private void Return(TesseractEngine engine)
    {
        if (_disposed)
        {
            engine.Dispose();
            return;
        }

        _engines.Add(engine);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        while (_engines.TryTake(out var engine))
        {
            engine.Dispose();
        }
    }

    /// <summary>A borrowed engine; disposing the lease gives it back to the pool.</summary>
    internal sealed class Lease : IDisposable
    {
        private readonly TesseractEnginePool _pool;
        private bool _returned;

        public Lease(TesseractEnginePool pool, TesseractEngine engine)
        {
            _pool = pool;
            Engine = engine;
        }

        public TesseractEngine Engine { get; }

        public void Dispose()
        {
            if (_returned)
            {
                return;
            }

            _returned = true;
            _pool.Return(Engine);
        }
    }
}
