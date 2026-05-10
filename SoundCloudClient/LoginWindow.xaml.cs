using System.Windows;
using System.Windows.Input;

namespace SoundCloudClient
{
    public partial class LoginWindow : Window
    {
        public string? OAuthToken { get; private set; }

        public LoginWindow()
        {
            InitializeComponent();
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TokenBox.Text))
            {
                MessageBox.Show("Введи OAuth токен");
                return;
            }

            OAuthToken = TokenBox.Text.Trim();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
    }
}
