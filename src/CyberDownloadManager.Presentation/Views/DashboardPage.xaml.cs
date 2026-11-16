using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using CyberDownloadManager.Application.DTOs;
using CyberDownloadManager.Application.Interfaces;
using CyberDownloadManager.Presentation.ViewModels;

namespace CyberDownloadManager.Presentation.Views;

public partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }
    private DispatcherTimer? _timer;

    public DashboardPage()
    {
        this.InitializeComponent();
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        this.DataContext = ViewModel;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadDownloadsAsync();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _timer.Tick += async (s, args) => { try { await ViewModel.LoadDownloadsAsync(); } catch { } };
        _timer.Start();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _timer?.Stop();
        _timer = null;
    }

    private async void BtnAddTask_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddDownloadDialog
        {
            XamlRoot = this.Content.XamlRoot
        };
        await dialog.ShowAsync();
        await ViewModel.LoadDownloadsAsync();
    }

    private async void BtnLogs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new DiagnosticsDialog
        {
            XamlRoot = this.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DownloadItemDto item && App.Services != null)
        {
            var downloadService = App.Services.GetRequiredService<IDownloadService>();
            await downloadService.PauseDownloadAsync(item.Id);
            await ViewModel.LoadDownloadsAsync();
        }
    }

    private async void Resume_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DownloadItemDto item && App.Services != null)
        {
            var downloadService = App.Services.GetRequiredService<IDownloadService>();
            await downloadService.ResumeDownloadAsync(item.Id);
            await ViewModel.LoadDownloadsAsync();
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DownloadItemDto item && App.Services != null)
        {
            var downloadService = App.Services.GetRequiredService<IDownloadService>();
            await downloadService.DeleteDownloadAsync(item.Id);
            await ViewModel.LoadDownloadsAsync();
        }
    }
}
