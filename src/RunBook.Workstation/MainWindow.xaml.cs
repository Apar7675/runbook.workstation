using RunBook.Workstation.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RunBook.Workstation
{
    public partial class MainWindow : Window
    {
        private const double PointerActivityThresholdPixels = 2.0;
        private static readonly TimeSpan PointerActivityThrottle = TimeSpan.FromMilliseconds(150);
        private readonly MainViewModel _vm;
        private Point? _lastPointerActivityPoint;
        private DateTime _lastPointerActivityUtc = DateTime.MinValue;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;
            _vm.PropertyChanged += ViewModel_OnPropertyChanged;
        }

        private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.SettingsControlPassword) && string.IsNullOrEmpty(_vm.SettingsControlPassword))
            {
                if (FindName("SupervisorPasswordBox") is PasswordBox supervisorPasswordBox)
                    supervisorPasswordBox.Password = "";
            }

            if (e.PropertyName == nameof(MainViewModel.ShowModulesShell))
                _vm.TraceSignedInShellVisible();
        }

        private void ControlPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox box)
                _vm.SetControlPassword(box.Password);
        }

        private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            _vm.NotifyUserActivity();
            if (_vm.HandlePasscodeKey(e.Key))
                e.Handled = true;
        }

        private void Window_OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            _vm.NotifyUserActivity();
        }

        private void Window_OnPreviewMouseMove(object sender, MouseEventArgs e)
        {
            var position = e.GetPosition(this);
            var nowUtc = DateTime.UtcNow;

            if (_lastPointerActivityPoint.HasValue)
            {
                var delta = position - _lastPointerActivityPoint.Value;
                var movedEnough = Math.Abs(delta.X) >= PointerActivityThresholdPixels || Math.Abs(delta.Y) >= PointerActivityThresholdPixels;
                var cooledDown = nowUtc - _lastPointerActivityUtc >= PointerActivityThrottle;
                if (!movedEnough || !cooledDown)
                    return;
            }

            _lastPointerActivityPoint = position;
            _lastPointerActivityUtc = nowUtc;
            _vm.NotifyUserActivity();
        }

        private void Window_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _lastPointerActivityPoint = e.GetPosition(this);
            _lastPointerActivityUtc = DateTime.UtcNow;
            _vm.NotifyUserActivity();
        }
    }
}
