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
    private bool _isFullscreen;
    private readonly object _gate = new();

#if WINDOWS
    private Microsoft.UI.Windowing.AppWindow? _nativeAppWindow;
    private bool _closeApproved;
#endif

    /// <summary>
    /// Asked when the student closes the exam window by a route the host holds
    /// back, such as the title bar button or Alt+F4. Returns true when the close
    /// must be held back and the page asked instead: that is only while a test
    /// is on the clock. With nothing at risk the window closes as usual.
    /// </summary>
    public Func<bool>? CloseGuard { get; set; }

    public bool SupportsFullscreen => true;

    public event Action<ExamFocusEvent>? FocusChanged;

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

                DetachNativeCloseGuard();
#endif
            }

            _window = window;

            if (window is not null)
            {
                // A fresh window starts normal, so the remembered state of a
                // window that was closed cannot leak into this one.
                _alwaysOnTop = false;
                _isFullscreen = false;

                window.Deactivated += OnDeactivated;
                window.Activated += OnActivated;
                window.Destroying += OnDestroying;
                window.HandlerChanged += OnWindowHandlerChanged;
                HookNativeWindow(window);
            }
            else
            {
                _alwaysOnTop = false;
                _isFullscreen = false;
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
        if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window newWin) return;

        newWin.Activated -= OnNativeWindowActivated;
        newWin.Activated += OnNativeWindowActivated;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(newWin);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
        if (appWindow is null || ReferenceEquals(appWindow, _nativeAppWindow)) return;

        _nativeAppWindow = appWindow;
        appWindow.Closing += OnNativeWindowClosing;

        if (_alwaysOnTop)
        {
            SetAlwaysOnTop(true);
        }
#endif
    }

#if WINDOWS
    private void DetachNativeCloseGuard()
    {
        if (_nativeAppWindow is null) return;
        _nativeAppWindow.Closing -= OnNativeWindowClosing;
        _nativeAppWindow = null;
        _closeApproved = false;
    }

    private void OnNativeWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_closeApproved) return;

        // Only a test that is on the clock is worth holding the window for. A
        // finished or cancelled test has nothing left to lose, so the close goes
        // through and the page is never asked a question with no answer at risk.
        var guard = CloseGuard;
        if (guard is null || !guard())
        {
            return;
        }

        args.Cancel = true;
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Raised when a close was held back and the page has to ask the question.
    /// Only fires alongside a <see cref="CloseGuard"/> that returned true.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Lets the next close attempt through. Set once the student has answered
    /// the question the page asked, so the close is not held a second time.
    /// </summary>
    public void ApproveClose()
    {
#if WINDOWS
        _closeApproved = true;
#endif
    }
#endif

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

    /// <summary>
    /// Keeps the exam window above the other windows. The WinUI presenter owns
    /// this, so there is no need to call user32 from here.
    /// </summary>
    public void SetAlwaysOnTop(bool on)
    {
        lock (_gate) _alwaysOnTop = on;
        MainThread.BeginInvokeOnMainThread(() =>
        {
#if WINDOWS
            if (_window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window win) return;

            var appWindow = ResolveAppWindow(win);
            if (appWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlapped)
            {
                overlapped.IsAlwaysOnTop = on;
            }
#endif
        });
    }

    /// <summary>Swaps between the full screen and the normal window.</summary>
    public void ToggleFullscreen()
    {
        bool target;
        lock (_gate) target = !_isFullscreen;
        SetFullscreenState(target);
    }

    private void SetFullscreenState(bool fullscreen)
    {
        lock (_gate)
        {
            if (_isFullscreen == fullscreen) return;
            _isFullscreen = fullscreen;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
#if WINDOWS
            if (_window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window win) return;

            var appWindow = ResolveAppWindow(win);
            if (appWindow is null) return;

            if (fullscreen)
            {
                appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            }
            else
            {
                appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Default);
            }

            // Leaving the full screen presenter drops the window back to normal,
            // so the always on top setting has to be put back on.
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter overlapped)
            {
                overlapped.IsAlwaysOnTop = _alwaysOnTop;
            }
#endif
        });
    }

#if WINDOWS
    private static Microsoft.UI.Windowing.AppWindow? ResolveAppWindow(Microsoft.UI.Xaml.Window win)
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(win);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        return Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
    }
#endif
}
