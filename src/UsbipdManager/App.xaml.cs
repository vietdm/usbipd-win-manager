using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using UsbipdManager.Core.Abstractions;
using UsbipdManager.Core.Logging;
using UsbipdManager.Core.Platform;
using UsbipdManager.Core.Processes;
using UsbipdManager.Core.Services;
using UsbipdManager.Core.Settings;
using UsbipdManager.Core.Usbipd;
using UsbipdManager.Core.Wsl;
using UsbipdManager.Interop;
using UsbipdManager.Services;
using UsbipdManager.Startup;
using UsbipdManager.Theme;
using UsbipdManager.Tray;
using UsbipdManager.ViewModels;
using UsbipdManager.Views;
using MainWindowView = UsbipdManager.Views.MainWindow;

namespace UsbipdManager;

/// <summary>
/// Composition root: command line (PLAN 6.1), single instance, object graph, theme, tray, windows, exit and session end.
/// </summary>
public partial class App : Application
{
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SessionEndTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan ExitCommandBudget = TimeSpan.FromSeconds(20);

    private readonly DialogService _dialogs = new();

    private ILogService? _log;
    private ISingleInstance? _singleInstance;
    private ISettingsStore? _settings;
    private WslClient? _wsl;
    private DeviceChangeNotifier? _notifier;
    private AppController? _controller;
    private IAppInfo? _appInfo;
    private IAutoStartManager? _autoStart;
    private IShortcutManager? _shortcuts;
    private ThemeManager? _theme;
    private MainViewModel? _mainViewModel;
    private MainWindowView? _mainWindow;
    private TrayIconService? _tray;
    private AboutWindow? _aboutWindow;
    private SettingsWindow? _settingsWindow;
    private bool _exiting;
    private bool _cleanedUp;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalExceptionHandlers();

        var options = CommandLineOptions.Parse(e.Args);
        if (options.IsHeadless)
        {
            var exitCode = await RunHeadlessAsync(options);
            Shutdown(exitCode);
            return;
        }

        StartInteractive(options);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        base.OnSessionEnding(e);
        if (e.Cancel || _exiting)
        {
            return;
        }

        // Logoff / shutdown: fast best-effort release, then let Windows end the process.
        _exiting = true;
        _log?.Info($"Windows session ending ({e.ReasonSessionEnding}): returning devices to Windows.");
        if (_controller is { } controller)
        {
            try
            {
                using var cts = new CancellationTokenSource(SessionEndTimeout);
                Task.Run(() => controller.ShutdownAsync(cts.Token)).Wait(SessionEndTimeout + TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                _log?.Warning($"Releasing devices at session end failed: {ex.GetBaseException().Message}");
            }
        }

        Cleanup();
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    // ===== Interactive start =====

    private void StartInteractive(CommandLineOptions options)
    {
        var singleInstance = new SingleInstance();
        bool isFirst;
        try
        {
            isFirst = singleInstance.TryAcquire();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Single instance check failed: {ex}");
            isFirst = true;
        }

        if (!isFirst)
        {
            // A second launch only activates the running instance's window.
            NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);
            singleInstance.SignalFirstInstance(InstanceSignal.Activate);
            singleInstance.Dispose();
            Shutdown(0);
            return;
        }

        _singleInstance = singleInstance;

        try
        {
            Compose(options);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Startup failed: {ex}");
            _log?.Error($"Startup failed: {ex}");
            MessageDialog.Show(null, "USBIPD Manager could not start", ex.Message, DialogKind.Error, "Close");
            Cleanup();
            Shutdown(1);
        }
    }

