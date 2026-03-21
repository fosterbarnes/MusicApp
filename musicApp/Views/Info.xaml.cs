using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using MusicApp;
using MusicApp.Constants;
using MusicApp.Helpers;

namespace MusicApp.Views;

public partial class InfoMetadataView : Window
{
    private const int DwmWindowAttributeUseImmersiveDarkMode = 20;
    private const int DwmWindowAttributeUseImmersiveDarkModeBefore20 = 19;

    private Song? _track;

    public event EventHandler<Song>? ShowInSongsRequested;
    public event EventHandler<Song>? ShowInArtistsRequested;
    public event EventHandler<Song>? ShowInAlbumsRequested;

    public InfoMetadataView()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyDarkTitleBar();
        GenreComboBox.ItemsSource = Array.Empty<string>();
        PopulatePlaceholderValues();
        ShowSection("Details");
    }

    /// <summary>Same distinct+order rules as <see cref="ArtistGenreView"/> Genres sidebar, plus the current track genre if missing.</summary>
    private static List<string> BuildGenreList(IEnumerable<Song> libraryTracks, string? currentGenre)
    {
        var list = libraryTracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Genre))
            .Select(t => t.Genre)
            .Distinct()
            .ToList();

        if (!string.IsNullOrWhiteSpace(currentGenre) &&
            !list.Any(g => string.Equals(g, currentGenre, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(currentGenre);
        }

        return list.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void PopulatePlaceholderValues()
    {
        TopSongNameText.Text = "Take On Me";
        TopArtistNameText.Text = "a-ha";
        TopAlbumNameText.Text = "Hunting High and Low";

        SongTitleTextBox.Text = "Take On Me";
        ArtistTextBox.Text = "a-ha";
        AlbumTextBox.Text = "Hunting High and Low";
        PlayCountTextBox.Text = "0";
    }

    public void LoadTrack(Song track, IEnumerable<Song>? libraryTracks = null)
    {
        if (track == null)
        {
            return;
        }

        _track = track;

        var genres = BuildGenreList(libraryTracks ?? Array.Empty<Song>(), track.Genre);
        GenreComboBox.ItemsSource = genres;

        TopSongNameText.Text = string.IsNullOrWhiteSpace(track.Title) ? "Song Name" : track.Title;
        TopArtistNameText.Text = string.IsNullOrWhiteSpace(track.Artist) ? "Artist Name" : track.Artist;
        TopAlbumNameText.Text = string.IsNullOrWhiteSpace(track.Album) ? "Album Name" : track.Album;

        SongTitleTextBox.Text = track.Title ?? string.Empty;
        ArtistTextBox.Text = track.Artist ?? string.Empty;
        AlbumTextBox.Text = track.Album ?? string.Empty;
        AlbumArtistTextBox.Text = track.AlbumArtist ?? string.Empty;
        ComposerTextBox.Text = track.Composer ?? string.Empty;
        YearTextBox.Text = track.Year > 0 ? track.Year.ToString() : string.Empty;
        TrackNumberTextBox.Text = track.TrackNumber > 0 ? track.TrackNumber.ToString() : string.Empty;
        DiscNumberTextBox.Text = track.DiscNumber ?? string.Empty;
        BpmTextBox.Text = track.BeatsPerMinute > 0 ? track.BeatsPerMinute.ToString() : string.Empty;
        PlayCountTextBox.Text = track.PlayCount.ToString();

        if (!string.IsNullOrWhiteSpace(track.Genre))
        {
            string? match = null;
            foreach (var g in genres)
            {
                if (string.Equals(g, track.Genre, StringComparison.OrdinalIgnoreCase))
                {
                    match = g;
                    break;
                }
            }

            GenreComboBox.SelectedItem = match;
        }
        else
        {
            GenreComboBox.SelectedItem = null;
        }

        LoadAlbumArt(track);
    }

    private void LoadAlbumArt(Song track)
    {
        TopAlbumArtImage.Source = null;

        var helperImage = AlbumArtThumbnailHelper.LoadForTrack(track, UILayoutConstants.InfoMetadataAlbumArtSize);
        if (helperImage != null)
        {
            TopAlbumArtImage.Source = helperImage;
            return;
        }

        var albumArtPath = track.AlbumArtPath;
        if (string.IsNullOrWhiteSpace(albumArtPath) || !File.Exists(albumArtPath))
            return;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(albumArtPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            TopAlbumArtImage.Source = image;
        }
        catch
        {
            TopAlbumArtImage.Source = null;
        }
    }

    private void ApplyDarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        int enabled = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttributeUseImmersiveDarkMode, ref enabled, Marshal.SizeOf<int>());
        _ = DwmSetWindowAttribute(hwnd, DwmWindowAttributeUseImmersiveDarkModeBefore20, ref enabled, Marshal.SizeOf<int>());
    }

    [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    private void SectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string sectionName)
        {
            return;
        }

        ShowSection(sectionName);
    }

    private void ShowSection(string sectionName)
    {
        DetailsSectionPanel.Visibility = sectionName == "Details" ? Visibility.Visible : Visibility.Collapsed;
        ArtworkSectionPanel.Visibility = sectionName == "Artwork" ? Visibility.Visible : Visibility.Collapsed;
        LyricsSectionPanel.Visibility = sectionName == "Lyrics" ? Visibility.Visible : Visibility.Collapsed;
        OptionsSectionPanel.Visibility = sectionName == "Options" ? Visibility.Visible : Visibility.Collapsed;
        SortingSectionPanel.Visibility = sectionName == "Sorting" ? Visibility.Visible : Visibility.Collapsed;
        FileSectionPanel.Visibility = sectionName == "File" ? Visibility.Visible : Visibility.Collapsed;

        SetSectionButtonState(DetailsSectionButton, sectionName == "Details");
        SetSectionButtonState(ArtworkSectionButton, sectionName == "Artwork");
        SetSectionButtonState(LyricsSectionButton, sectionName == "Lyrics");
        SetSectionButtonState(OptionsSectionButton, sectionName == "Options");
        SetSectionButtonState(SortingSectionButton, sectionName == "Sorting");
        SetSectionButtonState(FileSectionButton, sectionName == "File");
    }

    private void SetSectionButtonState(Button button, bool isActive)
    {
        var key = isActive ? "SectionButtonActiveStyle" : "SectionButtonStyle";
        if (TryFindResource(key) is Style style)
            button.Style = style;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void TopSongNameText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_track == null)
            return;
        ShowInSongsRequested?.Invoke(this, _track);
        DialogResult = false;
    }

    private void TopArtistNameText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_track == null || string.IsNullOrWhiteSpace(_track.Artist))
            return;
        ShowInArtistsRequested?.Invoke(this, _track);
        DialogResult = false;
    }

    private void TopAlbumNameText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_track == null || string.IsNullOrWhiteSpace(_track.Album))
            return;
        ShowInAlbumsRequested?.Invoke(this, _track);
        DialogResult = false;
    }
}
