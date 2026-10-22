using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using PortDeck.Localization;
using PortDeck.Services;

namespace PortDeck;

public partial class App : Application
{
    private SingleInstanceService? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        var settings = new SettingsStore().Load();
        ApplyThemeResources(settings.Theme);
        LocalizationService.Instance.Initialize(settings.Language);
        Log.Info("PortDeck starting.");

        // Hidden diagnostic mode: PortDeck.exe --dump-ports <path>
        // Useful for smoke tests and for inspecting exactly what the scanner sees.
        var dumpIndex = Array.IndexOf(e.Args, "--dump-ports");
        if (dumpIndex >= 0)
        {
            var output = dumpIndex + 1 < e.Args.Length
                ? e.Args[dumpIndex + 1]
                : Path.Combine(Path.GetTempPath(), "portdeck-ports.json");

            try
            {
                var scanner = new PortScanner();
                var result = scanner.ScanAsync(new HashSet<int>()).GetAwaiter().GetResult();
                var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                });
                File.WriteAllText(output, json);
            }
            catch (Exception ex)
            {
                File.WriteAllText(output, JsonSerializer.Serialize(new { ok = false, error = ex.ToString() }));
            }

            Shutdown();
            return;
        }

        _singleInstance = new SingleInstanceService("PortDeck.SingleInstance", "PortDeck.Activate");
        if (!_singleInstance.IsFirstInstance)
        {
            _singleInstance.SignalExisting();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        _singleInstance.StartListening(() => Dispatcher.Invoke(() =>
        {
            if (MainWindow is MainWindow mainWindow)
                mainWindow.ActivateFromExternal();
        }));
    }

    private void ApplyThemeResources(string theme)
    {
        var themeName = string.Equals(theme, "light", StringComparison.OrdinalIgnoreCase)
            ? "Light"
            : "Dark";
        Resources.MergedDictionaries.Clear();
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"Styles/Colors.{themeName}.xaml", UriKind.Relative)
        });
        Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("Styles/Theme.xaml", UriKind.Relative)
        });
    }

    public void ChangeTheme(string theme)
    {
        ApplyThemeResources(theme);

        var oldWindow = MainWindow as MainWindow;
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        var newWindow = new MainWindow
        {
            Opacity = 0,
            ShowInTaskbar = false
        };
        MainWindow = newWindow;
        newWindow.Show();
        newWindow.UpdateLayout();

        newWindow.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Render,
            new Action(() =>
            {
                newWindow.Opacity = 1;
                newWindow.ShowInTaskbar = true;
                newWindow.Activate();

                newWindow.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() =>
                    {
                        oldWindow?.CloseForRebuild();
                        ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose;
                    }));
            }));
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unhandled dispatcher exception.", e.Exception);
        MessageBox.Show(
            L.Format("UnhandledErrorFormat", e.Exception.Message),
            "PortDeck",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
