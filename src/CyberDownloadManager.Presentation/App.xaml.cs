using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CyberDownloadManager.Application.Interfaces;
using CyberDownloadManager.Application.Services;
using CyberDownloadManager.Domain.Interfaces;
using CyberDownloadManager.Infrastructure.Engine;
using CyberDownloadManager.Infrastructure.Logging;
using CyberDownloadManager.Infrastructure.Persistence;
using CyberDownloadManager.Infrastructure.Repositories;
using CyberDownloadManager.Presentation.ViewModels;
using CyberDownloadManager.Shared.Constants;
using Serilog;

namespace CyberDownloadManager.Presentation;

public partial class App : Microsoft.UI.Xaml.Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static Window MainWindow { get; private set; } = null!;
    private static bool IsDesignMode => Windows.ApplicationModel.DesignMode.DesignModeEnabled;

    public App()
    {
        this.InitializeComponent();

        this.UnhandledException += App_UnhandledException;

        // Catch background Task.Run exceptions to prevent crash
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Log.Error(e.Exception, "An unobserved background task exception occurred");
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            if (e.ExceptionObject is Exception exception)
                Log.Fatal(exception, "An unhandled application exception occurred");
        };

        if (!IsDesignMode)
        {
            Services = ConfigureServices();
        }
    }

    private void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "An unhandled WinUI exception occurred");
        e.Handled = true;
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Local storage path for db and logs
        string appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyberDownloadManager");
        Directory.CreateDirectory(appDataFolder);

        string dbPath = Path.Combine(appDataFolder, AppConstants.DatabaseFileName);
        string logPath = Path.Combine(appDataFolder, AppConstants.LogFileName);

        // Logger
        var logger = SerilogConfiguration.CreateLogger(logPath);
        Log.Logger = logger;
        services.AddSingleton(logger);

        // Db & Repositories
        var dbContext = new AppDbContext(dbPath);
        services.AddSingleton(dbContext);
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<IDownloadRepository, DownloadRepository>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();

        // Engine & Services
        services.AddSingleton<IDownloadEngine, DownloadEngineImpl>();
        services.AddSingleton<IVideoDownloadService, YtDlpService>();
        services.AddSingleton<IDownloadService, DownloadService>();
        services.AddSingleton<IQueueService, QueueService>();
        services.AddSingleton<ISchedulerService, SchedulerService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<INotificationService, NotificationService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ActiveTransfersViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services.BuildServiceProvider();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (IsDesignMode)
        {
            return;
        }

        // Show the application shell immediately so a slow database initialization
        // cannot leave the user with a running process and no visible window.
        MainWindow = new MainWindow();
        MainWindow.Activate();

        string? startupError = null;
        try
        {
            // Initialize SQLite Database
            var dbInitializer = Services.GetRequiredService<DatabaseInitializer>();
            await dbInitializer.InitializeAsync();
            await Services.GetRequiredService<IDownloadService>().RecoverInterruptedDownloadsAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not initialize the local database");
            startupError = ex.Message;
        }

        if (startupError is not null)
        {
            var dialog = new ContentDialog
            {
                Title = "Database initialization failed",
                Content = $"The application could not open its local database. Download history may not be available.\n\n{startupError}",
                CloseButtonText = "Close",
                XamlRoot = MainWindow.Content.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
