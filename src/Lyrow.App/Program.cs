using Avalonia;
using System;
using System.Threading;
using Lyrow.Core.Services;

namespace Lyrow.App;

class Program
{
    private const string AppGuid = "Lyrow_SingleInstance_f84b12c9-94b1-4c4f-9e63-47a61d15c89f";
    private const string WakeEventName = "Lyrow_WakeEvent_f84b12c9-94b1-4c4f-9e63-47a61d15c89f";

    [STAThread]
    public static void Main(string[] args)
    {
        LocalDiagnosticLogger.LogInfo("Lyrow starting...");

        bool isFirstInstance;
        Mutex? mutex = null;
        EventWaitHandle? wakeEvent = null;

        try
        {
            mutex = new Mutex(true, AppGuid, out isFirstInstance);
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogWarning($"Mutex creation fallback: {ex.Message}");
            isFirstInstance = true;
        }

        if (!isFirstInstance)
        {
            LocalDiagnosticLogger.LogInfo("Existing instance detected. Signalling active window to restore and exiting secondary instance.");
            try
            {
                if (EventWaitHandle.TryOpenExisting(WakeEventName, out var existingWakeEvent))
                {
                    existingWakeEvent.Set();
                    existingWakeEvent.Dispose();
                }
            }
            catch (Exception ex)
            {
                LocalDiagnosticLogger.LogError("Failed to signal existing instance wake event", ex);
            }
            return;
        }

        try
        {
            wakeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeEventName);
            StartWakeListenerThread(wakeEvent);
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogWarning($"Wake event setup warning: {ex.Message}");
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LocalDiagnosticLogger.LogError("Fatal unhandled exception in Avalonia application lifecycle", ex);
            throw;
        }
        finally
        {
            wakeEvent?.Dispose();
            try { mutex?.ReleaseMutex(); } catch { }
            mutex?.Dispose();
            LocalDiagnosticLogger.LogInfo("Lyrow gracefully stopped.");
        }
    }

    private static void StartWakeListenerThread(EventWaitHandle wakeEvent)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    if (wakeEvent.WaitOne())
                    {
                        LocalDiagnosticLogger.LogInfo("Wake signal received from secondary launch. Restoring main window.");
                        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                        {
                            App.MainWindowInstance?.ShowAndActivate();
                        });
                    }
                }
                catch (ThreadAbortException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LocalDiagnosticLogger.LogError("Wake listener loop error", ex);
                    break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "LyrowSingleInstanceWakeListener"
        };
        thread.Start();
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
