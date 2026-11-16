using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using System.IO;
using CyberDownloadManager.Application.DTOs;
using CyberDownloadManager.Application.Interfaces;

namespace CyberDownloadManager.Presentation.Views;

public partial class CompletedPage : Page
{
    public ObservableCollection<DownloadItemDto> CompletedDownloads { get; } = new();

    public CompletedPage()
    {
        this.InitializeComponent();
        this.DataContext = this;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadCompletedAsync();
    }

    private async Task LoadCompletedAsync()
    {
        try
        {
            if (App.Services != null)
            {
                var downloadService = App.Services.GetRequiredService<IDownloadService>();
                var result = await downloadService.GetCompletedDownloadsAsync();
                if (result.IsSuccess)
                {
                    CompletedDownloads.Clear();
                    foreach (var item in result.Value)
                    {
                        CompletedDownloads.Add(item);
                    }
                    EmptyStatePanel.Visibility = CompletedDownloads.Count == 0
                        ? Microsoft.UI.Xaml.Visibility.Visible
                        : Microsoft.UI.Xaml.Visibility.Collapsed;
                }
            }
        }
        catch { }
    }

    private void OpenFolder_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DownloadItemDto item && !string.IsNullOrEmpty(item.FilePath))
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
            }
            catch { }
        }
    }

    private async void Rename_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not DownloadItemDto item)
            return;

        var fileNameBox = new TextBox
        {
            Header = "File name (without extension)",
            Text = Path.GetFileNameWithoutExtension(item.FileName),
            MinWidth = 320
        };
        var dialog = new ContentDialog
        {
            Title = "Rename downloaded file",
            Content = fileNameBox,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        var service = App.Services.GetRequiredService<IDownloadService>();
        var result = await service.RenameCompletedDownloadAsync(item.Id, fileNameBox.Text);
        if (result.IsFailure)
        {
            await ShowMessageAsync("Rename failed", result.Error.Description);
            return;
        }

        await LoadCompletedAsync();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = XamlRoot
        };
        await dialog.ShowAsync();
    }
}
