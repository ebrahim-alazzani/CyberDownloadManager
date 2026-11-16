using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using CyberDownloadManager.Application.Interfaces;
using CyberDownloadManager.Shared.Results;
using Serilog;

namespace CyberDownloadManager.Infrastructure.Engine;

public sealed class DownloadEngineImpl : IDownloadEngine, IDisposable
{
    private const int BufferSize = 81920;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _activeCts = new();

    public DownloadEngineImpl(ILogger? logger = null, HttpClient? httpClient = null)
    {
        _logger = logger;
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        if (!_httpClient.DefaultRequestHeaders.UserAgent.Any())
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("CyberDownloadManager/1.0");
    }

    public async Task<Result> StartAsync(Guid downloadId, string url, string filePath, int segmentCount,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!_activeCts.TryAdd(downloadId, cts))
        {
            cts.Dispose();
            return Result.Failure(new Error("Download.AlreadyRunning", "This download is already running."));
        }

        try
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return Result.Failure(new Error("Download.InvalidUrl", "Enter a valid HTTP or HTTPS URL."));

            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var (length, supportsRanges) = await ProbeAsync(url, cts.Token);
            if (length > 0 && supportsRanges && segmentCount > 1)
            {
                try
                {
                    await DownloadSegmentsAsync(downloadId, url, filePath, length,
                        Math.Clamp(segmentCount, 1, 32), progress, cts.Token);
                    return Result.Success();
                }
                catch (InvalidRangeResponseException ex)
                {
                    _logger?.Warning(ex, "Server did not honor range requests for {DownloadId}; restarting as a single stream", downloadId);
                    DeleteSegmentFiles(filePath, Math.Clamp(segmentCount, 1, 32));
                }
            }

