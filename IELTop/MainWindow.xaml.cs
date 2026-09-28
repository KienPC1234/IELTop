using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using IELTop.ViewModels;

namespace IELTop
{
    public partial class MainWindow : Window
    {
        private bool _strict;
        private WindowStyle _savedStyle;
        private WindowState _savedState;
        private bool _savedTopmost;
        private ResizeMode _savedResize;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => HookStrictMode();
            PreviewKeyDown += OnPreviewKeyDown;
            Closing += OnClosing;
        }

        private void HookStrictMode()
        {
            if (DataContext is MainViewModel vm)
                vm.Exam.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ExamViewModel.StrictMode))
                        SetStrict(vm.Exam.StrictMode && vm.Exam.IsRunning);
                    if (e.PropertyName == nameof(ExamViewModel.IsRunning))
                        SetStrict(vm.Exam.StrictMode && vm.Exam.IsRunning);
                };
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
                _savedStyle = WindowStyle;
                _savedState = WindowState;
                _savedTopmost = Topmost;
                _savedResize = ResizeMode;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                Topmost = true;
                WindowState = WindowState.Maximized;
            }
            else
            {
                WindowStyle = _savedStyle;
                ResizeMode = _savedResize;
                Topmost = _savedTopmost;
                WindowState = _savedState == WindowState.Minimized
                    ? WindowState.Normal : _savedState;
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_strict) return;
            // Block common escape routes during a strict test.
            bool blocked =
                e.Key == Key.F11 ||
                e.Key == Key.F12 ||
                e.Key == Key.LWin || e.Key == Key.RWin ||
                e.Key == Key.PrintScreen ||
                (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Alt) != 0) ||
                (e.Key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) != 0) ||
                (e.Key == Key.Escape) ||
                ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0 &&
                 (e.Key == Key.Delete || e.Key == Key.Tab || e.Key == Key.Escape));
            if (blocked)
            {
                e.Handled = true;
                if (DataContext is MainViewModel vm)
                    vm.Exam.StatusMessage = "Strict mode is on. Finish or submit the test first.";
            }
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {
            if (!_strict) return;
            if (DataContext is MainViewModel vm && vm.Exam.IsRunning)
            {
                var ask = MessageBox.Show(
                    "Strict mode is on and a test is running. Quit anyway?",
                    "Quit test", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (ask != MessageBoxResult.Yes)
                    e.Cancel = true;
                else
                    SetStrict(false);
            }
            else
            {
                SetStrict(false);
            }
        }
    }
}
