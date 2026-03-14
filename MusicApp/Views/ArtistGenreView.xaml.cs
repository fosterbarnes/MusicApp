using System.Windows;
using System.Windows.Controls;

namespace MusicApp.Views
{
    public partial class ArtistGenreView : UserControl
    {
        public static readonly DependencyProperty ViewNameProperty = DependencyProperty.Register(
            nameof(ViewName), typeof(string), typeof(ArtistGenreView),
            new PropertyMetadata("Artists", OnViewNameChanged));

        public string ViewName
        {
            get => (string)GetValue(ViewNameProperty);
            set => SetValue(ViewNameProperty, value);
        }

        public ArtistGenreView()
        {
            InitializeComponent();
            trackList.ViewName = ViewName;
            Loaded += (_, _) => UpdatePlaceholderVisibility();
            trackList.AddToPlaylistRequested += (s, track) => AddToPlaylistRequested?.Invoke(this, track);
            trackList.AddTrackToPlaylistRequested += (s, args) => AddTrackToPlaylistRequested?.Invoke(this, args);
            trackList.CreateNewPlaylistWithTrackRequested += (s, track) => CreateNewPlaylistWithTrackRequested?.Invoke(this, track);
            trackList.PlayNextRequested += (s, track) => PlayNextRequested?.Invoke(this, track);
            trackList.AddToQueueRequested += (s, track) => AddToQueueRequested?.Invoke(this, track);
            trackList.InfoRequested += (s, track) => InfoRequested?.Invoke(this, track);
            trackList.ShowInExplorerRequested += (s, track) => ShowInExplorerRequested?.Invoke(this, track);
            trackList.RemoveFromLibraryRequested += (s, track) => RemoveFromLibraryRequested?.Invoke(this, track);
            trackList.DeleteRequested += (s, track) => DeleteRequested?.Invoke(this, track);
        }

        public System.Collections.IEnumerable? ItemsSource
        {
            get => trackList.ItemsSource;
            set => trackList.ItemsSource = value;
        }

        public event System.EventHandler<Song>? PlayTrackRequested;

        public event System.EventHandler<Song>? AddToPlaylistRequested;
        public event System.EventHandler<(Song track, Playlist playlist)>? AddTrackToPlaylistRequested;
        public event System.EventHandler<Song>? CreateNewPlaylistWithTrackRequested;
        public event System.EventHandler<Song>? PlayNextRequested;
        public event System.EventHandler<Song>? AddToQueueRequested;
        public event System.EventHandler<Song>? InfoRequested;
        public event System.EventHandler<Song>? ShowInExplorerRequested;
        public event System.EventHandler<Song>? RemoveFromLibraryRequested;
        public event System.EventHandler<Song>? DeleteRequested;

        public void RebuildColumns() => trackList.RebuildColumns();

        private static void OnViewNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ArtistGenreView view && e.NewValue is string name)
            {
                view.trackList.ViewName = name;
                view.UpdatePlaceholderVisibility();
            }
        }

        private void UpdatePlaceholderVisibility()
        {
            bool isArtists = string.Equals(ViewName, "Artists", StringComparison.OrdinalIgnoreCase);
            if (placeholderArtists != null)
                placeholderArtists.Visibility = isArtists ? Visibility.Visible : Visibility.Collapsed;
            if (placeholderGenres != null)
                placeholderGenres.Visibility = isArtists ? Visibility.Collapsed : Visibility.Visible;
        }

        private void TrackList_PlayTrackRequested(object? sender, Song e)
        {
            PlayTrackRequested?.Invoke(this, e);
        }
    }
}
