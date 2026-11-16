using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using CyberDownloadManager.Application.Interfaces;
using Serilog;

namespace CyberDownloadManager.Infrastructure.Engine;

/// <summary>
/// Wraps yt-dlp.exe to extract video metadata and download videos from 1000+ sites
/// including YouTube, Facebook, Twitter/X, Instagram, TikTok, etc.
/// </summary>
public class YtDlpService : IVideoDownloadService
{
    private static readonly string[] VideoHostPatterns =
    [
        "youtube.com", "youtu.be", "facebook.com", "fb.watch",
        "twitter.com", "x.com", "instagram.com", "tiktok.com",
        "vimeo.com", "dailymotion.com", "twitch.tv", "reddit.com",
        "soundcloud.com", "bilibili.com", "nicovideo.jp"
    ];

    private string? _ytDlpPath;
    private readonly SemaphoreSlim _ytDlpUpdateLock = new(1, 1);
    private DateTime _lastYtDlpUpdateCheckUtc = DateTime.MinValue;

    public bool IsVideoUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            var host = new Uri(url).Host.ToLowerInvariant();
            return VideoHostPatterns.Any(p => host.Contains(p));
        }
        catch { return false; }
    }

    public bool IsPlaylistUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            var uri = new Uri(url);
            var lower = url.ToLowerInvariant();
            // YouTube playlists: contains list= parameter or /playlist path
            if (lower.Contains("list=") || lower.Contains("/playlist"))
                return true;
            return false;
        }
        catch { return false; }
    }

    public async Task<VideoInfo?> GetVideoInfoAsync(string url, CancellationToken cancellationToken = default)
    {
        var ytDlp = await GetYtDlpPathAsync(cancellationToken);
        if (ytDlp == null) return null;

        try
        {
            // yt-dlp --dump-json --no-download <url>
            var arguments = new List<string>();
            await AddYoutubeRuntimeArgumentsAsync(url, arguments, cancellationToken);
            arguments.AddRange(["--dump-single-json", "--skip-download", "--no-playlist", "--no-warnings", url]);
            var output = await RunProcessAsync(ytDlp, arguments, cancellationToken);
            if (string.IsNullOrWhiteSpace(output)) return null;

            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;

            var title = root.GetProperty("title").GetString() ?? "Unknown";
            var thumbnail = root.TryGetProperty("thumbnail", out var th) ? th.GetString() ?? "" : "";
            var duration = root.TryGetProperty("duration", out var dur) ? (long)dur.GetDouble() : 0;

            var formats = new List<VideoFormat>();
            if (root.TryGetProperty("formats", out var fmts))
            {
                foreach (var fmt in fmts.EnumerateArray())
                {
                    var fmtId = fmt.TryGetProperty("format_id", out var fi) ? fi.GetString() ?? "" : "";
                    var height = fmt.TryGetProperty("height", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : 0;
                    var ext = fmt.TryGetProperty("ext", out var e) ? e.GetString() ?? "mp4" : "mp4";
                    var fileSize = fmt.TryGetProperty("filesize", out var fs) && fs.ValueKind == JsonValueKind.Number ? (long?)fs.GetInt64() : null;
                    var vcodec = fmt.TryGetProperty("vcodec", out var vc) ? vc.GetString() ?? "" : "";

                    // Only include video formats (not audio-only)
                    if (height > 0 && vcodec != "none")
                    {
                        formats.Add(new VideoFormat(fmtId, $"{height}p", ext, fileSize));
                    }
                }
            }

            // Deduplicate by quality, keep best (largest size)
            var distinctFormats = formats
                .GroupBy(f => f.Quality)
                .Select(g => g.OrderByDescending(f => f.FileSizeBytes ?? 0).First())
                .OrderByDescending(f => int.TryParse(f.Quality.Replace("p", ""), out var h) ? h : 0)
                .ToList();

            return new VideoInfo(title, url, thumbnail, duration, distinctFormats);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not retrieve video metadata for {VideoUrl}", url);
            return null;
        }
    }

    public async Task<PlaylistInfo?> GetPlaylistInfoAsync(string url, CancellationToken cancellationToken = default)
    {
        var ytDlp = await GetYtDlpPathAsync(cancellationToken);
        if (ytDlp == null) return null;

        try
        {
            // --flat-playlist gives us entries without downloading each video's full metadata
            var arguments = new List<string>();
            await AddYoutubeRuntimeArgumentsAsync(url, arguments, cancellationToken);
            arguments.AddRange(["--flat-playlist", "--dump-json", url]);
            var output = await RunProcessAsync(ytDlp, arguments, cancellationToken, timeoutMs: 30000);
            if (string.IsNullOrWhiteSpace(output)) return null;

            var entries = new List<PlaylistEntry>();
            string playlistTitle = "Playlist";

            // Each line is a separate JSON object (one per video)
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || !trimmed.StartsWith("{")) continue;

                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    var root = doc.RootElement;

                    // Try to get playlist title from first entry
                    if (entries.Count == 0 && root.TryGetProperty("playlist_title", out var pt))
                    {
                        playlistTitle = pt.GetString() ?? "Playlist";
                    }

                    var title = root.TryGetProperty("title", out var t) ? t.GetString() ?? "Untitled" : "Untitled";
                    var videoUrl = root.TryGetProperty("url", out var vu) ? vu.GetString() ?? "" : "";
                    var duration = root.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? (long)d.GetDouble() : 0;

                    if (string.IsNullOrEmpty(videoUrl) || videoUrl == "NA")
                    {
                        // Fallback: use id to construct YouTube URL
                        var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                        if (!string.IsNullOrEmpty(id))
                            videoUrl = $"https://www.youtube.com/watch?v={id}";
                    }

                    if (!string.IsNullOrEmpty(videoUrl))
                    {
                        entries.Add(new PlaylistEntry(title, videoUrl, duration));
                    }
                }
                catch { /* skip malformed line */ }
            }

            if (entries.Count == 0) return null;
            return new PlaylistInfo(playlistTitle, url, entries);
        }
        catch
        {
            return null;
        }
    }

    public async Task<string> DownloadVideoAsync(string url, string quality, string savePath,
        IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var ytDlp = await GetYtDlpPathAsync(cancellationToken);
        if (ytDlp == null) throw new InvalidOperationException("yt-dlp not found. Install it first.");

        var runtimeArguments = new List<string>();
        await AddYoutubeRuntimeArgumentsAsync(url, runtimeArguments, cancellationToken);

        var dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var ffmpegPath = await GetFfmpegPathAsync(cancellationToken);

        var height = int.TryParse(quality.Replace("p", "", StringComparison.OrdinalIgnoreCase), out var requestedHeight)
            ? requestedHeight
            : throw new ArgumentException("Choose a supported video quality.", nameof(quality));
        string formatArg;

        if (!string.IsNullOrEmpty(ffmpegPath))
        {
            // With ffmpeg available: download best video + best audio and merge into single MP4
            formatArg = $"bestvideo[height<={height}]+bestaudio/best[height<={height}]";
        }
        else
        {
            // Without ffmpeg, only select formats that already contain both audio and video.
            formatArg = $"best[height<={height}]/b[height<={height}]";
        }

        var outputDirectory = Path.GetDirectoryName(savePath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new ArgumentException("Choose a valid output file path.", nameof(savePath));

        var outputBaseName = Path.GetFileNameWithoutExtension(savePath);
        var outputTemplate = Path.Combine(outputDirectory, outputBaseName + ".%(ext)s");

        var psi = new ProcessStartInfo
        {
            FileName = ytDlp,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in runtimeArguments)
            psi.ArgumentList.Add(argument);
        psi.ArgumentList.Add("--no-playlist");
        psi.ArgumentList.Add("--socket-timeout");
        psi.ArgumentList.Add("15");
        psi.ArgumentList.Add("--retries");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("--fragment-retries");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("--progress");
        psi.ArgumentList.Add("--newline");
        psi.ArgumentList.Add("--no-overwrites");
        psi.ArgumentList.Add("--concurrent-fragments");
        psi.ArgumentList.Add("4");
        psi.ArgumentList.Add("--format");
        psi.ArgumentList.Add(formatArg);
        if (!string.IsNullOrEmpty(ffmpegPath))
        {
            psi.ArgumentList.Add("--ffmpeg-location");
            psi.ArgumentList.Add(ffmpegPath);
            psi.ArgumentList.Add("--merge-output-format");
            psi.ArgumentList.Add("mp4");
        }
        psi.ArgumentList.Add("--output");
        psi.ArgumentList.Add(outputTemplate);
        psi.ArgumentList.Add("--print");
        psi.ArgumentList.Add("after_move:filepath");
        psi.ArgumentList.Add(url);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start yt-dlp.");
        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        var downloadId = Guid.NewGuid();
        string? reportedOutputPath = null;
        var standardError = new System.Text.StringBuilder();

        // Parse yt-dlp progress: [download]  45.2% of  1.23GiB at 12.5MiB/s ETA 00:42
        // Also:                  [download]  45.2% of ~  1.23GiB at 12.5MiB/s ETA 00:42
        var progressRegex = new Regex(@"\[download\]\s+([\d.]+)%\s+of\s+~?\s*([\d.]+)(\w+)");

        long maxTotalBytes = 0;
        long maxReportedDownloadedBytes = 0;
        var progressSync = new object();
        void ReportProgress(long totalBytes, long downloadedBytes, double percentage)
        {
            if (progress == null) return;
            lock (progressSync)
            {
                maxReportedDownloadedBytes = Math.Max(maxReportedDownloadedBytes, downloadedBytes);
                progress.Report(new DownloadProgress(downloadId, totalBytes, maxReportedDownloadedBytes, 0, percentage, TimeSpan.Zero));
            }
        }

        async Task ReadOutputAsync(StreamReader reader, bool isError)
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                if (isError)
                {
                    lock (standardError) standardError.AppendLine(line);
                }
                else
                {
                    var candidatePath = line.Trim();
                    if (Path.IsPathFullyQualified(candidatePath) &&
                        string.Equals(Path.GetDirectoryName(candidatePath), outputDirectory, StringComparison.OrdinalIgnoreCase))
                        reportedOutputPath = candidatePath;
                }

                var match = progressRegex.Match(line);
                if (match.Success && progress != null)
                {
                    double percent = double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 0;
                    double sizeValue = double.TryParse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : 0;
                    long streamTotalBytes = ConvertToBytes(sizeValue, match.Groups[3].Value);
                    long totalBytesToReport;
                    lock (standardError)
                    {
                        maxTotalBytes = Math.Max(maxTotalBytes, streamTotalBytes);
                        totalBytesToReport = maxTotalBytes;
                    }

                    long downloadedBytes = (long)(totalBytesToReport * percent / 100.0);
                    ReportProgress(totalBytesToReport, downloadedBytes, percent);
                }
            }
        }

        var stdoutTask = ReadOutputAsync(process.StandardOutput, isError: false);
        var stderrTask = ReadOutputAsync(process.StandardError, isError: true);
        using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var monitorTask = MonitorPartialFileProgressAsync(outputDirectory, outputBaseName,
            bytes => ReportProgress(0, bytes, 0), monitorCancellation.Token);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        finally
        {
            monitorCancellation.Cancel();
            try { await monitorTask; } catch (OperationCanceledException) { }
        }
        await Task.WhenAll(stdoutTask, stderrTask);
        if (process.ExitCode != 0)
        {
            var errorText = standardError.ToString();
            var details = string.IsNullOrWhiteSpace(errorText)
                ? $"yt-dlp exited with code {process.ExitCode}."
                : errorText.Trim();
            throw new InvalidOperationException(details);
        }

        var actualPath = reportedOutputPath;
        if (string.IsNullOrWhiteSpace(actualPath) || !File.Exists(actualPath))
        {
            actualPath = Directory.EnumerateFiles(outputDirectory, outputBaseName + ".*", SearchOption.TopDirectoryOnly)
                .Where(path => !path.EndsWith(".part", StringComparison.OrdinalIgnoreCase) &&
                               !path.EndsWith(".ytdl", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(actualPath) || !File.Exists(actualPath))
            throw new InvalidOperationException("yt-dlp finished without creating the video file.");

        return actualPath;
    }

    private static long ConvertToBytes(double value, string unit)
    {
        var u = unit.ToUpperInvariant().TrimEnd('B', 'I');
        return u switch
        {
            "K" or "KI" => (long)(value * 1024),
            "M" or "MI" => (long)(value * 1024 * 1024),
            "G" or "GI" => (long)(value * 1024 * 1024 * 1024),
            "T" or "TI" => (long)(value * 1024L * 1024 * 1024 * 1024),
            _ => (long)value
        };
    }

    private static async Task MonitorPartialFileProgressAsync(string outputDirectory, string outputBaseName,
        Action<long> reportDownloadedBytes, CancellationToken cancellationToken)
    {
        long largestObservedBytes = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                long partialBytes = Directory.EnumerateFiles(outputDirectory, outputBaseName + ".*.part", SearchOption.TopDirectoryOnly)
                    .Sum(path => new FileInfo(path).Length);
                if (partialBytes > largestObservedBytes)
                {
                    largestObservedBytes = partialBytes;
                    reportDownloadedBytes(partialBytes);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    /// <summary>
    /// Sanitize a video title for use as a filename.
    /// </summary>
    public static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        // Trim and limit length
        clean = clean.Trim().TrimEnd('.');
        if (clean.Length > 200) clean = clean[..200];
        return string.IsNullOrWhiteSpace(clean) ? "video" : clean;
    }

    // ──────────── Locate or Auto-Download ffmpeg ────────────
    private async Task<string?> GetFfmpegPathAsync(CancellationToken ct)
    {
        var localDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberDownloadManager");
        var localFfmpeg = Path.Combine(localDir, "ffmpeg.exe");

        if (File.Exists(localFfmpeg)) return localDir;

        // Check if ffmpeg is in PATH
        var pathResult = await RunProcessAsync("where", ["ffmpeg"], ct);
        if (!string.IsNullOrWhiteSpace(pathResult))
        {
            var p = pathResult.Split('\n', '\r').FirstOrDefault()?.Trim();
            if (p != null && File.Exists(p)) return Path.GetDirectoryName(p) ?? localDir;
        }

        // Auto-download official yt-dlp FFmpeg build for Windows
        try
        {
            Directory.CreateDirectory(localDir);
            var tempZip = Path.Combine(Path.GetTempPath(), "ffmpeg_download.zip");
            var extractDir = Path.Combine(Path.GetTempPath(), "ffmpeg_extracted_" + Guid.NewGuid());

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            var downloadUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

            await using (var stream = await httpClient.GetStreamAsync(downloadUrl, ct))
            await using (var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write))
            {
                await stream.CopyToAsync(fileStream, ct);
            }

            if (File.Exists(tempZip))
            {
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, extractDir);
                var foundFfmpeg = Directory.GetFiles(extractDir, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (foundFfmpeg != null)
                {
                    File.Copy(foundFfmpeg, localFfmpeg, true);
                }

                // Cleanup temp
                try { File.Delete(tempZip); } catch { }
                try { Directory.Delete(extractDir, true); } catch { }
            }

            if (File.Exists(localFfmpeg)) return localDir;
        }
        catch { }

        return null;
    }

    // ──────────── Locate or Download yt-dlp ────────────
    private async Task<string?> GetYtDlpPathAsync(CancellationToken ct)
    {
        if (_ytDlpPath != null && File.Exists(_ytDlpPath)) return _ytDlpPath;

        // Prefer the copy managed by this application so its updater never changes
        // a system-wide or user-installed yt-dlp executable.
        var localPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberDownloadManager", "yt-dlp.exe");

        if (File.Exists(localPath))
        {
            await UpdateLocalYtDlpIfOutdatedAsync(localPath, ct);
            _ytDlpPath = localPath;
            return _ytDlpPath;
        }

        // Check if yt-dlp is on PATH
        var pathResult = await RunProcessAsync("where", ["yt-dlp"], ct);
        if (!string.IsNullOrWhiteSpace(pathResult))
        {
            _ytDlpPath = pathResult.Split('\n', '\r').FirstOrDefault()?.Trim();
            if (_ytDlpPath != null && File.Exists(_ytDlpPath)) return _ytDlpPath;
        }

        // Auto-download yt-dlp.exe from GitHub releases
        try
        {
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            await DownloadYtDlpBinaryAsync(localPath, ct);

            _ytDlpPath = localPath;
            return _ytDlpPath;
        }
        catch
        {
            return null;
        }
    }

    private async Task UpdateLocalYtDlpIfOutdatedAsync(string path, CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _lastYtDlpUpdateCheckUtc < TimeSpan.FromDays(1))
            return;

        await _ytDlpUpdateLock.WaitAsync(cancellationToken);
        var updateRequired = false;
        try
        {
            if (DateTime.UtcNow - _lastYtDlpUpdateCheckUtc < TimeSpan.FromDays(1))
                return;

            _lastYtDlpUpdateCheckUtc = DateTime.UtcNow;
            var currentVersion = (await RunProcessAsync(path, ["--version"], cancellationToken, timeoutMs: 5000)).Trim();
            var versionDate = currentVersion.Length >= 10 &&
                              DateOnly.TryParseExact(currentVersion[..10], "yyyy.MM.dd",
                                  System.Globalization.CultureInfo.InvariantCulture,
                                  System.Globalization.DateTimeStyles.None, out var parsedDate)
                ? parsedDate
                : (DateOnly?)null;

            updateRequired = versionDate is null || DateOnly.FromDateTime(DateTime.UtcNow).DayNumber - versionDate.Value.DayNumber > 30;
            if (!updateRequired)
                return;

            Log.Information("Updating bundled yt-dlp from version {YtDlpVersion}", currentVersion);
            await RunProcessAsync(path, ["--update"], cancellationToken, timeoutMs: 30000);
            var updatedVersion = (await RunProcessAsync(path, ["--version"], cancellationToken, timeoutMs: 5000)).Trim();
            if (string.IsNullOrEmpty(updatedVersion) || string.Equals(currentVersion, updatedVersion, StringComparison.Ordinal))
            {
                await DownloadYtDlpBinaryAsync(path, cancellationToken);
                updatedVersion = (await RunProcessAsync(path, ["--version"], cancellationToken, timeoutMs: 5000)).Trim();
            }
            if (!string.IsNullOrEmpty(updatedVersion) && !string.Equals(currentVersion, updatedVersion, StringComparison.Ordinal))
                Log.Information("Updated bundled yt-dlp to version {YtDlpVersion}", updatedVersion);
            else
                Log.Warning("Bundled yt-dlp update check did not change version {YtDlpVersion}", currentVersion);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not update the bundled yt-dlp executable");
            if (updateRequired)
                throw new InvalidOperationException("yt-dlp is outdated and could not be updated. Check your internet connection and try again.", ex);
        }
        finally
        {
            _ytDlpUpdateLock.Release();
        }
    }

    private static async Task DownloadYtDlpBinaryAsync(string targetPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Could not locate the video downloader folder.");
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $"yt-dlp.{Guid.NewGuid():N}.download");
        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CyberDownloadManager/1.0");
            var url = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
            await using (var stream = await client.GetStreamAsync(url, cancellationToken))
            await using (var file = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await stream.CopyToAsync(file, cancellationToken);

            var version = (await RunProcessAsync(tempPath, ["--version"], cancellationToken, timeoutMs: 10000)).Trim();
            if (version.Length < 10 || !DateOnly.TryParseExact(version[..10], "yyyy.MM.dd",
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                throw new InvalidOperationException("The downloaded yt-dlp update could not be verified.");

            File.Move(tempPath, targetPath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        }
    }

    private static async Task AddYoutubeRuntimeArgumentsAsync(string url, List<string> arguments, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !(uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
              uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase)))
            return;

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyberDownloadManager");
        var denoPath = Path.Combine(appData, "deno.exe");
        if (!File.Exists(denoPath))
        {
            var pathResult = await RunProcessAsync("where", ["deno"], cancellationToken);
            denoPath = pathResult.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? denoPath;
        }

        if (!File.Exists(denoPath))
        {
            try
            {
                Directory.CreateDirectory(appData);
                var zipPath = Path.Combine(Path.GetTempPath(), $"deno.{Guid.NewGuid():N}.zip");
                var extractPath = Path.Combine(Path.GetTempPath(), $"deno.{Guid.NewGuid():N}");
                try
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("CyberDownloadManager/1.0");
                    var urlToDeno = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";
                    await using (var stream = await client.GetStreamAsync(urlToDeno, cancellationToken))
                    await using (var file = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await stream.CopyToAsync(file, cancellationToken);
                    System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);
                    var extractedDeno = Directory.GetFiles(extractPath, "deno.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (extractedDeno == null) throw new InvalidOperationException("The Deno runtime download was incomplete.");
                    File.Copy(extractedDeno, denoPath, overwrite: true);
                }
                finally
                {
                    try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
                    try { if (Directory.Exists(extractPath)) Directory.Delete(extractPath, recursive: true); } catch { }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                throw new InvalidOperationException("YouTube downloads need the Deno JavaScript runtime. The app could not download it; check your internet connection and try again.", ex);
            }
        }

        arguments.Add("--js-runtimes");
        arguments.Add($"deno:{denoPath}");
    }

    private static async Task<string> RunProcessAsync(string fileName, IReadOnlyCollection<string> arguments,
        CancellationToken ct, int timeoutMs = 15000)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
                psi.ArgumentList.Add(argument);

            using var process = Process.Start(psi);
            if (process is null) return string.Empty;
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeoutMs);
            using var cancellationRegistration = cts.Token.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            });
            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            var output = await outputTask;
            await errorTask;
            return output;
        }
        catch
        {
            return string.Empty;
        }
    }
}
