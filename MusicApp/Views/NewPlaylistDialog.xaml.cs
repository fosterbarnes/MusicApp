using System.Windows;
using System.Windows.Input;

namespace MusicApp.Views
{
    public partial class NewPlaylistDialog : Window
    {
        public NewPlaylistDialog()
        {
            InitializeComponent();
            TxtName.SelectAll();
            TxtName.Focus();
        }

        public string? PlaylistName { get; private set; }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            PlaylistName = TxtName.Text?.Trim();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TxtName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OkButton_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}
