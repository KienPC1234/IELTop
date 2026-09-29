using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using IELTop.Styles;
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
            Deactivated += (_, _) => (DataContext as MainViewModel)?.Exam.RegisterFocusLost();
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
                e.Key == Key.Apps ||
                e.Key == Key.PrintScreen || e.Key == Key.Snapshot ||
                (e.Key == Key.Tab && (Keyboard.Modifiers & ModifierKeys.Alt) != 0) ||
                (e.Key == Key.F4 && (Keyboard.Modifiers & ModifierKeys.Alt) != 0) ||
                (e.Key == Key.Escape) ||
                ((Keyboard.Modifiers & ModifierKeys.Control) != 0 &&
                 (Keyboard.Modifiers & ModifierKeys.Shift) != 0 && e.Key == Key.Escape) ||
                ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0 &&
                 (e.Key == Key.Delete || e.Key == Key.Tab || e.Key == Key.Escape));
            if (blocked)
            {
                e.Handled = true;
                if (DataContext is MainViewModel vm)
                    vm.Exam.StatusMessage = "Strict mode is on. Finish or submit the test first.";
            }
        }

        /// <summary>
        /// Passage highlight, like the real test. Read only code behind:
        /// pure presentation, no test logic.
        /// </summary>
        private void HighlightButton_Click(object sender, RoutedEventArgs e)
        {
            var selection = PassageBox.Selection;
            if (!selection.IsEmpty)
                selection.ApplyPropertyValue(
                    TextElement.BackgroundProperty, Brushes.Yellow);
        }

        private void ClearHighlightButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.Exam.CurrentPart is not null)
                PassageBox.Document = RichText.Build(vm.Exam.CurrentPart.Material);
        }

        /// <summary>
        /// Drag and drop for matching questions: bank items drag, gaps drop.
        /// Pure presentation wiring, answers live in the row view models.
        /// </summary>
        private void BankItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed
                && sender is TextBlock tb && tb.Text.Length > 0)
                DragDrop.DoDragDrop(tb, tb.Text, DragDropEffects.Copy);
        }

        private void Gap_Drop(object sender, DragEventArgs e)
        {
            if (sender is Border border
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

        /// <summary>
        /// Library drag and drop: paper titles drag, the basket drops.
        /// </summary>
        private void PaperDrag_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed
                && sender is TextBlock tb && tb.Text.Length > 0)
                DragDrop.DoDragDrop(tb, tb.Text, DragDropEffects.Copy);
        }

        private void Basket_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.StringFormat)
                && DataContext is MainViewModel vm)
            {
                string title = (string)e.Data.GetData(DataFormats.StringFormat);
                if (vm.Library.AddToBasketCommand.CanExecute(title))
                    vm.Library.AddToBasketCommand.Execute(title);
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
