using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CyberDownloadManager.Application.DTOs;
using CyberDownloadManager.Application.Interfaces;

namespace CyberDownloadManager.Presentation.Views;

public partial class ActiveTransfersPage : Page, INotifyPropertyChanged
{
    public ObservableCollection<DownloadItemDto> Transfers { get; } = new();
    private DispatcherTimer? _timer;
    public event PropertyChangedEventHandler? PropertyChanged;

    public Visibility EmptyStateVisibility => Transfers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public ActiveTransfersPage()
    {
        this.InitializeComponent();
        this.DataContext = this;
        Transfers.CollectionChanged += Transfers_CollectionChanged;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadTransfersAsync();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += async (s, args) => await LoadTransfersAsync();
        _timer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _timer?.Stop();
        _timer = null;
    }

    private async Task LoadTransfersAsync()
    {
        if (App.Services != null)
        {
            var downloadService = App.Services.GetRequiredService<IDownloadService>();
            var result = await downloadService.GetActiveDownloadsAsync();
            if (result.IsSuccess)
            {
                var fresh = result.Value.ToList();
                var ids = fresh.Select(x => x.Id).ToHashSet();
                for (var index = Transfers.Count - 1; index >= 0; index--)
                {
                    if (!ids.Contains(Transfers[index].Id)) Transfers.RemoveAt(index);
                }
                foreach (var item in fresh)
                {
                    var current = Transfers.FirstOrDefault(x => x.Id == item.Id);
                    if (current is null) Transfers.Add(item);
                    else
                    {
                        current.FileSize = item.FileSize;
                        current.DownloadedBytes = item.DownloadedBytes;
                        current.Status = item.Status;
                    }
                }
            }
        }
    }

    private void Transfers_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EmptyStateVisibility)));

    private async void PauseAll_Click(object sender, RoutedEventArgs e)
    {
        await App.Services.GetRequiredService<IDownloadService>().PauseAllAsync();
        await LoadTransfersAsync();
    }

    private async void ResumeAll_Click(object sender, RoutedEventArgs e)
    {
        await App.Services.GetRequiredService<IDownloadService>().ResumeAllAsync();
        await LoadTransfersAsync();
    }

    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DownloadItemDto item && App.Services != null)
        {
            var downloadService = App.Services.GetRequiredService<IDownloadService>();
            await downloadService.PauseDownloadAsync(item.Id);
            await LoadTransfersAsync();
        }
    }

    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DownloadItemDto item && App.Services != null)
        {
            var downloadService = App.Services.GetRequiredService<IDownloadService>();
            await downloadService.ResumeDownloadAsync(item.Id);
            await LoadTransfersAsync();
        }
    }

    private async void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DownloadItemDto item && App.Services != null)
        {
            var downloadService = App.Services.GetRequiredService<IDownloadService>();
            await downloadService.DeleteDownloadAsync(item.Id);
            await LoadTransfersAsync();
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DownloadItemDto item && App.Services != null)
        {
            var downloadService = App.Services.GetRequiredService<IDownloadService>();
            await downloadService.DeleteDownloadAsync(item.Id);
            await LoadTransfersAsync();
        }
    }
}
