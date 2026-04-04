using RunBook.Workstation.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RunBook.Workstation
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm;

        public MainWindow()
        {
            InitializeComponent();
            _vm = new MainViewModel();
            DataContext = _vm;
        }

        private void ControlPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox box)
                _vm.SetControlPassword(box.Password);
        }

        private void Window_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_vm.HandlePasscodeKey(e.Key))
                e.Handled = true;
        }
    }
}
