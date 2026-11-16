using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CyberDownloadManager.Application.Interfaces;
using CyberDownloadManager.Domain.Interfaces;
using CyberDownloadManager.Infrastructure.Engine;
using CyberDownloadManager.Presentation.Utilities;
using Serilog;
using WinRT.Interop;

namespace CyberDownloadManager.Presentation.Views;

public partial class AddDownloadDialog : ContentDialog
{
    private readonly IVideoDownloadService? _videoService;
    private bool _isVideoUrl;
    private bool _isPlaylistUrl;
    private VideoInfo? _videoInfo;
    private CancellationTokenSource? _metadataCancellation;
    private string _metadataUrl = string.Empty;
    private bool _isUpdatingFileName;
    private bool _fileNameManuallyEdited;

    public AddDownloadDialog()
    {
        this.InitializeComponent();
        SavePathTextBox.Text = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CyberManager");
        if (App.Services != null)
        {
            _videoService = App.Services.GetService<IVideoDownloadService>();
            _ = LoadDefaultPathAsync();
        }
    }

    private async Task LoadDefaultPathAsync()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var defaultPath = await settings.GetSettingAsync("downloads.defaultPath", SavePathTextBox.Text);
        if (string.IsNullOrWhiteSpace(SavePathTextBox.Text) ||
            SavePathTextBox.Text.EndsWith("Downloads\\CyberManager", StringComparison.OrdinalIgnoreCase))
            SavePathTextBox.Text = defaultPath;
    }

    private void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        string url = UrlTextBox.Text?.Trim() ?? string.Empty;
        if (_videoService != null && !string.IsNullOrEmpty(url))
        {
            _isPlaylistUrl = _videoService.IsPlaylistUrl(url);
            _isVideoUrl = _videoService.IsVideoUrl(url);

            if (_isPlaylistUrl)
            {
                _metadataCancellation?.Cancel();
                _metadataUrl = string.Empty;
                VideoDetectedBanner.Visibility = Visibility.Visible;
                VideoTitleText.Text = "📋 Playlist Detected! Click Start to browse videos.";
                QualityComboBox.Visibility = Visibility.Collapsed;
                FileNameTextBox.Visibility = Visibility.Collapsed;
            }
            else if (_isVideoUrl)
            {
                VideoDetectedBanner.Visibility = Visibility.Visible;
                VideoTitleText.Text = "🎬 Video detected. Fetching title...";
                QualityComboBox.Visibility = Visibility.Visible;
                FileNameTextBox.Visibility = Visibility.Visible;
                if (!string.Equals(_metadataUrl, url, StringComparison.Ordinal))
                {
                    _metadataCancellation?.Cancel();
                    _metadataCancellation?.Dispose();
                    _metadataCancellation = new CancellationTokenSource();
                    _metadataUrl = url;
                    _videoInfo = null;
                    _fileNameManuallyEdited = false;
                    SetSuggestedFileName(GetFallbackVideoFileName(url));
                    _ = FetchVideoInfoAsync(url, _metadataCancellation.Token);
                }
            }
            else
            {
                _metadataCancellation?.Cancel();
                _metadataUrl = string.Empty;
                VideoDetectedBanner.Visibility = Visibility.Collapsed;
                FileNameTextBox.Visibility = Visibility.Collapsed;
                _videoInfo = null;
            }
        }
        else
        {
            _metadataCancellation?.Cancel();
            _metadataUrl = string.Empty;
            VideoDetectedBanner.Visibility = Visibility.Collapsed;
            FileNameTextBox.Visibility = Visibility.Collapsed;
            _isVideoUrl = false;
            _isPlaylistUrl = false;
            _videoInfo = null;
        }
    }

    private async Task FetchVideoInfoAsync(string url, CancellationToken cancellationToken)
    {
        if (_videoService == null) return;
        try
        {
            var videoInfo = await _videoService.GetVideoInfoAsync(url, cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                !string.Equals(url, UrlTextBox.Text?.Trim(), StringComparison.Ordinal))
                return;

            _videoInfo = videoInfo;
            if (_videoInfo != null)
            {
                VideoTitleText.Text = $"🎬 {_videoInfo.Title}";
                if (!_fileNameManuallyEdited)
                    SetSuggestedFileName(YtDlpService.SanitizeFileName(_videoInfo.Title));
            }
            else
            {
                VideoTitleText.Text = "🎬 Title unavailable. A name was generated; you can edit it or start the download.";
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not retrieve video metadata from the video provider");
            if (string.Equals(url, UrlTextBox.Text?.Trim(), StringComparison.Ordinal))
                VideoTitleText.Text = "🎬 Could not read the title. A generated file name is ready.";
        }
    }

    private static string GetFallbackVideoFileName(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return $"video_{DateTime.Now:yyyyMMdd_HHmmss}";

        string? videoId = null;
        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            videoId = uri.AbsolutePath.Trim('/').Split('/').FirstOrDefault();
        }
        else
        {
            videoId = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .FirstOrDefault(parts => parts.Length == 2 && parts[0].Equals("v", StringComparison.OrdinalIgnoreCase))?
                .ElementAtOrDefault(1);
        }

        videoId ??= uri.Segments.LastOrDefault()?.Trim('/');
        if (string.IsNullOrWhiteSpace(videoId))
            videoId = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        return YtDlpService.SanitizeFileName($"video_{Uri.UnescapeDataString(videoId)}");
    }

    private void FileNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isUpdatingFileName)
            _fileNameManuallyEdited = true;
    }

    private void SetSuggestedFileName(string fileName)
    {
        _isUpdatingFileName = true;
        FileNameTextBox.Text = fileName;
        _isUpdatingFileName = false;
    }

    private void BrowseSavePath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderPath = NativeFolderPicker.PickFolder(
                WindowNative.GetWindowHandle(App.MainWindow), "Select download folder");
            if (folderPath != null)
                SavePathTextBox.Text = folderPath;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not open the download folder picker");
            ShowError($"Could not open the folder picker (0x{ex.HResult:X8}): {ex.Message}");
        }
    }

    // Called when user clicks "Start Download" (PrimaryButton)
    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        ErrorText.Text = string.Empty;
        ErrorText.Visibility = Visibility.Collapsed;
        try
        {
            string url = UrlTextBox.Text?.Trim() ?? string.Empty;
            string savePath = SavePathTextBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(url))
            {
                args.Cancel = true;
                return;
            }

            if (string.IsNullOrEmpty(savePath))
                savePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CyberManager");

            // ─── Playlist URL: Open PlaylistDialog ───
            if (_isPlaylistUrl && _videoService != null && App.Services != null)
            {
                // Close this dialog, then open PlaylistDialog
                _ = DispatcherQueue.TryEnqueue(async () =>
                {
                    try
                    {
                        var playlistDialog = new PlaylistDialog(url, savePath)
                        {
                            XamlRoot = this.XamlRoot
                        };
                        await playlistDialog.ShowAsync();
                    }
                    catch { }
                });
            }
            // ─── Single Video URL: Download with real title ───
            else if (_isVideoUrl && _videoService != null)
            {
                string quality = "1080p";
                if (QualityComboBox?.SelectedItem is ComboBoxItem selected)
                    quality = selected.Tag?.ToString() ?? "1080p";

                // Prefer a user-entered or fetched title. A provider ID fallback keeps title lookup from blocking downloads.
                string? videoTitle = FileNameTextBox.Text?.Trim();
                if (string.IsNullOrWhiteSpace(videoTitle))
                    videoTitle = _videoInfo?.Title ?? GetFallbackVideoFileName(url);
                if (string.IsNullOrWhiteSpace(videoTitle) || videoTitle == "Unknown")
                    videoTitle = GetFallbackVideoFileName(url);

                string fileBaseName = YtDlpService.SanitizeFileName(System.IO.Path.GetFileNameWithoutExtension(videoTitle));
                if (string.IsNullOrWhiteSpace(fileBaseName))
                {
                    args.Cancel = true;
                    ShowError("Enter a valid file name.");
                    return;
                }

                Directory.CreateDirectory(savePath);
                string fullPath = GetAvailableVideoPath(savePath, fileBaseName);
                string fileName = System.IO.Path.GetFileName(fullPath);

                if (App.Services != null)
                {
                    var repository = App.Services.GetRequiredService<IDownloadRepository>();
                    var item = new Domain.Entities.DownloadItem
                    {
                        Id = Guid.NewGuid(),
                        Url = url,
                        FileName = fileName,
                        FilePath = fullPath,
                        Status = Domain.Enums.DownloadStatus.Downloading,
                        CreatedAt = DateTime.UtcNow
                    };

                    // Try to set initial file size from metadata
                    if (_videoInfo?.Formats.Count > 0)
                    {
                        var bestFormat = _videoInfo.Formats.FirstOrDefault(f => f.Quality == quality)
                            ?? _videoInfo.Formats.FirstOrDefault();
                        if (bestFormat?.FileSizeBytes > 0)
                            item.FileSize = bestFormat.FileSizeBytes.Value;
                    }

                    await repository.AddAsync(item);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var progress = new Progress<DownloadProgress>(p =>
                            {
                                item.DownloadedBytes = p.DownloadedBytes;
                                if (p.TotalBytes > 0) item.FileSize = p.TotalBytes;
                                _ = Task.Run(async () => { try { await repository.UpdateAsync(item); } catch { } });
                            });

                            item.FilePath = await _videoService.DownloadVideoAsync(url, quality, fullPath, progress);
                            item.FileName = System.IO.Path.GetFileName(item.FilePath);

                            if (!System.IO.File.Exists(item.FilePath))
                                throw new InvalidOperationException("The video provider finished without creating the output file.");

                            var fi = new System.IO.FileInfo(item.FilePath);
                            item.FileSize = fi.Length;
                            item.DownloadedBytes = fi.Length;
                            
                            item.Status = Domain.Enums.DownloadStatus.Completed;
                            item.CompletedAt = DateTime.UtcNow;
                            await repository.UpdateAsync(item);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Video download failed for download {DownloadId}", item.Id);
                            item.Status = Domain.Enums.DownloadStatus.Failed;
                            item.ErrorMessage = ex.Message;
                            try { await repository.UpdateAsync(item); } catch { }
                        }
                    });
                }
            }
            // ─── Direct File URL ───
            else
            {
                if (App.Services != null)
                {
                    var downloadService = App.Services.GetRequiredService<IDownloadService>();
                    var result = await downloadService.AddDownloadAsync(url, savePath);
                    if (result.IsFailure)
                    {
                        args.Cancel = true;
                        ShowError(result.Error.Description);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            ShowError(ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var url = UrlTextBox.Text?.Trim() ?? string.Empty;
            var savePath = SavePathTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(url))
            {
                args.Cancel = true;
                ShowError("Enter a URL before adding it to the queue.");
                return;
            }
            if (_isVideoUrl || _isPlaylistUrl)
            {
                args.Cancel = true;
                ShowError("Video and playlist downloads cannot be queued yet. Start them directly.");
                return;
            }
            if (string.IsNullOrWhiteSpace(savePath))
                savePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "CyberManager");

            var result = await App.Services.GetRequiredService<IDownloadService>().AddDownloadToQueueAsync(url, savePath);
            if (result.IsFailure)
            {
                args.Cancel = true;
                ShowError(result.Error.Description);
            }
        }
        catch (Exception ex)
        {
            args.Cancel = true;
            ShowError(ex.Message);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private static string GetAvailableVideoPath(string directory, string fileBaseName)
    {
        var candidate = System.IO.Path.Combine(directory, fileBaseName + ".mp4");
        for (var index = 1; Directory.EnumerateFiles(directory, System.IO.Path.GetFileNameWithoutExtension(candidate) + ".*", SearchOption.TopDirectoryOnly).Any(); index++)
        {
            var nextName = $"{fileBaseName} ({index})";
            candidate = System.IO.Path.Combine(directory, nextName + ".mp4");
            if (!Directory.EnumerateFiles(directory, nextName + ".*", SearchOption.TopDirectoryOnly).Any())
                return candidate;
        }
        return candidate;
    }
}
