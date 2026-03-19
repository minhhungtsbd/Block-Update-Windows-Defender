using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using BlockUpdateWindowsDefender.ViewModels;

namespace BlockUpdateWindowsDefender
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            _viewModel.PasswordFieldsResetRequested += OnPasswordFieldsResetRequested;
            DataContext = _viewModel;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await _viewModel.InitializeAsync();
            _ = Dispatcher.BeginInvoke(new System.Action(_viewModel.StartDeferredStartupTasks), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void RepositoryLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch
            {
            }
        }

        private void NewPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                _viewModel.NewWindowsPassword = passwordBox.Password;
            }
        }

        private void ConfirmPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                _viewModel.ConfirmWindowsPassword = passwordBox.Password;
            }
        }

        private void OnPasswordFieldsResetRequested()
        {
            Dispatcher.Invoke(() =>
            {
                if (NewPasswordBox != null)
                {
                    NewPasswordBox.Clear();
                }

                if (ConfirmPasswordBox != null)
                {
                    ConfirmPasswordBox.Clear();
                }
            });
        }
    }
}
