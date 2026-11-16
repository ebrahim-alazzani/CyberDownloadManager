using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CyberDownloadManager.Application.Interfaces;
using CyberDownloadManager.Domain.Interfaces;
using CyberDownloadManager.Infrastructure.Engine;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace CyberDownloadManager.Presentation.Views;

public class PlaylistVideoItem : INotifyPropertyChanged
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public long DurationSeconds { get; set; }

    public string DurationText => DurationSeconds > 0
        ? TimeSpan.FromSeconds(DurationSeconds).ToString(DurationSeconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss")
        : "--:--";

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class PlaylistDialog : ContentDialog
{
    private readonly string _playlistUrl;
    private readonly string _savePath;
    private readonly ObservableCollection<PlaylistVideoItem> _videos = new();

    public PlaylistDialog(string playlistUrl, string savePath)
    {
        this.InitializeComponent();
        _playlistUrl = playlistUrl;
        _savePath = savePath;
        VideoListView.ItemsSource = _videos;
        _ = LoadPlaylistAsync();
    }

    private async Task LoadPlaylistAsync()
    {
        try
        {
            var videoService = App.Services?.GetService<IVideoDownloadService>();
            if (videoService == null)
            {
                ShowError();
                return;
            }

            var playlist = await videoService.GetPlaylistInfoAsync(_playlistUrl);
            if (playlist == null || playlist.Entries.Count == 0)
            {
                ShowError();
                return;
            }

            PlaylistTitleText.Text = playlist.Title;
            this.Title = $"Playlist: {playlist.Title}";

            foreach (var entry in playlist.Entries)
            {
                _videos.Add(new PlaylistVideoItem
                {
                    Title = entry.Title,
                    Url = entry.Url,
                    DurationSeconds = entry.DurationSeconds,
                    IsSelected = true
                });
            }

            UpdateCountText();
            LoadingPanel.Visibility = Visibility.Collapsed;
            ContentPanel.Visibility = Visibility.Visible;
        }
        catch
        {
            ShowError();
        }
    }

    private void ShowError()
    {
        LoadingPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Visible;
        IsPrimaryButtonEnabled = false;
    }

    private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var v in _videos) v.IsSelected = true;
        UpdateCountText();
    }

    private void BtnDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var v in _videos) v.IsSelected = false;
        UpdateCountText();
    }

    private void CheckBox_Changed(object sender, RoutedEventArgs e) => UpdateCountText();

    private void UpdateCountText()
    {
        int selected = _videos.Count(v => v.IsSelected);
        CountText.Text = $"{selected} / {_videos.Count} selected";
        IsPrimaryButtonEnabled = selected > 0;
    }

    private async void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var videoService = App.Services?.GetService<IVideoDownloadService>();
            var repository = App.Services?.GetService<IDownloadRepository>();
            if (videoService == null || repository == null) return;

            string quality = "1080p";
            if (QualityComboBox?.SelectedItem is ComboBoxItem selected)
                quality = selected.Tag?.ToString() ?? "1080p";

            var selectedVideos = _videos.Where(v => v.IsSelected).ToList();

            foreach (var video in selectedVideos)
            {
                string fileName = YtDlpService.SanitizeFileName(video.Title) + ".mp4";
                string fullPath = System.IO.Path.Combine(_savePath, fileName);

                var item = new Domain.Entities.DownloadItem
                {
                    Id = Guid.NewGuid(),
                    Url = video.Url,
                    FileName = fileName,
                    FilePath = fullPath,
                    Status = Domain.Enums.DownloadStatus.Pending,
                    CreatedAt = DateTime.UtcNow
                };
                await repository.AddAsync(item);

                // Start each download in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        item.Status = Domain.Enums.DownloadStatus.Downloading;
                        await repository.UpdateAsync(item);

                        var progress = new Progress<DownloadProgress>(p =>
                        {
                            item.DownloadedBytes = p.DownloadedBytes;
                            if (p.TotalBytes > 0) item.FileSize = p.TotalBytes;
                            _ = Task.Run(async () => { try { await repository.UpdateAsync(item); } catch { } });
                        });

                        item.FilePath = await videoService.DownloadVideoAsync(video.Url, quality, fullPath, progress);
                        item.FileName = System.IO.Path.GetFileName(item.FilePath);

                        // Get actual file size
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
                        Serilog.Log.Error(ex, "Playlist video download failed for download {DownloadId}", item.Id);
                        item.Status = Domain.Enums.DownloadStatus.Failed;
                        item.ErrorMessage = ex.Message;
                        try { await repository.UpdateAsync(item); } catch { }
                    }
                });

                // Stagger downloads slightly to avoid overwhelming yt-dlp
                await Task.Delay(500);
            }
        }
        catch { }
        finally
        {
            deferral.Complete();
        }
    }
}
