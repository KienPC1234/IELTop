using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using IELTop.ViewModels;

namespace IELTop
{
    /// <summary>
    /// The main shell: sidebar, pages, and paper library. A running test
    /// lives in its own ExamWindow, so this window stays a calm setup shell.
    /// </summary>
    public partial class MainWindow : Window
    {
        private ExamWindow? _examWindow;

        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is not MainViewModel vm) return;
            vm.Exam.PropertyChanged += OnExamPropertyChanged;
            vm.Exam.ExamWindowCloseRequested += (_, _) => CloseExamWindow();
        }

        /// <summary>
        /// A running test opens the dedicated exam window. The window stays
        /// open after submit so the result and AI feedback show there, and
        /// closes when the student chooses Back to setup or Close.
        /// </summary>
        private void OnExamPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(ExamViewModel.IsRunning)) return;
            if (DataContext is not MainViewModel vm) return;
            if (vm.Exam.IsRunning)
                OpenExamWindow(vm);
        }

        private void OpenExamWindow(MainViewModel vm)
        {
            if (_examWindow is not null) return;
            try
            {
                _examWindow = new ExamWindow
                {
                    DataContext = vm,
                    Owner = this,
                    ApplyFullscreenOnStart = vm.Settings.FullscreenOnStart
                };
                _examWindow.Closed += (_, _) => _examWindow = null;
                _examWindow.Show();
                _examWindow.Activate();
            }
            catch (Exception)
            {
                // A window failure must not leave the app with a running test
                // and no visible window. Reset the test instead of crashing.
                _examWindow = null;
                vm.Exam.CancelRunningTest();
                System.Windows.MessageBox.Show(
                    "The test window could not open. The test was stopped. Try again.",
                    "IELTop", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void CloseExamWindow()
        {
            if (_examWindow is null) return;
            var window = _examWindow;
            _examWindow = null;
            window.Close();
        }

        /// <summary>
        /// Library drag and drop: paper titles drag, the basket drops.
        /// Pure presentation wiring, the view model holds the state.
        /// </summary>
        private void PaperDrag_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed
                && sender is System.Windows.Controls.TextBlock tb && tb.Text.Length > 0)
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
    }
}
