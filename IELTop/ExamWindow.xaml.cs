using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using IELTop.Styles;
using IELTop.ViewModels;

namespace IELTop
{
    /// <summary>
    /// The dedicated exam window, separate from the main shell, so a running
    /// test gets a full screen IDP style layout with a big timer, a part tab
    /// bar, a question palette, and its own strict mode. All test state lives
    /// in the shared ExamViewModel, so nothing is duplicated here.
    /// </summary>
    public partial class ExamWindow : Window
    {
        private bool _strict;
        private bool _fullscreen;
        private WindowStyle _savedStyle;
        private WindowState _savedState;
        private bool _savedTopmost;
        private ResizeMode _savedResize;
        private readonly System.Windows.Threading.DispatcherTimer _focusTimer;

        /// <summary>The app full screen preference, read from Settings by the shell.</summary>
        public bool ApplyFullscreenOnStart { get; set; }

        public ExamWindow()
        {
            InitializeComponent();
            _focusTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1200)
            };
            _focusTimer.Tick += OnFocusTimerTick;
            Loaded += (_, _) => HookStrictMode();
            PreviewKeyDown += OnPreviewKeyDown;
            Deactivated += OnDeactivated;
            Activated += (_, _) => _focusTimer.Stop();
            Closing += OnClosing;
        }

        private ExamViewModel? Vm => (DataContext as MainViewModel)?.Exam;

        private void HookStrictMode()
        {
            if (Vm is null) return;
            Vm.PropertyChanged += OnExamPropertyChanged;
            Vm.FullscreenToggleRequested += (_, _) => ToggleFullscreenFromExam();
            Vm.StrictToggleRequested += (_, _) => ToggleStrictFromExam();
            // Strict mode wins. Otherwise the window can open full screen,
            // either because the app asks for it or because the test does.
            // Deferred, because WPF ignores a Maximized state set too early.
            if (Vm.StrictMode && Vm.IsRunning)
                RunAfterLoad(() => SetStrict(true));
            else if ((ApplyFullscreenOnStart || Vm.IsFullscreen) && Vm.IsRunning)
                RunAfterLoad(() => SetFullscreen(true));
        }

        private void RunAfterLoad(Action action) =>
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, action);

        private void OnExamPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ExamViewModel.StrictMode)
                || e.PropertyName == nameof(ExamViewModel.IsRunning))
            {
                SetStrict(Vm?.StrictMode == true && Vm?.IsRunning == true);
                // When the test ends, come back to a normal window so the
                // result screen is easy to read and close.
                if (Vm?.IsRunning == false)
                    SetFullscreen(false);
            }
        }

        /// <summary>
        /// Voluntary exam lock. Full screen and topmost, with risky keys
        /// swallowed. This cannot block Alt Tab at OS level, but it stops
        /// casual cheating and keeps honest students focused.
        /// </summary>
        private void SetStrict(bool on)
        {
            if (on == _strict) return;
            _strict = on;
            if (on)
            {
                // Entering strict from plain full screen must not leave the
                // plain full screen flag behind.
                _fullscreen = false;
                SaveWindowState();
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                Topmost = true;
                WindowState = WindowState.Maximized;
            }
            else
            {
                RestoreWindowState();
            }
            SyncFullscreenFlag();
        }

        /// <summary>Turns the strict lock off and on again, for the exam button.</summary>
        private void ToggleStrictFromExam()
        {
            SetStrict(!_strict);
            if (Vm is not null)
            {
                // Keep the setup checkbox and the badge in step with the lock,
                // and save the choice so the next test opens the same way.
                Vm.StrictMode = _strict;
                SaveFullscreenChoice();
                Vm.StatusMessage = _strict
                    ? "Strict mode is on. Full screen, stay on this test."
                    : "Strict mode is off. Full screen stays on until you leave it.";
            }
        }

        /// <summary>
        /// Writes the current full screen choice back to settings, so the
        /// toggle sticks for the next test without opening Settings.
        /// </summary>
        private void SaveFullscreenChoice()
        {
            if (DataContext is not MainViewModel vm) return;
            vm.Settings.FullscreenOnStart = _strict || _fullscreen;
        }

        /// <summary>
        /// Full screen without the strict lock. No topmost and no key blocking,
        /// so the student can leave whenever they want. Press Esc or the button
        /// again to return to a window.
        /// </summary>
        private void SetFullscreen(bool on)
        {
            if (on == _fullscreen) return;
            if (_strict && on) return;
            _fullscreen = on;
            if (on)
            {
                SaveWindowState();
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                Topmost = false;
                WindowState = WindowState.Maximized;
            }
            else
            {
                RestoreWindowState();
            }
            SyncFullscreenFlag();
        }

        private void ToggleFullscreenFromExam()
        {
            if (_strict) return;
            SetFullscreen(!_fullscreen);
            // Remember the choice so the next test opens the same way.
            SaveFullscreenChoice();
        }

        private void SaveWindowState()
        {
            // Only capture the restored state once, so leaving one mode and
            // entering another does not save a full screen state as normal.
            if (_strict || _fullscreen) return;
            _savedStyle = WindowStyle;
            _savedState = WindowState;
            _savedTopmost = Topmost;
            _savedResize = ResizeMode;
        }

        private void RestoreWindowState()
        {
            WindowStyle = _savedStyle;
            ResizeMode = _savedResize;
            Topmost = _savedTopmost;
            WindowState = _savedState == WindowState.Minimized
                ? WindowState.Normal : _savedState;
        }

        private void SyncFullscreenFlag()
        {
            if (Vm is not null)
                Vm.IsFullscreen = _strict || _fullscreen;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Esc leaves plain full screen. In strict mode Esc stays blocked
            // and the Strict button turns the lock off, so the exit is deliberate.
            if (e.Key == Key.Escape && _fullscreen && !_strict)
            {
                SetFullscreen(false);
                e.Handled = true;
                return;
            }

            if (!_strict) return;
            bool blocked =
                e.Key == Key.F11 ||
                e.Key == Key.F12 ||
                e.Key == Key.LWin || e.Key == Key.RWin ||
                e.Key == Key.Apps ||
                e.Key == Key.PrintScreen || e.Key == Key.Snapshot ||
                e.Key == Key.System ||
                (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Alt) != 0) ||
                (e.Key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) != 0) ||
                (e.Key == Key.Escape) ||
                (e.Key == Key.F10 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0) ||
                ((Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
                 (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && e.Key == Key.Escape) ||
                ((Keyboard.Modifiers & ModifierKeys.Windows) != 0 &&
                 (e.Key == Key.D || e.Key == Key.M || e.Key == Key.S)) ||
                ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0 &&
                 (e.Key == Key.Delete || e.Key == Key.Tab || e.Key == Key.Escape));
            if (blocked && Vm is not null)
            {
                e.Handled = true;
                Vm.StatusMessage = "Strict mode is on. Finish or submit the test first.";
            }
        }

        private void OnDeactivated(object? sender, EventArgs e)
        {
            // Count a focus loss only if the window stays away for a moment.
            // A submit dialog or a dropdown steals focus briefly and must not
            // be reported as leaving the test.
            if (!_strict) return;
            _focusTimer.Stop();
            _focusTimer.Start();
        }

        private void OnFocusTimerTick(object? sender, EventArgs e)
        {
            _focusTimer.Stop();
            if (IsActive || Vm is null) return;
            // Another window of this app on top (a confirm dialog, a dropdown)
            // is not the student leaving the test.
            bool ownWindowActive = System.Windows.Application.Current.Windows
                .OfType<Window>()
                .Any(w => !ReferenceEquals(w, this) && w.IsActive);
            if (!ownWindowActive)
                Vm.RegisterFocusLost();
        }

        /// <summary>Passage highlight. Presentation only, no test logic.</summary>
        private void HighlightButton_Click(object sender, RoutedEventArgs e)
        {
            var selection = PassageBox.Selection;
            if (!selection.IsEmpty)
                selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Yellow);
        }

        private void ClearHighlightButton_Click(object sender, RoutedEventArgs e)
        {
            if (Vm?.CurrentPart is not null)
                PassageBox.Document = RichText.Build(Vm.CurrentPart.Material);
        }

        /// <summary>
        /// Right click Notes. The selected passage text travels with the note,
        /// so a note stays attached to the sentence it came from.
        /// </summary>
        private void NotesMenu_Click(object sender, RoutedEventArgs e)
        {
            string selected = PassageBox.Selection.IsEmpty ? string.Empty : PassageBox.Selection.Text;
            if (DataContext is MainViewModel vm && vm.Exam.OpenNotesCommand.CanExecute(selected))
                vm.Exam.OpenNotesCommand.Execute(selected);
        }

        private void CloseNotes_Click(object sender, RoutedEventArgs e)
        {
            if (Vm?.CurrentPart is not null)
                Vm.CurrentPart.NotesOpen = false;
        }

        /// <summary>Drag and drop for matching questions: bank items drag, gaps drop.</summary>
        private void BankItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed
                && sender is System.Windows.Controls.TextBlock tb && tb.Text.Length > 0)
                DragDrop.DoDragDrop(tb, tb.Text, DragDropEffects.Copy);
        }

        private void Gap_Drop(object sender, DragEventArgs e)
        {
            if (sender is System.Windows.Controls.Border border
                && border.DataContext is MatchRowViewModel row
                && e.Data.GetDataPresent(DataFormats.StringFormat))
                row.Selected = (string)e.Data.GetData(DataFormats.StringFormat);
        }

        private void ClearRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button b
                && b.DataContext is MatchRowViewModel row)
                row.Selected = string.Empty;
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (Vm is not null && Vm.IsRunning)
            {
                var ask = MessageBox.Show(
                    _strict
                        ? "Strict mode is on and a test is running. Quit anyway? Your answers are lost."
                        : "A test is running. Close and lose your answers?",
                    "Quit test", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (ask != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                Vm.CancelRunningTest();
            }
            SetStrict(false);
            SetFullscreen(false);
            _focusTimer.Stop();
        }
    }
}
