using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using CyberDownloadManager.Application.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CyberDownloadManager.Presentation.Views;

public partial class AnalyticsPage : Page
{
    public AnalyticsViewModel ViewModel { get; } = new();

    public AnalyticsPage()
    {
        this.InitializeComponent();
        this.DataContext = ViewModel;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAnalyticsAsync();
    }
}

public partial class AnalyticsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _totalTransferredText = "0 MB";

    [ObservableProperty]
    private string _totalDownloadsText = "0 Tasks";

    [ObservableProperty]
    private string _freeStorageText = "0 GB";

    [ObservableProperty]
    private int _videoPercentage;

    [ObservableProperty]
    private int _softwarePercentage;

    [ObservableProperty]
    private int _otherPercentage;

    public async Task LoadAnalyticsAsync()
    {
        if (App.Services != null)
        {
            try
            {
                var downloadService = App.Services.GetRequiredService<IDownloadService>();
                var result = await downloadService.GetCompletedDownloadsAsync();
                if (result.IsSuccess)
                {
                    var items = result.Value.ToList();
                    long totalBytes = items.Sum(x => x.FileSize);
                    TotalTransferredText = FormatSize(totalBytes);
                    TotalDownloadsText = $"{items.Count} Files Completed";
                }
            }
            catch { }
        }

        try
        {
            var drive = new DriveInfo("C");
            if (drive.IsReady)
            {
                FreeStorageText = $"{drive.AvailableFreeSpace / (1024 * 1024 * 1024)} GB Free";
            }
        }
        catch { }
    }

    private static string FormatSize(long bytes)
    {
        string[] suffix = { "B", "KB", "MB", "GB", "TB" };
        int i;
        double dblSByte = bytes;
        for (i = 0; i < suffix.Length && bytes >= 1024; i++, bytes /= 1024)
        {
            dblSByte = bytes / 1024.0;
        }
        return $"{dblSByte:0.##} {suffix[i]}";
    }
}
