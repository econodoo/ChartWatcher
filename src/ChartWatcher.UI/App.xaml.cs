using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Serilog;
using ChartWatcher.Application.Services;
using ChartWatcher.Application.ViewModels;
using ChartWatcher.Core.Components;
using ChartWatcher.Core.Sources;
using ChartWatcher.Core.Workspaces;
using ChartWatcher.Infrastructure.Persistence;
using ChartWatcher.Infrastructure.WebView;

namespace ChartWatcher.UI;

public partial class App : Microsoft.UI.Xaml.Application
{
    private Window? _shellWindow;

    public static IServiceProvider Services { get; private set; } = null!;
    public static ThemeService ThemeService => Services.GetRequiredService<ThemeService>();

    public App()
    {
        this.InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Configure Serilog
        var logFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChartWatcher", "logs");
        Directory.CreateDirectory(logFolder);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logFolder, "chartwatcher-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        Log.Information("=== ChartWatcher starting ===");

        // Initialize database
        var dbInit = new DatabaseInitializer();
        await dbInit.InitializeAsync();

        // Build DI container
        var services = new ServiceCollection();

        // Persistence
        services.AddSingleton(_ => dbInit);
        services.AddSingleton<ISourceRepository>(_ => new SqliteSourceRepository(dbInit.ConnectionString));
        services.AddSingleton<IComponentRepository>(_ => new SqliteComponentRepository(dbInit.ConnectionString));
        services.AddSingleton<IWorkspaceRepository>(_ => new SqliteWorkspaceRepository(dbInit.ConnectionString));

        // Services
        services.AddSingleton<ThemeService>();
        services.AddSingleton<SourceHub>();
        services.AddSingleton<ISourceHub>(sp => sp.GetRequiredService<SourceHub>());
        services.AddSingleton(Log.Logger);

        // ViewModels
        services.AddTransient<ShellViewModel>();

        Services = services.BuildServiceProvider();

        // Launch shell
        _shellWindow = new Windows.ShellWindow();
        _shellWindow.Activate();
    }
}
