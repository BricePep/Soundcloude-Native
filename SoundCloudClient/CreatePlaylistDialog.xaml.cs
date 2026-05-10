using System.Windows;
using System.Windows.Input;

namespace SoundCloudClient
{
    public partial class CreatePlaylistDialog : Window
    {
        public string PlaylistName { get; private set; } = "";

        public CreatePlaylistDialog()
        {
            InitializeComponent();
            PlaylistNameBox.Focus();
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(PlaylistNameBox.Text))
                return;

            PlaylistName = PlaylistNameBox.Text.Trim();
            DialogResult = true;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
