using CyberDownloadManager.Presentation.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace CyberDownloadManager.Presentation;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        this.InitializeComponent();

        // Set Custom Title Bar
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(AppTitleBar);

        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(Views.SettingsPage));
            return;
        }

        if (args.SelectedItem is NavigationViewItem item)
        {
            string tag = item.Tag?.ToString() ?? string.Empty;
            switch (tag)
            {
                case "Dashboard":
                    ContentFrame.Navigate(typeof(Views.DashboardPage));
                    break;
                case "ActiveTransfers":
                    ContentFrame.Navigate(typeof(Views.ActiveTransfersPage));
                    break;
                case "Completed":
                    ContentFrame.Navigate(typeof(Views.CompletedPage));
                    break;
                case "Queue":
                    ContentFrame.Navigate(typeof(Views.QueuePage));
                    break;
                case "Analytics":
                    ContentFrame.Navigate(typeof(Views.AnalyticsPage));
                    break;
            }
        }
    }

    private async void BtnNewDownload_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddDownloadDialog
        {
            XamlRoot = this.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
