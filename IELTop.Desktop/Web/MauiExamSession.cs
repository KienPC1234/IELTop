using IELTop.Services.Exam;

namespace IELTop.Desktop.Web;

/// <summary>
/// Controls the native exam window under .NET MAUI / WinUI 3. Implements
/// IExamSessionController to enforce fullscreen, always-on-top, window focus,
/// and track window activation/deactivation without platform-specific code in Core.
/// </summary>
public sealed class MauiExamSession : IExamSessionController
{
    private Window? _window;
    private bool _alwaysOnTop;
    private Action<string>? _pushSink;
    private readonly Queue<string> _pendingPushes = new();
    private readonly object _gate = new();

    public bool SupportsFullscreen => true;

    public event Action<ExamFocusEvent>? FocusChanged;

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;
#endif

    /// <summary>Attaches to the active exam window or detaches when null.</summary>
    public void Attach(Window? window)
    {
        lock (_gate)
        {
            if (_window is not null)
            {
                _window.Deactivated -= OnDeactivated;
                _window.Activated -= OnActivated;
                _window.Destroying -= OnDestroying;
                _window.HandlerChanged -= OnWindowHandlerChanged;
#if WINDOWS
                if (_window.Handler?.PlatformView is Microsoft.UI.Xaml.Window oldWin)
                {
                    oldWin.Activated -= OnNativeWindowActivated;
                }
#endif
            }

            _window = window;

            if (window is not null)
            {
                window.Deactivated += OnDeactivated;
                window.Activated += OnActivated;
                window.Destroying += OnDestroying;
                window.HandlerChanged += OnWindowHandlerChanged;
                HookNativeWindow(window);
            }
            else
            {
                _alwaysOnTop = false;
                _pushSink = null;
                _pendingPushes.Clear();
            }
        }
    }

    private void OnWindowHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is Window win)
        {
            HookNativeWindow(win);
        }
    }

    private void HookNativeWindow(Window window)
    {
#if WINDOWS
        if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window newWin)
        {
            newWin.Activated -= OnNativeWindowActivated;
            newWin.Activated += OnNativeWindowActivated;
            if (_alwaysOnTop)
            {
                SetAlwaysOnTop(true);
            }
        }
#endif
    }

#if WINDOWS
    private void OnNativeWindowActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == Microsoft.UI.Xaml.WindowActivationState.Deactivated)
        {
            FocusChanged?.Invoke(ExamFocusEvent.Lost);
        }
        else
        {
            FocusChanged?.Invoke(ExamFocusEvent.Regained);
        }
    }
#endif

    public void ReportFocus(ExamFocusEvent evt)
    {
        FocusChanged?.Invoke(evt);
    }

    public void RegisterPushSink(Action<string>? pushSink)
    {
        lock (_gate)
        {
            _pushSink = pushSink;
            if (pushSink is not null)
            {
                while (_pendingPushes.Count > 0)
                {
                    pushSink(_pendingPushes.Dequeue());
                }
            }
        }
    }

    public void Push(string message)
    {
        lock (_gate)
        {
            if (_pushSink is not null)
            {
                _pushSink(message);
            }
            else
            {
                _pendingPushes.Enqueue(message);
            }
        }
    }

    private void OnDeactivated(object? sender, EventArgs e) => FocusChanged?.Invoke(ExamFocusEvent.Lost);

    private void OnActivated(object? sender, EventArgs e) => FocusChanged?.Invoke(ExamFocusEvent.Regained);

    private void OnDestroying(object? sender, EventArgs e) => Attach(null);

    public void EnterFullscreen() => SetFullscreenState(true);

    public void ExitFullscreen() => SetFullscreenState(false);

    public void Focus()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
#if WINDOWS
            if (_window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window win)
            {
                win.Activate();
            }
#endif
        });
    }

    public void SetAlwaysOnTop(bool on)
    {
        lock (_gate) _alwaysOnTop = on;
        MainThread.BeginInvokeOnMainThread(() =>
        {
#if WINDOWS
            if (_window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window win)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(win);
                SetWindowPos(hwnd, on ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                if (appWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlapped)
                {
                    overlapped.IsAlwaysOnTop = on;
                }
            }
#endif
        });
    }

    public void ToggleFullscreen(ref bool isFullscreen)
    {
        isFullscreen = !isFullscreen;
        SetFullscreenState(isFullscreen);
    }

    private void SetFullscreenState(bool fullscreen)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
#if WINDOWS
            if (_window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window win)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(win);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                if (appWindow is not null)
                {
                    if (fullscreen)
                    {
                        appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                    }
                    else
                    {
                        appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
                        if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlapped)
                        {
                            overlapped.IsAlwaysOnTop = _alwaysOnTop;
                        }
                    }

                    if (_alwaysOnTop)
                    {
                        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }
                }
            }
#endif
        });
    }
}
