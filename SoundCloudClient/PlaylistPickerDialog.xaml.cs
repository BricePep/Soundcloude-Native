using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;

namespace SoundCloudClient
{
    public partial class PlaylistPickerDialog : Window
    {
        public string SelectedPlaylistName { get; private set; } = "";

        public PlaylistPickerDialog(List<Playlist> playlists)
        {
            InitializeComponent();
            foreach (var p in playlists)
                PlaylistList.Items.Add(p.Name);
            _playlists = playlists;
        }

        private readonly List<Playlist> _playlists;

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (PlaylistList.SelectedIndex < 0) return;
            SelectedPlaylistName = _playlists[PlaylistList.SelectedIndex].Name;
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