    private void Compose(CommandLineOptions options)
    {
        var log = new LogService();
        _log = log;
        foreach (var argument in options.UnknownArguments)
        {
            log.Warning($"Unknown command-line argument ignored: {argument}");
        }

        var settings = new SettingsStore(log);
        _settings = settings;
        var runner = new ProcessRunner(log);
        var usbipd = new UsbipdClient(runner, log);
        _wsl = new WslClient(runner, log);
        var service = new UsbipdServiceController(runner, log);
        var installer = new UsbipdInstaller(runner, log);
        var gate = new OperationGate();
        _notifier = new DeviceChangeNotifier(log);
        var checker = new EnvironmentChecker(usbipd, _wsl, service, log);
        var devices = new DeviceManager(usbipd, _wsl, settings, log);
        var modes = new ModeSwitcher(usbipd, _wsl, devices, settings, log);
        var init = new InitService(checker, installer, service, devices, log);
        _controller = new AppController(checker, init, devices, modes, _notifier, gate, settings, log);
        _appInfo = AppInfo.FromEntryAssembly();
        _autoStart = new AutoStartManager(runner, log, _appInfo.ExecutablePath);
        _shortcuts = new ShortcutManager(log, _appInfo.ExecutablePath);

        _theme = new ThemeManager(this);
        _theme.Apply(settings.Current.Theme);

        _mainViewModel = new MainViewModel(_controller, log, _appInfo, _dialogs, Dispatcher, OpenSettings);
        _mainWindow = new MainWindowView(_mainViewModel);
        MainWindow = _mainWindow;

        _tray = new TrayIconService(_mainViewModel, settings, log, _theme, Dispatcher, ShowMainWindow, ShowAbout, () => _ = ExitAsync());
        _mainWindow.HiddenToTray += (_, _) => _tray?.NotifyHiddenToTray();

        _singleInstance!.SignalReceived += OnInstanceSignal;

        log.Info($"{_appInfo.ProductName} {_appInfo.Version} started{(options.StartInTray ? " in the tray" : string.Empty)}.");
        if (!options.StartInTray)
        {
            ShowMainWindow();
        }

        var autoStart = _autoStart;
        var controller = _controller;
        RunInBackground("Start with Windows task check", () => autoStart.EnsurePathUpToDateAsync());
        RunInBackground("Startup", () => controller.StartAsync());
    }