            await DownloadSingleStreamAsync(downloadId, url, filePath, length, progress, cts.Token);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure(new Error("Download.Cancelled", "Download was paused or cancelled."));
        }
        catch (Exception ex)
        {
            _logger?.Error(ex, "Download {DownloadId} failed", downloadId);
            return Result.Failure(new Error("Download.Failed", ex.Message));
        }
        finally
        {
            _activeCts.TryRemove(downloadId, out _);
            cts.Dispose();
        }
    }

    public Task<Result> PauseAsync(Guid downloadId, CancellationToken cancellationToken = default) =>
        CancelActiveAsync(downloadId, "Download.NotRunning");

    public Task<Result> ResumeAsync(Guid downloadId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());

    public Task<Result> CancelAsync(Guid downloadId, CancellationToken cancellationToken = default) =>
        CancelActiveAsync(downloadId, "Download.NotRunning");

    private Task<Result> CancelActiveAsync(Guid id, string errorCode)
    {
        if (!_activeCts.TryGetValue(id, out var cts))
            return Task.FromResult(Result.Failure(new Error(errorCode, "Download is not currently running.")));
        cts.Cancel();
        return Task.FromResult(Result.Success());
    }

    private async Task<(long Length, bool SupportsRanges)> ProbeAsync(string url, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.IsSuccessStatusCode)
                return (response.Content.Headers.ContentLength ?? -1,
                    response.Headers.AcceptRanges?.Any(x => x.Equals("bytes", StringComparison.OrdinalIgnoreCase)) == true);
        }
        catch (HttpRequestException) { }
        return (-1, false);
    }

    private async Task DownloadSingleStreamAsync(Guid id, string url, string filePath, long totalBytes,
        IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var partialPath = filePath + ".download";
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var offset = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (offset > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                {
                    offset = 0;
                    File.Delete(partialPath);
                }
                response.EnsureSuccessStatusCode();
                if (response.StatusCode == HttpStatusCode.PartialContent &&
                    response.Content.Headers.ContentRange?.From != offset)
                    throw new InvalidRangeResponseException("The server returned an unexpected byte range.");

                totalBytes = response.Content.Headers.ContentRange?.Length ??
                    (totalBytes > 0 ? totalBytes : response.Content.Headers.ContentLength ?? -1);
                var downloaded = offset;
                var watch = Stopwatch.StartNew();
                await using (var input = await response.Content.ReadAsStreamAsync(ct))
                await using (var output = new FileStream(partialPath,
                    offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.Read, BufferSize, true))
                {
                    var buffer = new byte[BufferSize];
                    int read;
                    while ((read = await input.ReadAsync(buffer.AsMemory(), ct)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        downloaded += read;
                        ReportProgress(progress, id, totalBytes, downloaded, watch);
                    }
                }

                if (totalBytes > 0 && downloaded != totalBytes)
                    throw new IOException($"The response ended at {downloaded} of {totalBytes} bytes.");
                File.Move(partialPath, filePath, true);
                ReportProgress(progress, id, totalBytes, downloaded, watch);
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException && attempt < 3)
            {
                _logger?.Warning(ex, "Download {DownloadId} attempt {Attempt} failed", id, attempt);
                await Task.Delay(TimeSpan.FromMilliseconds(300 * attempt), ct);
            }
        }
        throw new IOException("The download failed after three attempts.");
    }

    private async Task DownloadSegmentsAsync(Guid id, string url, string filePath, long totalBytes,
        int segmentCount, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        segmentCount = (int)Math.Min(segmentCount, Math.Max(1, totalBytes / (256 * 1024)));
        var segmentSize = (totalBytes + segmentCount - 1) / segmentCount;
        var segmentPaths = Enumerable.Range(0, segmentCount).Select(i => $"{filePath}.part{i}").ToArray();
        var completed = new long[segmentCount];
        var watch = Stopwatch.StartNew();
        using var progressCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var progressTask = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    ReportProgress(progress, id, totalBytes, completed.Sum(), watch);
                    await Task.Delay(250, progressCts.Token);
                }
            }
            catch (OperationCanceledException) { }
        }, CancellationToken.None);

        try
        {
            var tasks = Enumerable.Range(0, segmentCount).Select(async index =>
            {
                var start = (long)index * segmentSize;
                var end = Math.Min(totalBytes - 1, start + segmentSize - 1);
                var existing = File.Exists(segmentPaths[index]) ? new FileInfo(segmentPaths[index]).Length : 0;
                if (existing > end - start + 1)
                {
                    File.Delete(segmentPaths[index]);
                    existing = 0;
                }
                completed[index] = existing;
                if (existing == end - start + 1) return;

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new RangeHeaderValue(start + existing, end);
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode != HttpStatusCode.PartialContent ||
                    response.Content.Headers.ContentRange?.From != start + existing)
                    throw new InvalidRangeResponseException("The server did not return the requested segment.");
                await using var input = await response.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(segmentPaths[index], existing > 0 ? FileMode.Append : FileMode.Create,
                    FileAccess.Write, FileShare.Read, BufferSize, true);
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer.AsMemory(), ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    completed[index] += read;
                }
                if (completed[index] != end - start + 1)
                    throw new IOException($"Segment {index} is incomplete.");
            }).ToArray();

            await Task.WhenAll(tasks);
        }
        finally
        {
            progressCts.Cancel();
            await progressTask;
        }

        var tempOutput = filePath + ".download";
        await using (var output = new FileStream(tempOutput, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true))
        {
            foreach (var segmentPath in segmentPaths)
            {
                await using var part = new FileStream(segmentPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, true);
                await part.CopyToAsync(output, ct);
            }
        }
        if (new FileInfo(tempOutput).Length != totalBytes)
            throw new IOException("The assembled file size does not match the server response.");
        File.Move(tempOutput, filePath, true);
        DeleteSegmentFiles(filePath, segmentCount);
        ReportProgress(progress, id, totalBytes, totalBytes, watch);
    }

    private static void DeleteSegmentFiles(string filePath, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var path = $"{filePath}.part{i}";
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void ReportProgress(IProgress<DownloadProgress>? progress, Guid id,
        long totalBytes, long downloadedBytes, Stopwatch watch)
    {
        if (progress is null) return;
        var speed = watch.Elapsed.TotalSeconds > 0 ? downloadedBytes / watch.Elapsed.TotalSeconds : 0;
        var remaining = totalBytes > downloadedBytes ? totalBytes - downloadedBytes : 0;
        var eta = speed > 0 ? TimeSpan.FromSeconds(remaining / speed) : TimeSpan.Zero;
        progress.Report(new DownloadProgress(id, totalBytes, downloadedBytes, speed,
            totalBytes > 0 ? downloadedBytes * 100d / totalBytes : 0, eta));
    }

    public void Dispose()
    {
        foreach (var cts in _activeCts.Values) cts.Cancel();
        if (_ownsHttpClient) _httpClient.Dispose();
    }

    private sealed class InvalidRangeResponseException(string message) : IOException(message);
}
