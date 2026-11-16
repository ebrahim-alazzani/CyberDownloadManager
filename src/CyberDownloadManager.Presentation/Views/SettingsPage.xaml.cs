using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using CyberDownloadManager.Presentation.ViewModels;
using CyberDownloadManager.Presentation.Utilities;
using WinRT.Interop;

namespace CyberDownloadManager.Presentation.Views;

public partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        this.InitializeComponent();
        ViewModel = App.Services.GetRequiredService<SettingsViewModel>();
        DataContext = ViewModel;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
        ConnectionsComboBox.SelectedIndex = ViewModel.MaxConcurrentDownloads switch
        {
            4 => 0,
            8 => 1,
            12 => 2,
            16 => 3,
            32 => 4,
            _ => 3
        };
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var path = DownloadPathTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path) || !System.IO.Path.IsPathFullyQualified(path) || !System.IO.Directory.Exists(path))
        {
            await ShowMessageAsync("Choose an existing download folder using Browse, or enter a valid full folder path.", "Invalid download folder");
            return;
        }

        ViewModel.DownloadPath = System.IO.Path.GetFullPath(path);
        ViewModel.MaxConcurrentDownloads = ConnectionsComboBox.SelectedIndex switch
        {
            0 => 4,
            1 => 8,
            2 => 12,
            4 => 32,
            _ => 16
        };
        await ViewModel.SaveAsync();
        var dialog = new ContentDialog
        {
            Title = "Settings saved",
            Content = "Your download path and connection settings are saved.",
            CloseButtonText = "Close",
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private async void BrowseDownloadPath_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var folderPath = NativeFolderPicker.PickFolder(
                WindowNative.GetWindowHandle(App.MainWindow), "Select download folder");
            if (folderPath is not null)
                DownloadPathTextBox.Text = folderPath;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not open the settings folder picker");
            await ShowMessageAsync($"Could not open the folder picker (0x{ex.HResult:X8}): {ex.Message}", "Folder selection failed");
        }
    }

    private async Task ShowMessageAsync(string message, string title)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "Close",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