    private void RunInBackground(string name, Func<Task> work) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                _log?.Error($"{name} failed: {ex.Message}");
            }
        });

    // ===== Windows =====

    private void ShowMainWindow()
    {
        if (_mainWindow is null || _exiting)
        {
            return;
        }

        if (!_mainWindow.IsVisible)
        {
            _mainWindow.Show();
        }

        if (_mainWindow.WindowState == WindowState.Minimized)
        {
            _mainWindow.WindowState = WindowState.Normal;
        }

        Window target = _settingsWindow is { IsVisible: true } ? _settingsWindow : _mainWindow;
        target.Activate();
        // Topmost toggle brings the window above others when activation alone is not enough.
        target.Topmost = true;
        target.Topmost = false;
        target.Focus();
    }

    private void OpenSettings()
    {
        if (_exiting || _settings is null || _autoStart is null || _shortcuts is null || _log is null || _controller is null || _theme is null)
        {
            return;
        }

        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var viewModel = new SettingsViewModel(_settings, _autoStart, _shortcuts, _log, _controller, _theme, _dialogs);
        _settingsWindow = new SettingsWindow(viewModel);
        if (_mainWindow is { IsVisible: true })
        {
            _settingsWindow.Owner = _mainWindow;
        }
        else
        {
            _settingsWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        try
        {
            _settingsWindow.ShowDialog();
        }
        finally
        {
            _settingsWindow = null;
        }
    }

    private void ShowAbout()
    {
        if (_exiting || _appInfo is null)
        {
            return;
        }

        if (_aboutWindow is not null)
        {
            _aboutWindow.Activate();
            return;
        }

        _aboutWindow = new AboutWindow(new AboutViewModel(_appInfo));
        if (_mainWindow is { IsVisible: true })
        {
            _aboutWindow.Owner = _mainWindow;
        }
        else
        {
            _aboutWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        _aboutWindow.Closed += (_, _) => _aboutWindow = null;
        _aboutWindow.Show();
        _aboutWindow.Activate();
    }

    // ===== Signals and exit =====

    private void OnInstanceSignal(object? sender, InstanceSignal signal)
    {
        // Raised on a background thread.
        Dispatcher.BeginInvoke(() =>
        {
            if (signal == InstanceSignal.Exit)
            {
                _log?.Info("Exit requested by another process.");
                _ = ExitAsync();
            }
            else
            {
                ShowMainWindow();
            }
        });
    }

    /// <summary>Tray Exit or the Exit signal: release devices (saved mode kept), dispose everything, quit.</summary>
    public async Task ExitAsync()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;
        _log?.Info("Exiting: returning all devices to Windows...");
        _aboutWindow?.Close();
        _settingsWindow?.Close();
        _mainViewModel?.SetExiting();
        _tray?.SetExiting();

        if (_controller is { } controller)
        {
            using var cts = new CancellationTokenSource(ExitTimeout);
            try
            {
                var shutdown = Task.Run(() => controller.ShutdownAsync(cts.Token));
                var finished = await Task.WhenAny(shutdown, Task.Delay(ExitTimeout + TimeSpan.FromSeconds(2)));
                if (finished == shutdown)
                {
                    await shutdown;
                }
                else
                {
                    _log?.Warning("Returning devices to Windows timed out; exiting anyway.");
                }
            }
            catch (OperationCanceledException)
            {
                _log?.Warning("Returning devices to Windows timed out; exiting anyway.");
            }
            catch (Exception ex)
            {
                _log?.Error($"Returning devices to Windows failed: {ex.Message}");
            }
        }

        Cleanup();
        Shutdown(0);
    }

    // Idempotent; runs on the UI thread (the single-instance mutex is owned by that thread).
    private void Cleanup()
    {
        if (_cleanedUp)
        {
            return;
        }

        _cleanedUp = true;
        if (_mainWindow is not null)
        {
            _mainWindow.AllowClose = true;
        }

        TryDispose(() => _tray?.Dispose());
        TryDispose(() => _mainViewModel?.Dispose());
        TryDispose(() => _controller?.Dispose());
        TryDispose(() =>
        {
            _notifier?.Stop();
            _notifier?.Dispose();
        });
        TryDispose(() => _wsl?.Dispose());
        TryDispose(() => _theme?.Dispose());
        TryDispose(() =>
        {
            if (_singleInstance is not null)
            {
                _singleInstance.SignalReceived -= OnInstanceSignal;
                _singleInstance.Dispose();
            }
        });
    }

    private void TryDispose(Action dispose)
    {
        try
        {
            dispose();
        }
        catch (Exception ex)
        {
            _log?.Warning($"Cleanup: {ex.Message}");
        }
    }

    // ===== Headless commands (no UI) =====

    private async Task<int> RunHeadlessAsync(CommandLineOptions options)
    {
        try
        {
            var log = new LogService();
            _log = log;
            switch (options.Command)
            {
                case StartupCommand.RegisterAutoStart:
                case StartupCommand.UnregisterAutoStart:
                {
                    var info = AppInfo.FromEntryAssembly();
                    var autoStart = new AutoStartManager(new ProcessRunner(log), log, info.ExecutablePath);
                    var register = options.Command == StartupCommand.RegisterAutoStart;
                    var result = register ? await autoStart.EnableAsync() : await autoStart.DisableAsync();
                    if (!result.Success)
                    {
                        log.Error($"{(register ? "--register-autostart" : "--unregister-autostart")} failed: {result.Error}");
                    }

                    return result.Success ? 0 : 1;
                }

                case StartupCommand.Exit:
                    return await ExitRunningInstanceAsync(log);

                default:
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Headless command failed: {ex}");
            _log?.Error($"Headless command failed: {ex.Message}");
            return 1;
        }
    }

    /// <summary><c>--exit</c>: ask the running instance to release devices and quit, wait up to 20 s for it to end.</summary>
    private static async Task<int> ExitRunningInstanceAsync(ILogService log)
    {
        var budget = Stopwatch.StartNew();
        var others = FindOtherInstances();

        using (var probe = new SingleInstance())
        {
            if (probe.TryAcquire())
            {
                log.Info("--exit: no running instance.");
                foreach (var process in others)
                {
                    process.Dispose();
                }

                return 0;
            }

            probe.SignalFirstInstance(InstanceSignal.Exit);
        }

        log.Info("--exit: asked the running instance to quit.");

        // Wait for the processes we know by name, then make sure the instance mutex is free
        // (covers a running copy with another file name, e.g. the portable exe).
        foreach (var process in others)
        {
            using (process)
            {
                var remaining = ExitCommandBudget - budget.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                try
                {
                    using var cts = new CancellationTokenSource(remaining);
                    await process.WaitForExitAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
                {
                }
            }
        }

        while (budget.Elapsed < ExitCommandBudget)
        {
            using (var probe = new SingleInstance())
            {
                if (probe.TryAcquire())
                {
                    log.Info("--exit: the running instance has ended.");
                    return 0;
                }
            }

            await Task.Delay(250);
        }

        log.Error("--exit: the running instance did not end within 20 seconds.");
        return 1;
    }

    private static List<Process> FindOtherInstances()
    {
        var currentId = Environment.ProcessId;
        var names = new[] { Process.GetCurrentProcess().ProcessName, "UsbipdManager" }.Distinct(StringComparer.OrdinalIgnoreCase);
        var result = new List<Process>();
        foreach (var name in names)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                if (process.Id == currentId || result.Any(p => p.Id == process.Id))
                {
                    process.Dispose();
                }
                else
                {
                    result.Add(process);
                }
            }
        }

        return result;
    }

    // ===== Global exception handlers =====

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            LogUnhandled("UI", e.Exception);
            e.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogUnhandled("Background task", e.Exception);
            e.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            LogUnhandled(e.IsTerminating ? "Fatal" : "Unhandled", e.ExceptionObject as Exception);
    }

    private void LogUnhandled(string source, Exception? exception)
    {
        try
        {
            var message = $"{source} error: {exception}";
            if (_log is not null)
            {
                _log.Error(message);
            }
            else
            {
                Trace.WriteLine(message);
            }
        }
        catch (Exception)
        {
            // Never throw from an exception handler.
        }
    }
}
