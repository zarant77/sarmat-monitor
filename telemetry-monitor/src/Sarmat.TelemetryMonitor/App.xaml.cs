using System.IO;
using System.Windows;
using Sarmat.TelemetryMonitor.Configuration;
using Sarmat.TelemetryMonitor.ViewModels;

namespace Sarmat.TelemetryMonitor;

public partial class App : Application
{
    private MainViewModel? viewModel;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var configuredPath = Environment.GetEnvironmentVariable("SARMAT_MONITOR_CONFIG");
            var workingPath = Path.Combine(Environment.CurrentDirectory, "config.json");
            var userDirectory = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData), "Sarmat", "TelemetryMonitor");
            var userPath = Path.Combine(userDirectory, "config.json");
            var appPath = Path.Combine(AppContext.BaseDirectory, "config.json");
            var examplePath = Path.Combine(AppContext.BaseDirectory, "config.example.json");
            var configPath = configuredPath ?? (File.Exists(workingPath) ? workingPath :
                File.Exists(userPath) ? userPath : appPath);
            if (configuredPath is null && !File.Exists(configPath) && File.Exists(examplePath))
            {
                Directory.CreateDirectory(userDirectory);
                File.Copy(examplePath, userPath);
                configPath = userPath;
            }
            var config = MonitorConfig.Load(configPath);
            viewModel = new MainViewModel(config);
            var window = new MainWindow { DataContext = viewModel };
            MainWindow = window;
            window.Show();
            viewModel.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Sarmat Telemetry Monitor", MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        viewModel?.Dispose();
        base.OnExit(e);
    }
}
