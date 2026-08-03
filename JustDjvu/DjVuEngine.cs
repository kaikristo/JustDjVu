using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media.Imaging;

namespace JustDjvu;

public sealed class DjVuEngine : IDisposable
{
    private readonly string _toolsDirectory;
    private readonly object _cacheLock = new();
    private readonly Dictionary<RenderRequest, BitmapSource> _cache = [];
    private readonly LinkedList<RenderRequest> _lru = [];
    private readonly ConcurrentDictionary<RenderRequest, Task<BitmapSource>> _inflight = new();
    private readonly SemaphoreSlim _pageRenderGate = new(2, 2);
    private readonly SemaphoreSlim _thumbnailRenderGate = new(1, 1);
    private CancellationTokenSource _lifetime = new();
    private Guid _documentId;

    public DjVuEngine() =>
        _toolsDirectory = Path.Combine(AppContext.BaseDirectory, "Tools", "DjVuLibre");

    public string? DocumentPath { get; private set; }
    public int PageCount { get; private set; }

    public async Task OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                LocalizationService.Translate("Файл не найден."), path);
        }

        var extension = Path.GetExtension(path);
        if (!extension.Equals(".djvu", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".djv", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                LocalizationService.Translate("Поддерживаются файлы .djvu и .djv."));
        }

        EnsureTools();
        Reset();
        DocumentPath = path;
        _documentId = Guid.NewGuid();

        var result = await RunToolAsync("djvused.exe", [path, "-e", "n"], cancellationToken);
        if (!int.TryParse(result.StandardOutput.Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var pageCount) || pageCount < 1)
        {
            throw new InvalidDataException(
                string.IsNullOrWhiteSpace(result.StandardError)
                    ? LocalizationService.Translate("Не удалось определить количество страниц DjVu.")
                    : result.StandardError.Trim());
        }

        PageCount = pageCount;
    }

    public async Task<BitmapSource> RenderPageAsync(
        int page, bool thumbnail, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        page = Math.Clamp(page, 1, PageCount);
        var request = new RenderRequest(_documentId, page, thumbnail);
        if (TryGetCached(request, out var cached))
        {
            return cached!;
        }

        var documentPath = DocumentPath!;
        var lifetimeToken = _lifetime.Token;
        var renderTask = _inflight.GetOrAdd(request,
            _ => RenderUncachedAsync(request, documentPath, lifetimeToken));
        try
        {
            return await renderTask.WaitAsync(cancellationToken);
        }
        finally
        {
            if (renderTask.IsCompleted)
            {
                _inflight.TryRemove(
                    new KeyValuePair<RenderRequest, Task<BitmapSource>>(request, renderTask));
            }
        }
    }

    private async Task<BitmapSource> RenderUncachedAsync(
        RenderRequest request, string documentPath, CancellationToken cancellationToken)
    {
        var gate = request.Thumbnail ? _thumbnailRenderGate : _pageRenderGate;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGetCached(request, out var cached))
            {
                return cached!;
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "JustDjVu");
            Directory.CreateDirectory(tempDirectory);
            var outputPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.tif");
            try
            {
                var arguments = new List<string>
                {
                    "-format=tiff",
                    $"-page={request.Page}"
                };
                if (request.Thumbnail)
                {
                    arguments.Add("-size=180x240");
                }
                arguments.Add(documentPath);
                arguments.Add(outputPath);

                var result = await RunToolAsync("ddjvu.exe", arguments, cancellationToken);
                if (!File.Exists(outputPath))
                {
                    throw new InvalidDataException(
                        string.IsNullOrWhiteSpace(result.StandardError)
                            ? LocalizationService.Format(
                                "Не удалось отобразить страницу {0}.", request.Page)
                            : result.StandardError.Trim());
                }

                var bitmap = LoadBitmap(outputPath);
                AddToCache(request, bitmap);
                return bitmap;
            }
            finally
            {
                TryDelete(outputPath);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<string> ExtractPageTextAsync(int page, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        var result = await RunToolAsync(
            "djvutxt.exe", [$"--page={page}", DocumentPath!], cancellationToken);
        return result.StandardOutput;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var results = new List<SearchResult>();
        for (var page = 1; page <= PageCount; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = await ExtractPageTextAsync(page, cancellationToken);
            var index = text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
            if (index >= 0)
            {
                results.Add(new SearchResult(page, CreateSnippet(text, index, query.Length)));
            }
            progress?.Report(page / (double)PageCount);
        }

        return results;
    }

    public void Reset()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _lifetime = new CancellationTokenSource();
        _documentId = Guid.Empty;
        _inflight.Clear();
        lock (_cacheLock)
        {
            _cache.Clear();
            _lru.Clear();
        }
        DocumentPath = null;
        PageCount = 0;
    }

    private void EnsureTools()
    {
        foreach (var tool in new[] { "ddjvu.exe", "djvused.exe", "djvutxt.exe" })
        {
            if (!File.Exists(Path.Combine(_toolsDirectory, tool)))
            {
                throw new FileNotFoundException(
                    LocalizationService.Format(
                        "Компонент DjVuLibre «{0}» не найден рядом с приложением.", tool));
            }
        }
    }

    private void EnsureOpen()
    {
        if (DocumentPath is null || PageCount < 1)
        {
            throw new InvalidOperationException(
                LocalizationService.Translate("Документ не открыт."));
        }
    }

    private async Task<ProcessResult> RunToolAsync(
        string tool, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(_toolsDirectory, tool),
            WorkingDirectory = _toolsDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                LocalizationService.Format("Не удалось запустить {0}.", tool));
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch
            {
                // Процесс уже завершился.
            }
            throw;
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException(
                string.IsNullOrWhiteSpace(error)
                    ? $"{tool} завершился с кодом {process.ExitCode}."
                    : error.Trim());
        }

        return new ProcessResult(output, error);
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var decoder = BitmapDecoder.Create(
            stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private bool TryGetCached(RenderRequest key, out BitmapSource? bitmap)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(key, out bitmap))
            {
                Touch(key);
                return true;
            }
        }

        bitmap = null;
        return false;
    }

    private void AddToCache(RenderRequest key, BitmapSource bitmap)
    {
        lock (_cacheLock)
        {
            _cache[key] = bitmap;
            Touch(key);

            var fullPageCount = _cache.Keys.Count(x => !x.Thumbnail);
            var thumbnailCount = _cache.Keys.Count(x => x.Thumbnail);
            while (fullPageCount > 14 || thumbnailCount > 80)
            {
                var candidate = _lru.First;
                while (candidate is not null &&
                       ((candidate.Value.Thumbnail && thumbnailCount <= 80) ||
                        (!candidate.Value.Thumbnail && fullPageCount <= 14)))
                {
                    candidate = candidate.Next;
                }

                if (candidate is null)
                {
                    break;
                }

                _lru.Remove(candidate);
                _cache.Remove(candidate.Value);
                if (candidate.Value.Thumbnail) thumbnailCount--;
                else fullPageCount--;
            }
        }
    }

    private void Touch(RenderRequest key)
    {
        var existing = _lru.Find(key);
        if (existing is not null)
        {
            _lru.Remove(existing);
        }
        _lru.AddLast(key);
    }

    private static string CreateSnippet(string text, int index, int length)
    {
        var start = Math.Max(0, index - 55);
        var end = Math.Min(text.Length, index + length + 90);
        var snippet = text[start..end]
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ');
        while (snippet.Contains("  ", StringComparison.Ordinal))
        {
            snippet = snippet.Replace("  ", " ", StringComparison.Ordinal);
        }
        return (start > 0 ? "… " : "") + snippet.Trim() + (end < text.Length ? " …" : "");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Временный файл удалит очистка temp.
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        _pageRenderGate.Dispose();
        _thumbnailRenderGate.Dispose();
    }

    private readonly record struct RenderRequest(Guid DocumentId, int Page, bool Thumbnail);
    private sealed record ProcessResult(string StandardOutput, string StandardError);
}
