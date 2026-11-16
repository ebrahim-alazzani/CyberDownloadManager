using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CyberDownloadManager.Shared.Constants;
using Windows.ApplicationModel.DataTransfer;

namespace CyberDownloadManager.Presentation.Views;

public partial class DiagnosticsDialog : ContentDialog
{
    private string _fullLogPath = string.Empty;

    public DiagnosticsDialog()
    {
        this.InitializeComponent();
        LoadLogs();
    }

    private void LoadLogs()
    {
        try
        {
            string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyberDownloadManager");
            var directoryInfo = new DirectoryInfo(appDataFolder);
            var logFiles = directoryInfo.GetFiles("*.log");

            if (logFiles.Length > 0)
            {
                var latestLog = logFiles.OrderByDescending(f => f.LastWriteTime).First();
                _fullLogPath = latestLog.FullName;
                LogPathText.Text = $"Log File: {_fullLogPath}";

                using var stream = new FileStream(latestLog.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                string text = reader.ReadToEnd();
                LogTextBox.Text = string.IsNullOrWhiteSpace(text) ? "Log file is currently empty." : text;
            }
            else
            {
                LogTextBox.Text = "No log files found in " + appDataFolder;
                LogPathText.Text = appDataFolder;
            }
        }
        catch (Exception ex)
        {
            LogTextBox.Text = $"Error loading logs: {ex.Message}";
        }
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LoadLogs();
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_fullLogPath))
        {
            var dp = new DataPackage();
            dp.SetText(_fullLogPath);
            Clipboard.SetContent(dp);
        }
    }
}
