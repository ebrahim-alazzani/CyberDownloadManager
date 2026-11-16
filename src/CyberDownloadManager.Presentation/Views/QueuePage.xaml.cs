using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CyberDownloadManager.Application.DTOs;
using CyberDownloadManager.Application.Interfaces;

namespace CyberDownloadManager.Presentation.Views;

public partial class QueuePage : Page, INotifyPropertyChanged
{
    public ObservableCollection<DownloadItemDto> QueuedItems { get; } = new();
    public Visibility EmptyQueueVisibility => QueuedItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public event PropertyChangedEventHandler? PropertyChanged;

    public QueuePage()
    {
        this.InitializeComponent();
        this.DataContext = this;
        QueuedItems.CollectionChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EmptyQueueVisibility)));
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadQueueAsync();
    }

    private async Task LoadQueueAsync()
    {
        try
        {
            if (App.Services != null)
            {
                var downloadService = App.Services.GetRequiredService<IDownloadService>();
                var result = await downloadService.GetActiveDownloadsAsync();
                if (result.IsSuccess)
                {
                    QueuedItems.Clear();
                    foreach (var item in result.Value.Where(x => x.Status is CyberDownloadManager.Domain.Enums.DownloadStatus.Pending or CyberDownloadManager.Domain.Enums.DownloadStatus.Queued))
                    {
                        QueuedItems.Add(item);
                    }
                }
            }
        }
        catch { }
    }

    private async void StartQueue_Click(object sender, RoutedEventArgs e)
    {
        if (App.Services is null) return;
        var result = await App.Services.GetRequiredService<IQueueService>().ProcessNextAsync();
        if (result.IsFailure)
        {
            var dialog = new ContentDialog
            {
                Title = "Queue could not start",
                Content = result.Error.Description,
                CloseButtonText = "Close",
                XamlRoot = Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
        await LoadQueueAsync();
    }

    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.DataContext is DownloadItemDto item && App.Services != null)
            {
                var downloadService = App.Services.GetRequiredService<IDownloadService>();
                await downloadService.PauseDownloadAsync(item.Id);
                await LoadQueueAsync();
            }
        }
        catch { }
    }

    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is Button btn && btn.DataContext is DownloadItemDto item && App.Services != null)
            {
                var downloadService = App.Services.GetRequiredService<IDownloadService>();
                await downloadService.ResumeDownloadAsync(item.Id);
                await LoadQueueAsync();
            }
        }
        catch { }
    }
}
