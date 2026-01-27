using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Threading;
using NAudio.Wave;
using ATL;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace MusicApp
{
    public partial class MainWindow : Window
    {


        // ===========================================
        // WINDOWS API IMPORTS FOR RECYCLE BIN
        // ===========================================
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pTo;
            public ushort fFlags;
            [MarshalAs(UnmanagedType.Bool)]
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string lpszProgressTitle;
        }

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_SILENT = 0x0004;



        // ===========================================
        // WINDOW MANAGEMENT
        // ===========================================
        private WindowManager windowManager;

        // ===========================================
        // DATA COLLECTIONS
        // ===========================================
        private ObservableCollection<Song> allTracks = new ObservableCollection<Song>();
        private ObservableCollection<Song> filteredTracks = new ObservableCollection<Song>();
        private ObservableCollection<Song> shuffledTracks = new ObservableCollection<Song>();
        private ObservableCollection<Playlist> playlists = new ObservableCollection<Playlist>();
        private ObservableCollection<Song> recentlyPlayed = new ObservableCollection<Song>();

        // ===========================================
        // SORTING STATE
        // ===========================================
        private Dictionary<ListView, (string column, ListSortDirection direction)> sortStates = 
            new Dictionary<ListView, (string, ListSortDirection)>();
        private HashSet<GridViewColumnHeader> wiredHeaders = new HashSet<GridViewColumnHeader>();
        private HashSet<GridViewColumn> wiredColumnWidthHandlers = new HashSet<GridViewColumn>();
        private Dictionary<ListView, ContextMenu> listViewContextMenus = new Dictionary<ListView, ContextMenu>();

        // ===========================================
        // SETTINGS AND PERSISTENCE
        // ===========================================
        private LibraryManager libraryManager = LibraryManager.Instance;
        private SettingsManager settingsManager = SettingsManager.Instance;
        private SettingsManager.AppSettings appSettings = new SettingsManager.AppSettings();
        private DispatcherTimer? sidebarWidthSaveTimer;
        private bool isSidebarWidthDirty = false;
        private DispatcherTimer? columnWidthSaveTimer;
        private bool isColumnWidthDirty = false;
        private HashSet<string> dirtyColumnWidthViews = new HashSet<string>();
        private Dictionary<ListView, string> listViewToViewName = new Dictionary<ListView, string>();

        // ===========================================
        // AUDIO PLAYBACK STATE
        // ===========================================
        private WaveOutEvent? waveOut;
        private AudioFileReader? audioFileReader;
        private int currentTrackIndex = -1;
        private int currentShuffledIndex = -1;
        private Song? currentTrack;
        private bool isManuallyStopping = false; // Flag to prevent infinite loops during manual stops
        private bool isManualNavigation = false; // Flag to differentiate between natural progression and manual navigation

        // ===========================================
        // COLUMN MANAGEMENT
        // ===========================================
        private Dictionary<string, ColumnDefinition> columnDefinitions = new Dictionary<string, ColumnDefinition>();
        private Dictionary<string, List<string>> defaultVisibleColumns = new Dictionary<string, List<string>>();
        
        private class ColumnDefinition
        {
            public string DisplayName { get; set; } = "";
            public string PropertyName { get; set; } = "";
            public string SortPropertyName { get; set; } = "";
            public double DefaultWidth { get; set; } = 150;
            public IValueConverter? Converter { get; set; }
        }

        // ===========================================
        // CONSTRUCTOR AND INITIALIZATION
        // ===========================================
        public MainWindow()
        {
            InitializeComponent();

            // Initialize window manager
            windowManager = new WindowManager(this, titleBarPlayer);

            // Subscribe to window state changes for auto-saving
            windowManager.WindowStateChanged += WindowManager_WindowStateChanged;

            // Try to load settings synchronously to set the correct initial position
            try
            {
                var initialSettings = settingsManager.LoadSettingsSync();
                if (initialSettings?.WindowState != null)
                {
                    // Use saved window position if available
                    windowManager.SetInitialPosition(
                        initialSettings.WindowState.Left,
                        initialSettings.WindowState.Top,
                        initialSettings.WindowState.Width,
                        initialSettings.WindowState.Height
                    );

                    // Restore sidebar width if available
                    if (initialSettings.WindowState.SidebarWidth > 0)
                    {
                        sidebarColumn.Width = new GridLength(initialSettings.WindowState.SidebarWidth);
                    }

                    // Store the settings for later use
                    appSettings = initialSettings;
                    
                    // Columns will be built dynamically in SetupEventHandlers after InitializeColumnDefinitions
                }
                else
                {
                    // Fall back to default position
                    windowManager.SetInitialPosition(100, 100, 1200, 700);
                }
            }
            catch
            {
                // Fall back to default position if loading fails
                windowManager.SetInitialPosition(100, 100, 1200, 700);
            }

            // Initialize column definitions FIRST (before SetupEventHandlers which uses them)
            InitializeColumnDefinitions();

            SetupEventHandlers();

            // Load saved data asynchronously
            _ = LoadSavedDataAsync();

            // Initialize window state tracking
            windowManager.InitializeWindowState();

            // Setup sidebar width tracking
            SetupSidebarWidthTracking();
            SetupColumnWidthTracking();
        }

        /// <summary>
        /// Initializes column definitions for all available columns
        /// </summary>
        private void InitializeColumnDefinitions()
        {
            // Clear any existing definitions
            columnDefinitions.Clear();
            
            columnDefinitions["Title"] = new ColumnDefinition
            {
                DisplayName = "Title",
                PropertyName = "Title",
                SortPropertyName = "Title",
                DefaultWidth = 300
            };

            columnDefinitions["Album"] = new ColumnDefinition
            {
                DisplayName = "Album",
                PropertyName = "Album",
                SortPropertyName = "Album",
                DefaultWidth = 200
            };

            columnDefinitions["Album Artist"] = new ColumnDefinition
            {
                DisplayName = "Album Artist",
                PropertyName = "AlbumArtist",
                SortPropertyName = "AlbumArtist",
                DefaultWidth = 200
            };

            columnDefinitions["Artist"] = new ColumnDefinition
            {
                DisplayName = "Artist",
                PropertyName = "Artist",
                SortPropertyName = "Artist",
                DefaultWidth = 200
            };

            columnDefinitions["Beats Per Minute"] = new ColumnDefinition
            {
                DisplayName = "Beats Per Minute",
                PropertyName = "BeatsPerMinute",
                SortPropertyName = "BeatsPerMinute",
                DefaultWidth = 120,
                Converter = new Converters.IntConverter()
            };

            columnDefinitions["Bit Rate"] = new ColumnDefinition
            {
                DisplayName = "Bit Rate",
                PropertyName = "Bitrate",
                SortPropertyName = "Bitrate",
                DefaultWidth = 100
            };

            columnDefinitions["Category"] = new ColumnDefinition
            {
                DisplayName = "Category",
                PropertyName = "Category",
                SortPropertyName = "Category",
                DefaultWidth = 150
            };

            columnDefinitions["Composer"] = new ColumnDefinition
            {
                DisplayName = "Composer",
                PropertyName = "Composer",
                SortPropertyName = "Composer",
                DefaultWidth = 200
            };

            columnDefinitions["Date Added"] = new ColumnDefinition
            {
                DisplayName = "Date Added",
                PropertyName = "DateAdded",
                SortPropertyName = "DateAdded",
                DefaultWidth = 120,
                Converter = new Converters.DateConverter()
            };

            columnDefinitions["Date Modified"] = new ColumnDefinition
            {
                DisplayName = "Date Modified",
                PropertyName = "DateModified",
                SortPropertyName = "DateModified",
                DefaultWidth = 120,
                Converter = new Converters.DateConverter()
            };

            columnDefinitions["Disc Number"] = new ColumnDefinition
            {
                DisplayName = "Disc Number",
                PropertyName = "DiscNumber",
                SortPropertyName = "DiscNumber",
                DefaultWidth = 100
            };

            columnDefinitions["File Type"] = new ColumnDefinition
            {
                DisplayName = "File Type",
                PropertyName = "FileType",
                SortPropertyName = "FileType",
                DefaultWidth = 100
            };

            columnDefinitions["Genre"] = new ColumnDefinition
            {
                DisplayName = "Genre",
                PropertyName = "Genre",
                SortPropertyName = "Genre",
                DefaultWidth = 150
            };

            columnDefinitions["Last Played"] = new ColumnDefinition
            {
                DisplayName = "Last Played",
                PropertyName = "LastPlayed",
                SortPropertyName = "LastPlayed",
                DefaultWidth = 120,
                Converter = new Converters.DateConverter()
            };

            columnDefinitions["Plays"] = new ColumnDefinition
            {
                DisplayName = "Plays",
                PropertyName = "PlayCount",
                SortPropertyName = "PlayCount",
                DefaultWidth = 80,
                Converter = new Converters.IntConverter()
            };

            columnDefinitions["Release Date"] = new ColumnDefinition
            {
                DisplayName = "Release Date",
                PropertyName = "ReleaseDate",
                SortPropertyName = "ReleaseDate",
                DefaultWidth = 120,
                Converter = new Converters.NullableDateConverter()
            };

            columnDefinitions["Sample Rate"] = new ColumnDefinition
            {
                DisplayName = "Sample Rate",
                PropertyName = "SampleRate",
                SortPropertyName = "SampleRate",
                DefaultWidth = 120
            };

            columnDefinitions["Size"] = new ColumnDefinition
            {
                DisplayName = "Size",
                PropertyName = "FileSize",
                SortPropertyName = "FileSize",
                DefaultWidth = 100,
                Converter = new Converters.FileSizeConverter()
            };

            columnDefinitions["Time"] = new ColumnDefinition
            {
                DisplayName = "Time",
                PropertyName = "Duration",
                SortPropertyName = "DurationTimeSpan",
                DefaultWidth = 80
            };

            columnDefinitions["Track Number"] = new ColumnDefinition
            {
                DisplayName = "Track Number",
                PropertyName = "TrackNumber",
                SortPropertyName = "TrackNumber",
                DefaultWidth = 100,
                Converter = new Converters.IntConverter()
            };

            columnDefinitions["Year"] = new ColumnDefinition
            {
                DisplayName = "Year",
                PropertyName = "Year",
                SortPropertyName = "Year",
                DefaultWidth = 80,
                Converter = new Converters.IntConverter()
            };

            // Set default visible columns for each view
            defaultVisibleColumns["Songs"] = new List<string> { "Title", "Artist", "Album", "Time" };
            defaultVisibleColumns["Queue"] = new List<string> { "Title", "Artist", "Album", "Time" };
            defaultVisibleColumns["Artists"] = new List<string> { "Title", "Artist", "Album", "Time" };
            defaultVisibleColumns["Albums"] = new List<string> { "Title", "Artist", "Album", "Time" };
            defaultVisibleColumns["Genres"] = new List<string> { "Title", "Artist", "Album", "Time" };
            defaultVisibleColumns["Recently Played"] = new List<string> { "Title", "Artist", "Album", "Time" };
        }

        /// <summary>
        /// Gets the list of visible columns for a view, using saved preferences or defaults
        /// </summary>
        private List<string> GetVisibleColumns(string viewName)
        {
            if (appSettings?.WindowState?.ColumnVisibility != null &&
                appSettings.WindowState.ColumnVisibility.TryGetValue(viewName, out var savedColumns) &&
                savedColumns != null && savedColumns.Count > 0)
            {
                return savedColumns;
            }

            if (defaultVisibleColumns.TryGetValue(viewName, out var defaultColumns))
            {
                return defaultColumns;
            }

            return new List<string> { "Title", "Artist", "Album", "Time" };
        }

        /// <summary>
        /// Builds GridView columns dynamically based on visible columns configuration
        /// </summary>
        private void BuildGridViewColumns(ListView listView, string viewName)
        {
            try
            {
                if (listView == null) return;

                GridView gridView;
                if (listView.View is GridView existingGridView)
                {
                    gridView = existingGridView;
                }
                else
                {
                    gridView = new GridView();
                    listView.View = gridView;
                }

                // Clear existing columns
                gridView.Columns.Clear();

                // Get visible columns for this view
                var visibleColumns = GetVisibleColumns(viewName);
                
                if (visibleColumns == null || visibleColumns.Count == 0)
                {
                    // Fallback to defaults if no columns configured
                    visibleColumns = defaultVisibleColumns.TryGetValue(viewName, out var defaults) 
                        ? defaults 
                        : new List<string> { "Title", "Artist", "Album", "Time" };
                }

                // Get saved column widths for this view if available
                Dictionary<string, double> savedWidths;
                if (appSettings?.WindowState?.ColumnWidths != null && 
                    appSettings.WindowState.ColumnWidths.TryGetValue(viewName, out var viewWidths))
                {
                    savedWidths = viewWidths;
                }
                else
                {
                    // Fallback to SongsViewColumnWidths for backward compatibility
                    savedWidths = appSettings?.WindowState?.SongsViewColumnWidths ?? new Dictionary<string, double>();
                }

                // Build columns in the order they should appear
                foreach (var columnName in visibleColumns)
                {
                    if (columnDefinitions.TryGetValue(columnName, out var columnDef))
                    {
                        var column = new GridViewColumn
                        {
                            Header = columnDef.DisplayName,
                            Width = savedWidths.TryGetValue(columnDef.DisplayName, out var savedWidth) && savedWidth > 0
                                ? savedWidth
                                : columnDef.DefaultWidth
                        };

                        // Create binding
                        var binding = new Binding(columnDef.PropertyName);
                        if (columnDef.Converter != null)
                        {
                            binding.Converter = columnDef.Converter;
                        }

                        // Create cell template using FrameworkElementFactory (still supported in WPF)
                        var dataTemplate = new DataTemplate();
                        var textBlockFactory = new FrameworkElementFactory(typeof(TextBlock));
                        textBlockFactory.SetBinding(TextBlock.TextProperty, binding);
                        textBlockFactory.SetValue(TextBlock.MarginProperty, new Thickness(-9, 0, 0, 0));
                        textBlockFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
                        dataTemplate.VisualTree = textBlockFactory;
                        column.CellTemplate = dataTemplate;

                        gridView.Columns.Add(column);
                    }
                }

                // Apply column header style
                var headerStyle = this.FindResource("CompactGridViewHeaderStyle") as Style;
                if (headerStyle != null)
                {
                    gridView.ColumnHeaderContainerStyle = headerStyle;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error building columns for {viewName}: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Updates column visibility for a view and rebuilds the columns
        /// </summary>
        private async Task UpdateColumnVisibilityAsync(string viewName, List<string> visibleColumns)
        {
            if (appSettings?.WindowState == null)
            {
                appSettings = await settingsManager.LoadSettingsAsync();
            }

            if (appSettings.WindowState.ColumnVisibility == null)
            {
                appSettings.WindowState.ColumnVisibility = new Dictionary<string, List<string>>();
            }

            appSettings.WindowState.ColumnVisibility[viewName] = visibleColumns;
            await settingsManager.SaveSettingsAsync(appSettings);

            // Rebuild columns for the affected view
            ListView? targetListView = viewName switch
            {
                "Songs" => lstMusic,
                "Queue" => lstQueue,
                "Artists" => lstArtists,
                "Albums" => lstAlbums,
                "Genres" => lstGenres,
                "Recently Played" => lstRecentlyPlayed,
                _ => null
            };

            if (targetListView != null)
            {
                BuildGridViewColumns(targetListView, viewName);
                WireUpColumnHeaderSorting(targetListView);
                WireUpColumnResizeHandlers(targetListView);
                WireUpColumnContextMenu(targetListView, viewName);
            }
        }

        /// <summary>
        /// Wires up right-click context menu on grid view column headers
        /// </summary>
        private void WireUpColumnContextMenu(ListView listView, string viewName)
        {
            if (listView.View is not GridView gridView)
                return;

            // Wait for headers to be rendered
            listView.Loaded += (s, e) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    WireUpContextMenuForHeaders(listView, viewName);
                }), DispatcherPriority.Loaded);
            };

            listView.LayoutUpdated += (s, e) =>
            {
                WireUpContextMenuForHeaders(listView, viewName);
            };
        }

        /// <summary>
        /// Wires up context menu for all column headers in a ListView
        /// </summary>
        private void WireUpContextMenuForHeaders(ListView listView, string viewName)
        {
            var headers = FindVisualChildren<GridViewColumnHeader>(listView);
            
            // Get or create context menu for this ListView
            if (!listViewContextMenus.TryGetValue(listView, out var contextMenu))
            {
                contextMenu = CreateColumnContextMenu(listView, viewName);
                listViewContextMenus[listView] = contextMenu;
            }
            else
            {
                // Update existing context menu to reflect current state
                contextMenu = CreateColumnContextMenu(listView, viewName);
                listViewContextMenus[listView] = contextMenu;
            }
            
            foreach (var header in headers)
            {
                header.ContextMenu = contextMenu;
            }
        }

        /// <summary>
        /// Creates a context menu for column selection
        /// </summary>
        private ContextMenu CreateColumnContextMenu(ListView listView, string viewName)
        {
            var contextMenu = new ContextMenu();
            contextMenu.Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D));
            contextMenu.Foreground = new SolidColorBrush(Colors.White);

            // Add "Auto Size All Columns" menu item
            var autoSizeItem = new MenuItem
            {
                Header = "Auto Size All Columns",
                Foreground = new SolidColorBrush(Colors.White)
            };
            autoSizeItem.Click += (s, e) => AutoSizeAllColumns(listView);
            contextMenu.Items.Add(autoSizeItem);

            // Add separator
            contextMenu.Items.Add(new Separator());

            // Get current visible columns
            var visibleColumns = GetVisibleColumns(viewName);

            // Add menu items for each available column
            foreach (var columnDef in columnDefinitions.OrderBy(kvp => kvp.Key))
            {
                var columnName = columnDef.Key;
                var isVisible = visibleColumns.Contains(columnName);

                var menuItem = new MenuItem
                {
                    Header = columnDef.Value.DisplayName,
                    IsCheckable = true,
                    IsChecked = isVisible,
                    Foreground = new SolidColorBrush(Colors.White)
                };

                menuItem.Checked += (s, e) =>
                {
                    var currentVisible = GetVisibleColumns(viewName);
                    if (!currentVisible.Contains(columnName))
                    {
                        var updatedColumns = new List<string>(currentVisible) { columnName };
                        _ = UpdateColumnVisibilityAsync(viewName, updatedColumns);
                    }
                };

                menuItem.Unchecked += (s, e) =>
                {
                    var currentVisible = GetVisibleColumns(viewName);
                    // Don't allow unchecking if it's the last visible column
                    if (currentVisible.Count > 1)
                    {
                        var updatedColumns = new List<string>(currentVisible);
                        updatedColumns.Remove(columnName);
                        _ = UpdateColumnVisibilityAsync(viewName, updatedColumns);
                    }
                    else
                    {
                        // Re-check it if it's the last one
                        menuItem.IsChecked = true;
                    }
                };

                contextMenu.Items.Add(menuItem);
            }

            return contextMenu;
        }

        /// <summary>
        /// Auto-sizes all columns in a ListView
        /// </summary>
        private void AutoSizeAllColumns(ListView listView)
        {
            if (listView.View is GridView gridView)
            {
                foreach (var column in gridView.Columns)
                {
                    column.Width = double.NaN; // Auto size
                }
            }
        }



        /// <summary>
        /// Loads all saved data from settings files
        /// </summary>
        private async Task LoadSavedDataAsync()
        {
            try
            {
                // Load general settings (window state only) - do this first if not already loaded
                if (appSettings == null)
                {
                    appSettings = await settingsManager.LoadSettingsAsync();

                    // Restore window state immediately after loading settings
                    RestoreWindowState();
                }

                // Load library cache (tracks only)
                var libraryCache = await libraryManager.LoadLibraryCacheAsync();

                // Load library folders (music folders and scan times)
                var libraryFolders = await libraryManager.LoadLibraryFoldersAsync();

                // Load recently played
                var recentlyPlayedCache = await libraryManager.LoadRecentlyPlayedAsync();

                // Load playlists
                var playlistsCache = await libraryManager.LoadPlaylistsAsync();

                // Load music from saved folders
                await LoadMusicFromSavedFoldersAsync();

                // Restore playlists
                RestorePlaylists(playlistsCache);

                // Load sample data if no playlists exist
                LoadSampleData();

                // Restore recently played
                RestoreRecentlyPlayed(recentlyPlayedCache);

                // Update UI
                UpdateUI();

                // Initialize shuffled tracks if shuffle is enabled
                if (titleBarPlayer.IsShuffleEnabled)
                {
                    RegenerateShuffledTracks();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error loading saved data: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Restores window state from settings
        /// </summary>
        private void RestoreWindowState()
        {
            if (appSettings.WindowState != null)
            {
                windowManager.RestoreWindowState(
                    appSettings.WindowState.Width,
                    appSettings.WindowState.Height,
                    appSettings.WindowState.Left,
                    appSettings.WindowState.Top,
                    appSettings.WindowState.IsMaximized
                );

                // Restore sidebar width
                if (appSettings.WindowState.SidebarWidth > 0)
                {
                    sidebarColumn.Width = new GridLength(appSettings.WindowState.SidebarWidth);
                }

                // Restore column widths for songs view
                RestoreColumnWidths();
                
                // Rebuild columns with saved visibility settings
                RebuildAllColumnsWithSavedSettings();
            }
        }
        
        /// <summary>
        /// Rebuilds all columns with saved visibility and width settings
        /// </summary>
        private void RebuildAllColumnsWithSavedSettings()
        {
            BuildGridViewColumns(lstMusic, "Songs");
            BuildGridViewColumns(lstQueue, "Queue");
            BuildGridViewColumns(lstRecentlyPlayed, "Recently Played");
            BuildGridViewColumns(lstArtists, "Artists");
            BuildGridViewColumns(lstAlbums, "Albums");
            BuildGridViewColumns(lstGenres, "Genres");
            
            // Re-wire up handlers
            WireUpColumnHeaderSorting(lstMusic);
            WireUpColumnHeaderSorting(lstQueue);
            WireUpColumnHeaderSorting(lstRecentlyPlayed);
            WireUpColumnHeaderSorting(lstArtists);
            WireUpColumnHeaderSorting(lstAlbums);
            WireUpColumnHeaderSorting(lstGenres);
            WireUpColumnResizeHandlers(lstMusic);
            WireUpColumnContextMenu(lstMusic, "Songs");
            WireUpColumnContextMenu(lstQueue, "Queue");
            WireUpColumnContextMenu(lstRecentlyPlayed, "Recently Played");
            WireUpColumnContextMenu(lstArtists, "Artists");
            WireUpColumnContextMenu(lstAlbums, "Albums");
            WireUpColumnContextMenu(lstGenres, "Genres");
        }

        /// <summary>
        /// Restores column widths for the songs view from saved settings
        /// </summary>
        private void RestoreColumnWidths()
        {
            if (lstMusic.View is GridView gridView && appSettings.WindowState?.SongsViewColumnWidths != null)
            {
                foreach (var column in gridView.Columns)
                {
                    if (column.Header != null && appSettings.WindowState.SongsViewColumnWidths.TryGetValue(
                        column.Header.ToString() ?? "", out double savedWidth))
                    {
                        if (savedWidth > 0)
                        {
                            column.Width = savedWidth;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Loads music from previously saved folders
        /// </summary>
        private async Task LoadMusicFromSavedFoldersAsync()
        {
            var musicFolders = await libraryManager.GetMusicFoldersAsync();
            if (musicFolders == null || musicFolders.Count == 0)
                return;

            foreach (var folderPath in musicFolders)
            {
                if (Directory.Exists(folderPath))
                {
                    // Check if there are new files in the folder
                    bool hasNewFiles = await libraryManager.HasNewFilesInFolderAsync(folderPath);

                    if (hasNewFiles)
                    {
                        // Load new files from this folder
                        await LoadMusicFromFolderAsync(folderPath, true);
                    }
                    else
                    {
                        // Load from cache
                        await LoadMusicFromCacheAsync(folderPath);
                    }
                }
            }
        }

        /// <summary>
        /// Loads music from cache for a specific folder
        /// </summary>
        private async Task LoadMusicFromCacheAsync(string folderPath)
        {
            try
            {
                var libraryCache = await libraryManager.LoadLibraryCacheAsync();
                var cachedTracks = libraryCache.Tracks.Where(t => t.FilePath.StartsWith(folderPath)).ToList();

                foreach (var track in cachedTracks)
                {
                    // Verify file still exists
                    if (File.Exists(track.FilePath))
                    {
                        // Populate missing FileType if not set
                        if (string.IsNullOrEmpty(track.FileType))
                        {
                            var extension = Path.GetExtension(track.FilePath);
                            if (!string.IsNullOrEmpty(extension))
                            {
                                track.FileType = extension.TrimStart('.').ToUpper();
                            }
                        }
                        
                        // Populate missing Bitrate and SampleRate if not set
                        if (string.IsNullOrEmpty(track.Bitrate) || string.IsNullOrEmpty(track.SampleRate))
                        {
                            try
                            {
                                var atlTrack = new ATL.Track(track.FilePath);
                                if (string.IsNullOrEmpty(track.Bitrate) && atlTrack.Bitrate > 0)
                                {
                                    track.Bitrate = $"{atlTrack.Bitrate} kbps";
                                }
                                if (string.IsNullOrEmpty(track.SampleRate) && atlTrack.SampleRate > 0)
                                {
                                    track.SampleRate = $"{atlTrack.SampleRate / 1000.0:F1} kHz";
                                }
                            }
                            catch
                            {
                                // If ATL fails, will try to calculate below
                            }
                        }
                        
                        // Try NAudio fallback for sample rate if still not set
                        if (string.IsNullOrEmpty(track.SampleRate))
                        {
                            try
                            {
                                using var audioFile = new AudioFileReader(track.FilePath);
                                var sampleRate = audioFile.WaveFormat.SampleRate;
                                if (sampleRate > 0)
                                {
                                    track.SampleRate = $"{sampleRate / 1000.0:F1} kHz";
                                }
                            }
                            catch
                            {
                                // Ignore if NAudio fails
                            }
                        }
                        
                        // Restore DurationTimeSpan from Duration string (since it's not serialized)
                        if (track.DurationTimeSpan == TimeSpan.Zero && !string.IsNullOrEmpty(track.Duration))
                        {
                            try
                            {
                                // Parse Duration string (format: "mm:ss")
                                var parts = track.Duration.Split(':');
                                if (parts.Length == 2 && int.TryParse(parts[0], out int minutes) && int.TryParse(parts[1], out int seconds))
                                {
                                    track.DurationTimeSpan = new TimeSpan(0, minutes, seconds);
                                }
                            }
                            catch
                            {
                                // If parsing fails, try to get duration from file
                                try
                                {
                                    var atlTrack = new ATL.Track(track.FilePath);
                                    if (atlTrack.Duration > 0)
                                    {
                                        track.DurationTimeSpan = TimeSpan.FromSeconds(atlTrack.Duration);
                                    }
                                }
                                catch
                                {
                                    // If all else fails, leave it as zero
                                }
                            }
                        }
                        
                        // Calculate bitrate from file size and duration if still not set
                        if (string.IsNullOrEmpty(track.Bitrate) && track.FileSize > 0 && track.DurationTimeSpan.TotalSeconds > 0)
                        {
                            var bitrateKbps = (int)((track.FileSize * 8) / (track.DurationTimeSpan.TotalSeconds * 1000));
                            if (bitrateKbps > 0)
                            {
                                track.Bitrate = $"{bitrateKbps} kbps";
                            }
                        }

                        allTracks.Add(track);
                        filteredTracks.Add(track);
                    }
                }

                // Update shuffled tracks if shuffle is enabled
                Console.WriteLine($"After loading from cache - allTracks: {allTracks.Count}, filteredTracks: {filteredTracks.Count}");
                UpdateShuffledTracks();

                // Update queue view if it's currently visible
                if (queueView.Visibility == Visibility.Visible)
                {
                    UpdateQueueView();
                }

                // Update status bar after loading from cache
                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateStatusBar();
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading from cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores playlists from saved data
        /// </summary>
        private void RestorePlaylists(LibraryManager.PlaylistsCache playlistsCache)
        {
            if (playlistsCache.Playlists != null)
            {
                foreach (var playlist in playlistsCache.Playlists)
                {
                    // Reconstruct tracks for each playlist
                    playlist.ReconstructTracks(allTracks);
                    playlists.Add(playlist);
                }
            }
        }

        /// <summary>
        /// Restores recently played tracks
        /// </summary>
        private void RestoreRecentlyPlayed(LibraryManager.RecentlyPlayedCache recentlyPlayedCache)
        {
            if (recentlyPlayedCache.RecentlyPlayed != null)
            {
                foreach (var item in recentlyPlayedCache.RecentlyPlayed.OrderByDescending(x => x.LastPlayed).Take(20))
                {
                    var track = allTracks.FirstOrDefault(t => t.FilePath == item.FilePath);
                    if (track != null)
                    {
                        recentlyPlayed.Add(track);
                    }
                }
            }
        }

        /// <summary>
        /// Updates the UI after loading data
        /// </summary>
        private void UpdateUI()
        {
            // Refresh the music list
            lstMusic.ItemsSource = null;
            lstMusic.ItemsSource = filteredTracks;

            // Refresh playlists
            lstPlaylists.ItemsSource = null;
            lstPlaylists.ItemsSource = playlists;

            // Refresh recently played
            lstRecentlyPlayed.ItemsSource = null;
            lstRecentlyPlayed.ItemsSource = recentlyPlayed;

            // Refresh queue view if it's visible
            if (queueView.Visibility == Visibility.Visible)
            {
                UpdateQueueView();
            }
        }

        /// <summary>
        /// Updates the status bar with library statistics
        /// </summary>
        private void UpdateStatusBar()
        {
            try
            {
                if (statusBarText == null || allTracks == null || allTracks.Count == 0)
                {
                    if (statusBarText != null)
                    {
                        statusBarText.Text = "0 songs, 0 albums, 0.0 days, 0.00 GB";
                    }
                    return;
                }

                // Calculate total tracks
                var totalTracks = allTracks.Count;

                // Calculate unique albums
                var uniqueAlbums = allTracks
                    .Where(t => !string.IsNullOrWhiteSpace(t.Album) && t.Album != "Unknown Album")
                    .Select(t => new { t.Album, t.Artist })
                    .Distinct()
                    .Count();

                // Calculate total duration in days
                var totalDuration = allTracks
                    .Where(t => t.DurationTimeSpan != TimeSpan.Zero)
                    .Sum(t => t.DurationTimeSpan.TotalSeconds);
                var totalDays = totalDuration / (24.0 * 3600.0);

                // Calculate total file size in GB (use cached FileSize, avoid File.Exists checks)
                long totalBytes = 0;
                foreach (var track in allTracks)
                {
                    if (track.FileSize > 0)
                    {
                        totalBytes += track.FileSize;
                    }
                    // Only check file system if FileSize is not cached (should be rare after initial load)
                    else if (!string.IsNullOrEmpty(track.FilePath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(track.FilePath);
                            if (fileInfo.Exists)
                            {
                                totalBytes += fileInfo.Length;
                                track.FileSize = fileInfo.Length; // Cache it
                            }
                        }
                        catch
                        {
                            // Skip files that can't be accessed
                        }
                    }
                }
                var totalGB = totalBytes / (1024.0 * 1024.0 * 1024.0);

                // Update status bar text
                statusBarText.Text = $"{totalTracks} songs, {uniqueAlbums} albums, {totalDays:F1} days, {totalGB:F2} GB";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating status bar: {ex.Message}");
                if (statusBarText != null)
                {
                    statusBarText.Text = "Error calculating statistics";
                }
            }
        }

        /// <summary>
        /// Sets up data bindings and event handlers for UI controls
        /// </summary>
        private void SetupEventHandlers()
        {
            // Set the data context for the music list
            lstMusic.ItemsSource = filteredTracks;
            lstPlaylists.ItemsSource = playlists;
            lstRecentlyPlayed.ItemsSource = recentlyPlayed;

            // Initialize queue view
            lstQueue.ItemsSource = new ObservableCollection<Song>();

            // Initialize new views (will be populated with filtered data later)
            lstArtists.ItemsSource = new ObservableCollection<Song>();
            lstAlbums.ItemsSource = new ObservableCollection<Song>();
            lstGenres.ItemsSource = new ObservableCollection<Song>();

            // Map ListViews to view names for column width tracking
            listViewToViewName[lstMusic] = "Songs";
            listViewToViewName[lstQueue] = "Queue";
            listViewToViewName[lstRecentlyPlayed] = "Recently Played";
            listViewToViewName[lstArtists] = "Artists";
            listViewToViewName[lstAlbums] = "Albums";
            listViewToViewName[lstGenres] = "Genres";
            
            // Build columns dynamically for all ListViews after they're loaded
            lstMusic.Loaded += (s, e) => BuildGridViewColumns(lstMusic, "Songs");
            lstQueue.Loaded += (s, e) => BuildGridViewColumns(lstQueue, "Queue");
            lstRecentlyPlayed.Loaded += (s, e) => BuildGridViewColumns(lstRecentlyPlayed, "Recently Played");
            lstArtists.Loaded += (s, e) => BuildGridViewColumns(lstArtists, "Artists");
            lstAlbums.Loaded += (s, e) => BuildGridViewColumns(lstAlbums, "Albums");
            lstGenres.Loaded += (s, e) => BuildGridViewColumns(lstGenres, "Genres");
            
            // Also build immediately in case Loaded already fired
            BuildGridViewColumns(lstMusic, "Songs");
            BuildGridViewColumns(lstQueue, "Queue");
            BuildGridViewColumns(lstRecentlyPlayed, "Recently Played");
            BuildGridViewColumns(lstArtists, "Artists");
            BuildGridViewColumns(lstAlbums, "Albums");
            BuildGridViewColumns(lstGenres, "Genres");

            // Wire up column header sorting for all ListViews
            WireUpColumnHeaderSorting(lstMusic);
            WireUpColumnHeaderSorting(lstQueue);
            WireUpColumnHeaderSorting(lstRecentlyPlayed);
            WireUpColumnHeaderSorting(lstArtists);
            WireUpColumnHeaderSorting(lstAlbums);
            WireUpColumnHeaderSorting(lstGenres);

            // Wire up column resize handlers for songs view to enforce minimum width
            WireUpColumnResizeHandlers(lstMusic);

            // Wire up context menus for all ListViews
            WireUpColumnContextMenu(lstMusic, "Songs");
            WireUpColumnContextMenu(lstQueue, "Queue");
            WireUpColumnContextMenu(lstRecentlyPlayed, "Recently Played");
            WireUpColumnContextMenu(lstArtists, "Artists");
            WireUpColumnContextMenu(lstAlbums, "Albums");
            WireUpColumnContextMenu(lstGenres, "Genres");

            // Wire up title bar player control events
            titleBarPlayer.PlayPauseRequested += TitleBarPlayer_PlayPauseRequested;
            titleBarPlayer.PreviousTrackRequested += TitleBarPlayer_PreviousTrackRequested;
            titleBarPlayer.NextTrackRequested += TitleBarPlayer_NextTrackRequested;
            titleBarPlayer.WindowMinimizeRequested += TitleBarPlayer_WindowMinimizeRequested;
            titleBarPlayer.WindowMaximizeRequested += TitleBarPlayer_WindowMaximizeRequested;
            titleBarPlayer.WindowCloseRequested += TitleBarPlayer_WindowCloseRequested;

            // Wire up shuffle state change event
            titleBarPlayer.ShuffleStateChanged += TitleBarPlayer_ShuffleStateChanged;

            // Wire up window size changed event
            this.SizeChanged += MainWindow_SizeChanged;
        }

        /// <summary>
        /// Loads initial sample data for the application (only if no saved data exists)
        /// </summary>
        private void LoadSampleData()
        {
            // Only add sample playlists if no playlists exist
            if (playlists.Count == 0)
            {
                playlists.Add(new Playlist("Favorites", "My favorite songs"));
                playlists.Add(new Playlist("Workout Mix", "High energy songs for workouts"));
                playlists.Add(new Playlist("Chill Vibes", "Relaxing music"));
            }
        }

        #region Shuffle Management

        /// <summary>
        /// Regenerates the shuffled tracks collection and updates current track index
        /// This should only be called when shuffle is first enabled or when explicitly requested
        /// </summary>
        private void RegenerateShuffledTracks()
        {
            try
            {
                Console.WriteLine($"RegenerateShuffledTracks called - filteredTracks.Count: {filteredTracks.Count}");

                // Safety check
                if (filteredTracks == null || filteredTracks.Count == 0)
                {
                    Console.WriteLine("No filtered tracks to shuffle");
                    shuffledTracks.Clear();
                    currentShuffledIndex = -1;
                    return;
                }

                // Clear existing shuffled tracks
                shuffledTracks.Clear();

                // Add all filtered tracks to shuffled collection
                Console.WriteLine($"Processing {filteredTracks.Count} tracks from filteredTracks");
                foreach (var track in filteredTracks)
                {
                    if (track != null && !string.IsNullOrEmpty(track.FilePath))
                    {
                        shuffledTracks.Add(track);
                    }
                    else
                    {
                        Console.WriteLine($"Skipping null or invalid track in filteredTracks");
                    }
                }

                Console.WriteLine($"Added {shuffledTracks.Count} valid tracks to shuffled queue");
                Console.WriteLine($"Shuffle queue now contains: {string.Join(", ", shuffledTracks.Take(5).Select(t => t.Title))}... (and {shuffledTracks.Count - 5} more)");

                // Only shuffle if we have tracks
                if (shuffledTracks.Count > 1)
                {
                    // Shuffle the tracks using Fisher-Yates algorithm
                    var random = new Random();
                    for (int i = shuffledTracks.Count - 1; i > 0; i--)
                    {
                        int j = random.Next(i + 1);
                        var temp = shuffledTracks[i];
                        shuffledTracks[i] = shuffledTracks[j];
                        shuffledTracks[j] = temp;
                    }
                    Console.WriteLine("Shuffled tracks using Fisher-Yates algorithm");
                }
                else
                {
                    Console.WriteLine("Not enough tracks to shuffle (need at least 2)");
                }

                // If we have a current track, ensure it's at the beginning of the shuffled queue
                if (currentTrack != null)
                {
                    // Find the current track in the shuffled list
                    int trackIndex = shuffledTracks.IndexOf(currentTrack);
                    if (trackIndex > 0)
                    {
                        // Move the current track to the beginning
                        var trackToMove = shuffledTracks[trackIndex];
                        shuffledTracks.RemoveAt(trackIndex);
                        shuffledTracks.Insert(0, trackToMove);
                        Console.WriteLine($"Moved current track '{currentTrack.Title}' to beginning of shuffled queue");
                    }
                    else if (trackIndex == 0)
                    {
                        Console.WriteLine($"Current track '{currentTrack.Title}' is already at beginning of shuffled queue");
                    }
                    else
                    {
                        Console.WriteLine($"Current track '{currentTrack.Title}' not found in shuffled list, this shouldn't happen");
                    }

                    // Set the current shuffled index to 0 since the current track is now first
                    currentShuffledIndex = 0;
                }
                else
                {
                    currentShuffledIndex = -1;
                }

                Console.WriteLine($"Shuffled tracks generated - shuffledTracks.Count: {shuffledTracks.Count}, currentShuffledIndex: {currentShuffledIndex}");
                Console.WriteLine($"Shuffle queue order: {string.Join(" -> ", shuffledTracks.Take(5).Select(t => t.Title))}...");

                // Update queue view if it's currently visible
                if (queueView.Visibility == Visibility.Visible)
                {
                    UpdateQueueView();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RegenerateShuffledTracks: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                // Fallback: clear shuffled tracks and reset index
                try
                {
                    shuffledTracks.Clear();
                    currentShuffledIndex = -1;
                }
                catch (Exception clearEx)
                {
                    Console.WriteLine($"Error clearing shuffled tracks: {clearEx.Message}");
                }
            }
        }

        /// <summary>
        /// Ensures the shuffled tracks collection is properly initialized when shuffle is enabled
        /// This method maintains the existing shuffled order if possible, only regenerating when necessary
        /// </summary>
        private void EnsureShuffledTracksInitialized()
        {
            try
            {
                if (!titleBarPlayer.IsShuffleEnabled)
                {
                    return; // Shuffle not enabled, no need to initialize
                }

                Console.WriteLine($"EnsureShuffledTracksInitialized called - shuffledTracks.Count: {shuffledTracks.Count}, currentShuffledIndex: {currentShuffledIndex}");

                // If we don't have shuffled tracks or they don't match the current filtered tracks, regenerate
                if (shuffledTracks.Count == 0 || shuffledTracks.Count != filteredTracks.Count)
                {
                    Console.WriteLine("Shuffled tracks count mismatch or empty, regenerating");
                    RegenerateShuffledTracks();
                    return;
                }

                // If current track is not in shuffled tracks, regenerate
                if (currentTrack != null && shuffledTracks.IndexOf(currentTrack) == -1)
                {
                    Console.WriteLine("Current track not found in shuffled tracks, regenerating");
                    RegenerateShuffledTracks();
                    return;
                }

                // If we have a valid current track but no valid shuffled index, find it
                if (currentTrack != null && currentShuffledIndex == -1)
                {
                    currentShuffledIndex = shuffledTracks.IndexOf(currentTrack);
                    if (currentShuffledIndex == -1)
                    {
                        Console.WriteLine("Could not find current track in shuffled tracks, regenerating");
                        RegenerateShuffledTracks();
                    }
                    else
                    {
                        Console.WriteLine($"Found current track in shuffled tracks at index: {currentShuffledIndex}");
                    }
                }

                Console.WriteLine($"Shuffled tracks properly initialized - shuffledTracks.Count: {shuffledTracks.Count}, currentShuffledIndex: {currentShuffledIndex}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in EnsureShuffledTracksInitialized: {ex.Message}");
                // Fallback: regenerate shuffled tracks
                RegenerateShuffledTracks();
            }
        }

        /// <summary>
        /// Updates the shuffled tracks collection when filtered tracks change
        /// This should only be called when the library content changes, not during normal playback
        /// </summary>
        private void UpdateShuffledTracks()
        {
            Console.WriteLine($"UpdateShuffledTracks called - shuffle enabled: {titleBarPlayer.IsShuffleEnabled}, filteredTracks: {filteredTracks.Count}, shuffledTracks: {shuffledTracks.Count}");

            if (titleBarPlayer.IsShuffleEnabled)
            {
                // Only regenerate if the library content has actually changed
                if (shuffledTracks.Count != filteredTracks.Count)
                {
                    Console.WriteLine("Library content changed, regenerating shuffled tracks");
                    RegenerateShuffledTracks();
                }
                else
                {
                    Console.WriteLine("Library content unchanged, maintaining existing shuffled order");
                    EnsureShuffledTracksInitialized();
                }
            }
        }

        /// <summary>
        /// Gets the current play queue (either filtered or shuffled based on shuffle state)
        /// </summary>
        private ObservableCollection<Song> GetCurrentPlayQueue()
        {
            try
            {
                var queue = titleBarPlayer.IsShuffleEnabled ? shuffledTracks : filteredTracks;

                // Safety check - ensure we have a valid queue
                if (queue == null)
                {
                    Console.WriteLine("GetCurrentPlayQueue: queue is null, falling back to filteredTracks");
                    queue = filteredTracks;
                }

                Console.WriteLine($"GetCurrentPlayQueue - shuffle enabled: {titleBarPlayer.IsShuffleEnabled}, queue count: {queue?.Count ?? 0}, filteredTracks: {filteredTracks.Count}, shuffledTracks: {shuffledTracks.Count}");

                if (titleBarPlayer.IsShuffleEnabled && shuffledTracks.Count > 0)
                {
                    Console.WriteLine($"Shuffle queue sample: {string.Join(", ", shuffledTracks.Take(3).Select(t => t.Title))}...");
                }

                return queue ?? new ObservableCollection<Song>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCurrentPlayQueue: {ex.Message}");
                return filteredTracks ?? new ObservableCollection<Song>();
            }
        }

        /// <summary>
        /// Gets the current track index in the current play queue
        /// </summary>
        private int GetCurrentTrackIndex()
        {
            try
            {
                var index = titleBarPlayer.IsShuffleEnabled ? currentShuffledIndex : currentTrackIndex;
                Console.WriteLine($"GetCurrentTrackIndex - shuffle enabled: {titleBarPlayer.IsShuffleEnabled}, index: {index}");
                return index;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetCurrentTrackIndex: {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// Sets the current track index in the current play queue
        /// </summary>
        private void SetCurrentTrackIndex(int index)
        {
            if (titleBarPlayer.IsShuffleEnabled)
            {
                currentShuffledIndex = index;
            }
            else
            {
                currentTrackIndex = index;
            }
        }

        /// <summary>
        /// Safely gets a track from the current play queue at the specified index
        /// </summary>
        private Song? GetTrackFromCurrentQueue(int index)
        {
            try
            {
                var queue = GetCurrentPlayQueue();
                if (queue != null && index >= 0 && index < queue.Count)
                {
                    var track = queue[index];
                    if (track != null && !string.IsNullOrEmpty(track.FilePath))
                    {
                        return track;
                    }
                    else
                    {
                        Console.WriteLine($"Track at index {index} is null or has invalid file path");
                    }
                }
                else
                {
                    Console.WriteLine($"Invalid index {index} or queue is null/empty (count: {queue?.Count ?? 0})");
                }
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting track from queue at index {index}: {ex.Message}");
                return null;
            }
        }

        #endregion

        #region Title Bar Player Control Event Handlers

        private void TitleBarPlayer_PlayPauseRequested(object? sender, EventArgs e)
        {
            if (currentTrack == null)
            {
                if (filteredTracks.Count > 0)
                {
                    PlayTrack(filteredTracks[0]);
                }
                return;
            }

            if (titleBarPlayer.IsPlaying)
            {
                PausePlayback();
            }
            else
            {
                ResumePlayback();
            }
        }

        private void TitleBarPlayer_PreviousTrackRequested(object? sender, EventArgs e)
        {
            var currentQueue = GetCurrentPlayQueue();
            var currentIndex = GetCurrentTrackIndex();

            Console.WriteLine($"Previous track requested - currentIndex: {currentIndex}, queue count: {currentQueue.Count}");

            // Set manual navigation flag
            isManualNavigation = true;

            // Get current playback position
            var currentPosition = titleBarPlayer.CurrentPosition;
            Console.WriteLine($"Current playback position: {currentPosition.TotalSeconds:F1} seconds");

            // Store current playback state to preserve it
            bool wasPlaying = titleBarPlayer.IsPlaying;

            // If we're 3 or more seconds into the song, restart the current song
            if (currentPosition.TotalSeconds >= 3.0)
            {
                Console.WriteLine("Restarting current track (3+ seconds elapsed)");
                if (currentTrack != null)
                {
                    // Load the track without starting playback, then restore the previous state
                    LoadTrackWithoutPlayback(currentTrack);
                    // If it was playing before, start playback now
                    if (wasPlaying)
                    {
                        ResumePlayback();
                    }
                }
            }
            // If we're 2 seconds or less into the song, go to previous track
            else if (currentPosition.TotalSeconds <= 2.0 && currentIndex > 0)
            {
                Console.WriteLine("Going to previous track (2 seconds or less elapsed)");
                var previousTrack = GetTrackFromCurrentQueue(currentIndex - 1);
                if (previousTrack != null)
                {
                    // Load the previous track without starting playback, then restore the previous state
                    LoadTrackWithoutPlayback(previousTrack);
                    // If it was playing before, start playback now
                    if (wasPlaying)
                    {
                        ResumePlayback();
                    }
                }
                else
                {
                    Console.WriteLine("Previous track is null or invalid, cannot play");
                    // Update queue view if it's visible
                    if (queueView.Visibility == Visibility.Visible)
                    {
                        UpdateQueueView();
                    }
                }
            }
            // If we're between 2-3 seconds, restart the current song (edge case)
            else
            {
                Console.WriteLine("Restarting current track (between 2-3 seconds elapsed)");
                if (currentTrack != null)
                {
                    // Load the track without starting playback, then restore the previous state
                    LoadTrackWithoutPlayback(currentTrack);
                    // If it was playing before, start playback now
                    if (wasPlaying)
                    {
                        ResumePlayback();
                    }
                }
            }

            // Reset manual navigation flag after a short delay
            Task.Delay(100).ContinueWith(_ => isManualNavigation = false);
        }

        private void TitleBarPlayer_NextTrackRequested(object? sender, EventArgs e)
        {
            var currentQueue = GetCurrentPlayQueue();
            var currentIndex = GetCurrentTrackIndex();

            Console.WriteLine($"Next track requested - currentIndex: {currentIndex}, queue count: {currentQueue.Count}");

            // Set manual navigation flag
            isManualNavigation = true;

            if (currentIndex < currentQueue.Count - 1)
            {
                var nextTrack = GetTrackFromCurrentQueue(currentIndex + 1);
                if (nextTrack != null)
                {
                    // Store current playback state to preserve it
                    bool wasPlaying = titleBarPlayer.IsPlaying;

                    // Load the next track without starting playback, then restore the previous state
                    LoadTrackWithoutPlayback(nextTrack);
                    // If it was playing before, start playback now
                    if (wasPlaying)
                    {
                        ResumePlayback();
                    }
                }
                else
                {
                    Console.WriteLine("Next track is null or has invalid file path, stopping playback");
                    // Clean up and reset to idle state
                    CleanupAudioObjects();
                    currentTrack = null;
                    currentTrackIndex = -1;
                    currentShuffledIndex = -1;
                    titleBarPlayer.SetTrackInfo("No track selected", "", "");

                    // Update queue view if it's visible
                    if (queueView.Visibility == Visibility.Visible)
                    {
                        UpdateQueueView();
                    }
                }
            }
            else
            {
                // If it's the last track, stop playback and reset to idle state
                Console.WriteLine("Reached end of queue, stopping playback and resetting to idle state");

                // Clean up audio objects and reset to idle state
                CleanupAudioObjects();

                // Reset current track info
                currentTrack = null;
                currentTrackIndex = -1;
                currentShuffledIndex = -1;

                // Update title bar player to show no track is playing
                titleBarPlayer.SetTrackInfo("No track selected", "", "");

                // Update queue view if it's visible
                if (queueView.Visibility == Visibility.Visible)
                {
                    UpdateQueueView();
                }

                Console.WriteLine("Playback stopped - app reset to idle state");
            }

            // Reset manual navigation flag after a short delay
            Task.Delay(100).ContinueWith(_ => isManualNavigation = false);
        }

        private void TitleBarPlayer_WindowMinimizeRequested(object? sender, EventArgs e)
        {
            windowManager.MinimizeWindow();
        }

        private void TitleBarPlayer_WindowMaximizeRequested(object? sender, EventArgs e)
        {
            windowManager.ToggleMaximize();
        }





        /// <summary>
        /// Updates the window state tracking when the window state changes externally
        /// </summary>
        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            windowManager.OnStateChanged();
        }

        /// <summary>
        /// Handles window activation to restore custom window style after minimize/restore operations
        /// </summary>
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);

            // Restore custom window style after minimize/restore operations
            if (WindowStyle != WindowStyle.None)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, (DispatcherOperationCallback)delegate (object unused)
                {
                    WindowStyle = WindowStyle.None;

                    // After restoring the window style, check if the window is visually maximized
                    // This helps fix the issue where minimize/restore of maximized windows
                    // doesn't properly update the maximize button icon
                    windowManager.CheckIfWindowIsVisuallyMaximized();

                    return null;
                }, null);
            }
        }

        /// <summary>
        /// Handles window location and size changes to update state tracking
        /// </summary>
        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            windowManager.OnLocationChanged();
        }

        /// <summary>
        /// Handles window size changed events
        /// </summary>
        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            windowManager.OnSizeChanged();
        }


        /// <summary>
        /// Sets up tracking for sidebar width changes
        /// </summary>
        private void SetupSidebarWidthTracking()
        {
            // Initialize the save timer for sidebar width
            sidebarWidthSaveTimer = new DispatcherTimer(
                TimeSpan.FromSeconds(0.5), 
                DispatcherPriority.Background, 
                SidebarWidthSaveTimer_Tick, 
                Dispatcher.CurrentDispatcher);
        }

        /// <summary>
        /// Handles GridSplitter drag completed event
        /// </summary>
        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            MarkSidebarWidthDirty();
        }

        /// <summary>
        /// Marks the sidebar width as dirty and starts the save timer
        /// </summary>
        private void MarkSidebarWidthDirty()
        {
            isSidebarWidthDirty = true;
            
            if (sidebarWidthSaveTimer != null && !sidebarWidthSaveTimer.IsEnabled)
            {
                sidebarWidthSaveTimer.Start();
            }
        }

        /// <summary>
        /// Timer callback to save the sidebar width
        /// </summary>
        private async void SidebarWidthSaveTimer_Tick(object? sender, EventArgs e)
        {
            if (isSidebarWidthDirty)
            {
                isSidebarWidthDirty = false;
                sidebarWidthSaveTimer?.Stop();
                
                try
                {
                    // Update the sidebar width in appSettings
                    if (appSettings.WindowState != null)
                    {
                        appSettings.WindowState.SidebarWidth = sidebarColumn.ActualWidth;
                        await settingsManager.SaveSettingsAsync(appSettings);
                        System.Diagnostics.Debug.WriteLine($"MainWindow: Sidebar width saved: {sidebarColumn.ActualWidth}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving sidebar width: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handles window state changes from the WindowManager
        /// </summary>
        private async void WindowManager_WindowStateChanged(object? sender, EventArgs e)
        {
            try
            {
                // Update the window state in appSettings
                appSettings.WindowState = windowManager.GetCurrentWindowState();

                // Also save current sidebar width
                if (appSettings.WindowState != null)
                {
                    appSettings.WindowState.SidebarWidth = sidebarColumn.ActualWidth;

                    // Save column widths for all views
                    if (appSettings.WindowState.ColumnWidths == null)
                    {
                        appSettings.WindowState.ColumnWidths = new Dictionary<string, Dictionary<string, double>>();
                    }

                    foreach (var kvp in listViewToViewName)
                    {
                        var listView = kvp.Key;
                        var viewName = kvp.Value;
                        
                        if (listView.View is GridView gridView)
                        {
                            var columnWidths = new Dictionary<string, double>();
                            foreach (var column in gridView.Columns)
                            {
                                if (column.Header != null && !double.IsNaN(column.Width))
                                {
                                    columnWidths[column.Header.ToString() ?? ""] = column.Width;
                                }
                            }
                            appSettings.WindowState.ColumnWidths[viewName] = columnWidths;

                            // Also update SongsViewColumnWidths for backward compatibility
                            if (viewName == "Songs")
                            {
                                appSettings.WindowState.SongsViewColumnWidths = columnWidths;
                            }
                        }
                    }
                }

                // Save the settings asynchronously
                await settingsManager.SaveSettingsAsync(appSettings);

                System.Diagnostics.Debug.WriteLine("MainWindow: Window state saved successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MainWindow: Error saving window state: {ex.Message}");
            }
        }

        private void TitleBarPlayer_WindowCloseRequested(object? sender, EventArgs e)
        {
            windowManager.CloseWindow();
        }

        private void TitleBarPlayer_ShuffleStateChanged(object? sender, bool isShuffleEnabled)
        {
            Console.WriteLine($"Shuffle state changed to: {isShuffleEnabled}");

            if (isShuffleEnabled)
            {
                // Shuffle was enabled - regenerate shuffled tracks
                Console.WriteLine("Shuffle enabled - regenerating shuffled tracks");
                RegenerateShuffledTracks();
            }
            else
            {
                // Shuffle was disabled - we need to find the current track in the original filtered list
                // and update the currentTrackIndex to maintain the current position
                Console.WriteLine("Shuffle disabled - updating current track index in filtered list");
                if (currentTrack != null)
                {
                    currentTrackIndex = filteredTracks.IndexOf(currentTrack);
                    if (currentTrackIndex == -1)
                    {
                        // If current track not found in filtered list, reset to beginning
                        currentTrackIndex = 0;
                    }
                    Console.WriteLine($"Current track index updated to: {currentTrackIndex}");
                }
            }

            // Update queue view if it's currently visible
            if (queueView.Visibility == Visibility.Visible)
            {
                UpdateQueueView();
            }
        }

        #endregion

        #region Navigation Events

        private void BtnLibrary_Click(object sender, RoutedEventArgs e)
        {
            ShowLibraryView();
        }

        private void BtnQueue_Click(object sender, RoutedEventArgs e)
        {
            ShowQueueView();
        }

        private void BtnPlaylists_Click(object sender, RoutedEventArgs e)
        {
            ShowPlaylistsView();
        }

        private void BtnRecentlyPlayed_Click(object sender, RoutedEventArgs e)
        {
            ShowRecentlyPlayedView();
        }

        private void BtnArtists_Click(object sender, RoutedEventArgs e)
        {
            ShowArtistsView();
        }

        private void BtnAlbums_Click(object sender, RoutedEventArgs e)
        {
            ShowAlbumsView();
        }

        private void BtnGenres_Click(object sender, RoutedEventArgs e)
        {
            ShowGenresView();
        }

        private async void BtnAddFolder_Click(object sender, RoutedEventArgs e)
        {
            await AddMusicFolderAsync();
        }

        private async void BtnRescanLibrary_Click(object sender, RoutedEventArgs e)
        {
            await RescanLibraryAsync();
        }

        private async void BtnRemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            await RemoveMusicFolderAsync();
        }

        private void BtnClearSettings_Click(object sender, RoutedEventArgs e)
        {
            ClearSettings();
        }

        #endregion

        #region View Management

        private void ShowLibraryView()
        {
            libraryView.Visibility = Visibility.Visible;
            queueView.Visibility = Visibility.Collapsed;
            playlistsView.Visibility = Visibility.Collapsed;
            recentlyPlayedView.Visibility = Visibility.Collapsed;
            artistsView.Visibility = Visibility.Collapsed;
            albumsView.Visibility = Visibility.Collapsed;
            genresView.Visibility = Visibility.Collapsed;
        }

        private void ShowQueueView()
        {
            libraryView.Visibility = Visibility.Collapsed;
            queueView.Visibility = Visibility.Visible;
            playlistsView.Visibility = Visibility.Collapsed;
            recentlyPlayedView.Visibility = Visibility.Collapsed;
            artistsView.Visibility = Visibility.Collapsed;
            albumsView.Visibility = Visibility.Collapsed;
            genresView.Visibility = Visibility.Collapsed;

            // Update the queue list with current playing queue
            UpdateQueueView();
        }

        private void ShowPlaylistsView()
        {
            libraryView.Visibility = Visibility.Collapsed;
            queueView.Visibility = Visibility.Collapsed;
            playlistsView.Visibility = Visibility.Visible;
            recentlyPlayedView.Visibility = Visibility.Collapsed;
            artistsView.Visibility = Visibility.Collapsed;
            albumsView.Visibility = Visibility.Collapsed;
            genresView.Visibility = Visibility.Collapsed;
        }

        private void ShowRecentlyPlayedView()
        {
            libraryView.Visibility = Visibility.Collapsed;
            queueView.Visibility = Visibility.Collapsed;
            playlistsView.Visibility = Visibility.Collapsed;
            recentlyPlayedView.Visibility = Visibility.Visible;
            artistsView.Visibility = Visibility.Collapsed;
            albumsView.Visibility = Visibility.Collapsed;
            genresView.Visibility = Visibility.Collapsed;
        }

        private void ShowArtistsView()
        {
            libraryView.Visibility = Visibility.Collapsed;
            queueView.Visibility = Visibility.Collapsed;
            playlistsView.Visibility = Visibility.Collapsed;
            recentlyPlayedView.Visibility = Visibility.Collapsed;
            artistsView.Visibility = Visibility.Visible;
            albumsView.Visibility = Visibility.Collapsed;
            genresView.Visibility = Visibility.Collapsed;
        }

        private void ShowAlbumsView()
        {
            libraryView.Visibility = Visibility.Collapsed;
            queueView.Visibility = Visibility.Collapsed;
            playlistsView.Visibility = Visibility.Collapsed;
            recentlyPlayedView.Visibility = Visibility.Collapsed;
            artistsView.Visibility = Visibility.Collapsed;
            albumsView.Visibility = Visibility.Visible;
            genresView.Visibility = Visibility.Collapsed;
        }

        private void ShowGenresView()
        {
            libraryView.Visibility = Visibility.Collapsed;
            queueView.Visibility = Visibility.Collapsed;
            playlistsView.Visibility = Visibility.Collapsed;
            recentlyPlayedView.Visibility = Visibility.Collapsed;
            artistsView.Visibility = Visibility.Collapsed;
            albumsView.Visibility = Visibility.Collapsed;
            genresView.Visibility = Visibility.Visible;
        }

        #endregion

        #region Queue Management

        /// <summary>
        /// Updates the queue view with the current playing queue
        /// </summary>
        private void UpdateQueueView()
        {
            try
            {
                Console.WriteLine("UpdateQueueView called");

                var queueView = BuildQueueView();
                Console.WriteLine($"Built queue view with {queueView?.Count ?? 0} songs");

                if (queueView != null && queueView.Count > 0)
                {
                    Console.WriteLine($"Setting lstQueue.ItemsSource to queue with {queueView.Count} songs");
                    lstQueue.ItemsSource = queueView;
                }
                else
                {
                    Console.WriteLine("Queue view is empty, setting lstQueue.ItemsSource to empty collection");
                    lstQueue.ItemsSource = new ObservableCollection<Song>();
                }

                // Log the current state
                var currentQueue = GetCurrentPlayQueue();
                Console.WriteLine($"Current queue state - filteredTracks: {filteredTracks.Count}, shuffledTracks: {shuffledTracks.Count}, currentTrack: {currentTrack?.Title ?? "None"}, currentIndex: {GetCurrentTrackIndex()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating queue view: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                lstQueue.ItemsSource = new ObservableCollection<Song>();
            }
        }

        /// <summary>
        /// Builds a proper queue view with currently playing song at the top
        /// </summary>
        private ObservableCollection<Song> BuildQueueView()
        {
            try
            {
                Console.WriteLine("BuildQueueView called");
                var queueView = new ObservableCollection<Song>();

                // Get the current play queue
                var currentQueue = GetCurrentPlayQueue();
                var currentIndex = GetCurrentTrackIndex();

                Console.WriteLine($"BuildQueueView - currentQueue: {currentQueue?.Count ?? 0}, currentIndex: {currentIndex}, currentTrack: {currentTrack?.Title ?? "None"}");

                if (currentQueue == null || currentQueue.Count == 0)
                {
                    // No queue available, show empty queue
                    Console.WriteLine("BuildQueueView - No queue available, returning empty queue");
                    return queueView;
                }

                if (currentTrack != null && currentIndex >= 0)
                {
                    Console.WriteLine($"BuildQueueView - Building queue with current track: {currentTrack.Title} at index {currentIndex}");

                    if (titleBarPlayer.IsShuffleEnabled)
                    {
                        // For shuffle mode, show current track at top, then remaining tracks in shuffled order
                        // First add the current track
                        queueView.Add(currentTrack);
                        Console.WriteLine($"BuildQueueView - Added current shuffled track at top: {currentTrack.Title}");

                        // Then add remaining tracks from current position onwards (skip previously played)
                        if (currentIndex < currentQueue.Count - 1)
                        {
                            for (int i = currentIndex + 1; i < currentQueue.Count; i++)
                            {
                                var track = currentQueue[i];
                                if (track != null && !string.IsNullOrEmpty(track.FilePath))
                                {
                                    queueView.Add(track);
                                    Console.WriteLine($"BuildQueueView - Added remaining shuffled track: {track.Title} at position {i}");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("BuildQueueView - No remaining tracks after current track in shuffle mode");
                        }
                    }
                    else
                    {
                        // For normal mode, show current track at top followed by remaining tracks
                        // Add the currently playing song at the top
                        queueView.Add(currentTrack);

                        // Add the remaining songs in order (from current position + 1 to end)
                        // Only add if there are actually songs after the current one
                        if (currentIndex < currentQueue.Count - 1)
                        {
                            for (int i = currentIndex + 1; i < currentQueue.Count; i++)
                            {
                                var track = currentQueue[i];
                                if (track != null && !string.IsNullOrEmpty(track.FilePath))
                                {
                                    queueView.Add(track);
                                    Console.WriteLine($"BuildQueueView - Added remaining track: {track.Title}");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("BuildQueueView - No remaining tracks after current track");
                        }
                    }
                }
                else
                {
                    // No current track playing, show empty queue when app is in idle state
                    Console.WriteLine("BuildQueueView - No current track playing, returning empty queue (app is idle)");
                    return queueView;
                }

                Console.WriteLine($"Built queue view - current track: {currentTrack?.Title ?? "None"} (index {currentIndex}), total songs: {queueView.Count}, queue size: {currentQueue.Count}");
                return queueView;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error building queue view: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return new ObservableCollection<Song>();
            }
        }

        /// <summary>
        /// Gets the actual playback queue (the songs that will actually be played)
        /// This is different from the full library - it represents the current play session
        /// </summary>
        private ObservableCollection<Song> GetActualPlaybackQueue()
        {
            try
            {
                // If we have a current track, the playback queue should only include
                // songs from the current track's position to the end of the current queue
                var currentQueue = GetCurrentPlayQueue();
                var currentIndex = GetCurrentTrackIndex();

                if (currentQueue == null || currentQueue.Count == 0 || currentIndex < 0)
                {
                    return new ObservableCollection<Song>();
                }

                // Create a new collection with only the songs that will actually be played
                var playbackQueue = new ObservableCollection<Song>();

                // Add songs from current position to end (these are the songs that will actually play)
                for (int i = currentIndex; i < currentQueue.Count; i++)
                {
                    var track = currentQueue[i];
                    if (track != null && !string.IsNullOrEmpty(track.FilePath))
                    {
                        playbackQueue.Add(track);
                    }
                }

                Console.WriteLine($"GetActualPlaybackQueue - current index: {currentIndex}, total queue: {currentQueue.Count}, playback queue: {playbackQueue.Count}");
                return playbackQueue;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting actual playback queue: {ex.Message}");
                return new ObservableCollection<Song>();
            }
        }

        /// <summary>
        /// Event handler for queue list selection changes
        /// </summary>
        private void LstQueue_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstQueue.SelectedItem is Song selectedSong)
            {
                // When selecting from queue view, jump to the selected track without regenerating shuffle queue
                var currentQueue = GetCurrentPlayQueue();
                if (currentQueue != null)
                {
                    int queueIndex = currentQueue.IndexOf(selectedSong);
                    if (queueIndex >= 0)
                    {
                        Console.WriteLine($"Queue selection: jumping to track '{selectedSong.Title}' at index {queueIndex} in existing queue");

                        // Set manual navigation flag to prevent shuffle queue regeneration
                        isManualNavigation = true;

                        // Start playing the selected track immediately
                        PlayTrack(selectedSong);

                        // Clear the selection to avoid confusion
                        lstQueue.SelectedItem = null;
                    }
                }
            }
        }

        #endregion

        #region Music Management

        private async Task AddMusicFolderAsync()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select a folder containing music files"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                await LoadMusicFromFolderAsync(dialog.SelectedPath, true);
            }
        }

        private async Task RescanLibraryAsync()
        {
            try
            {
                var musicFolders = await libraryManager.GetMusicFoldersAsync();
                if (musicFolders == null || musicFolders.Count == 0)
                {
                    System.Windows.MessageBox.Show("No music folders have been added yet.", "No Folders", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                var totalNewTracks = 0;
                foreach (var folderPath in musicFolders)
                {
                    if (Directory.Exists(folderPath))
                    {
                        // Always re-scan the folder
                        await LoadMusicFromFolderAsync(folderPath, false);
                        totalNewTracks += allTracks.Count(t => t.FilePath.StartsWith(folderPath));
                    }
                }

                UpdateUI();
                System.Windows.MessageBox.Show($"Library re-scanned. Found {totalNewTracks} total tracks across all folders.", "Library Updated", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error re-scanning library: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task RemoveMusicFolderAsync()
        {
            try
            {
                var musicFolders = await libraryManager.GetMusicFoldersAsync();
                if (musicFolders == null || musicFolders.Count == 0)
                {
                    System.Windows.MessageBox.Show("No music folders have been added yet.", "No Folders", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select a folder to remove from the library",
                    ShowNewFolderButton = false
                };

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var folderToRemove = dialog.SelectedPath;
                    if (musicFolders.Contains(folderToRemove))
                    {
                        // Remove tracks from collections
                        var tracksToRemove = allTracks.Where(t => t.FilePath.StartsWith(folderToRemove)).ToList();
                        foreach (var track in tracksToRemove)
                        {
                            allTracks.Remove(track);
                            filteredTracks.Remove(track);
                            recentlyPlayed.Remove(track);
                        }

                        // Remove from playlists
                        foreach (var playlist in playlists)
                        {
                            var playlistTracksToRemove = playlist.Tracks.Where(t => t.FilePath.StartsWith(folderToRemove)).ToList();
                            foreach (var track in playlistTracksToRemove)
                            {
                                playlist.RemoveTrack(track);
                            }
                        }

                        // Remove from library manager
                        await libraryManager.RemoveMusicFolderAsync(folderToRemove);

                        // Remove from cache
                        await libraryManager.RemoveFolderFromCacheAsync(folderToRemove);

                        // Update UI
                        UpdateUI();

                        // Update shuffled tracks if shuffle is enabled
                        UpdateShuffledTracks();

                        // Update queue view if it's currently visible
                        if (queueView.Visibility == Visibility.Visible)
                        {
                            UpdateQueueView();
                        }

                        // Update status bar after removing tracks
                        UpdateStatusBar();

                        System.Windows.MessageBox.Show($"Folder '{folderToRemove}' and {tracksToRemove.Count} tracks removed from library.", "Folder Removed", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                    else
                    {
                        System.Windows.MessageBox.Show($"Folder '{folderToRemove}' not found in library.", "Folder Not Found", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error removing folder: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task LoadMusicFromFolderAsync(string folderPath, bool saveToSettings = false)
        {
            try
            {
                var supportedExtensions = new[] { ".mp3", ".wav", ".flac", ".m4a", ".aac" };
                var musicFiles = await Task.Run(() =>
                {
                    return Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                        .Where(file => supportedExtensions.Contains(Path.GetExtension(file).ToLower()))
                        .ToList();
                });

                // Show progress bar if there are files to process
                if (musicFiles.Count > 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        progressBarFill.Width = 0;
                        progressBarFill.Visibility = Visibility.Visible;
                    });
                }

                // Get existing tracks on UI thread before processing (ObservableCollection must be accessed on UI thread)
                var existingTracks = await Dispatcher.InvokeAsync(() =>
                {
                    return allTracks.Where(t => t.FilePath.StartsWith(folderPath)).ToList();
                });

                var newTracks = new List<Song>();
                int processedCount = 0;
                const int updateInterval = 5; // Update UI every N files

                // Process files on background thread
                await Task.Run(async () =>
                {
                    foreach (var file in musicFiles)
                    {
                        try
                        {
                            // Check if track already exists
                            var existingTrack = existingTracks.FirstOrDefault(t => t.FilePath == file);
                            if (existingTrack == null)
                            {
                                // Load song on background thread
                                var track = LoadSong(file);
                                if (track != null)
                                {
                                    // Add to collections on UI thread
                                    await Dispatcher.InvokeAsync(() =>
                                    {
                                        newTracks.Add(track);
                                        allTracks.Add(track);
                                        filteredTracks.Add(track);
                                    });
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log error but continue with other files
                            Console.WriteLine($"Error loading {file}: {ex.Message}");
                        }
                        finally
                        {
                            // Update progress periodically to avoid excessive UI updates
                            processedCount++;
                            if (processedCount % updateInterval == 0 || processedCount == musicFiles.Count)
                            {
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    // Update progress bar fill width
                                    if (musicFiles.Count > 0)
                                    {
                                        double progressPercent = (double)processedCount / musicFiles.Count;
                                        progressBarFill.Width = progressBarBackground.ActualWidth * progressPercent;
                                    }
                                    // Update status bar statistics dynamically only during active loading
                                    if (progressBarFill.Visibility == Visibility.Visible)
                                    {
                                        UpdateStatusBar();
                                    }
                                });
                                // Yield to allow UI thread to process updates
                                await Task.Yield();
                            }
                        }
                    }
                });

                // Save to library manager if requested
                if (saveToSettings)
                {
                    await libraryManager.AddMusicFolderAsync(folderPath);
                }

                // Update library cache
                await UpdateLibraryCacheAsync();

                // Update folder scan time
                await libraryManager.UpdateFolderScanTimeAsync(folderPath);

                // Update shuffled tracks if shuffle is enabled
                Console.WriteLine($"After loading from folder - allTracks: {allTracks.Count}, filteredTracks: {filteredTracks.Count}");
                UpdateShuffledTracks();

                // Update queue view if it's currently visible
                if (queueView.Visibility == Visibility.Visible)
                {
                    UpdateQueueView();
                }

                // Hide progress bar and final status bar update
                if (musicFiles.Count > 0)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        progressBarFill.Visibility = Visibility.Collapsed;
                        progressBarFill.Width = 0;
                        // Final status bar update
                        UpdateStatusBar();
                    });
                }
            }
            catch (Exception ex)
            {
                // Hide progress bar on error
                await Dispatcher.InvokeAsync(() =>
                {
                    progressBarFill.Visibility = Visibility.Collapsed;
                    progressBarFill.Width = 0;
                });
                System.Windows.MessageBox.Show($"Error loading music folder: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private async Task UpdateLibraryCacheAsync()
        {
            try
            {
                var libraryCache = await libraryManager.LoadLibraryCacheAsync();
                libraryCache.Tracks = allTracks.ToList();
                await libraryManager.SaveLibraryCacheAsync(libraryCache);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error updating library cache: {ex.Message}");
            }
        }

        private Song? LoadSong(string filePath)
        {
            try
            {
                // Get file system info first
                FileInfo? fileInfo = null;
                try
                {
                    fileInfo = new FileInfo(filePath);
                    if (fileInfo.Exists)
                    {
                        // Set file size and date modified
                        // FileSize will be set later if not already cached
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error getting file info for {filePath}: {ex.Message}");
                }

                var track = new Song
                {
                    Title = "Unknown Title",
                    Artist = "Unknown Artist",
                    Album = "Unknown Album",
                    DurationTimeSpan = TimeSpan.Zero,
                    Duration = "00:00",
                    FilePath = filePath,
                    TrackNumber = 0,
                    Year = 0,
                    Genre = "",
                    DateAdded = DateTime.Now
                };

                // Set file system properties
                if (fileInfo != null && fileInfo.Exists)
                {
                    track.DateModified = fileInfo.LastWriteTime;
                    if (track.FileSize == 0)
                    {
                        track.FileSize = fileInfo.Length;
                    }
                }
                
                // Always set FileType from file path extension (even if fileInfo is null)
                if (string.IsNullOrEmpty(track.FileType))
                {
                    var extension = Path.GetExtension(filePath);
                    if (!string.IsNullOrEmpty(extension))
                    {
                        track.FileType = extension.TrimStart('.').ToUpper();
                    }
                }

                // Use ATL.NET to read metadata
                try
                {
                    var atlTrack = new ATL.Track(filePath);

                    // Extract basic metadata from ATL
                    if (!string.IsNullOrEmpty(atlTrack.Title))
                        track.Title = atlTrack.Title;

                    if (!string.IsNullOrEmpty(atlTrack.Artist))
                        track.Artist = atlTrack.Artist;

                    if (!string.IsNullOrEmpty(atlTrack.Album))
                        track.Album = atlTrack.Album;

                    if (atlTrack.TrackNumber.HasValue && atlTrack.TrackNumber.Value > 0)
                        track.TrackNumber = atlTrack.TrackNumber.Value;

                    if (atlTrack.Year.HasValue && atlTrack.Year.Value > 0)
                        track.Year = atlTrack.Year.Value;

                    if (!string.IsNullOrEmpty(atlTrack.Genre))
                        track.Genre = atlTrack.Genre;

                    // Extract additional metadata
                    if (!string.IsNullOrEmpty(atlTrack.AlbumArtist))
                        track.AlbumArtist = atlTrack.AlbumArtist;

                    if (!string.IsNullOrEmpty(atlTrack.Composer))
                        track.Composer = atlTrack.Composer;

                    if (atlTrack.DiscNumber.HasValue && atlTrack.DiscNumber.Value > 0)
                        track.DiscNumber = atlTrack.DiscNumber.Value.ToString();

                    // Audio properties
                    if (atlTrack.Bitrate > 0)
                    {
                        track.Bitrate = $"{atlTrack.Bitrate} kbps";
                    }

                    if (atlTrack.SampleRate > 0)
                    {
                        track.SampleRate = $"{atlTrack.SampleRate / 1000.0:F1} kHz";
                    }

                    // BPM (Beats Per Minute) - check AdditionalFields
                    if (atlTrack.AdditionalFields != null)
                    {
                        // Try common BPM field names
                        if (atlTrack.AdditionalFields.ContainsKey("BPM"))
                        {
                            if (int.TryParse(atlTrack.AdditionalFields["BPM"], out int bpm))
                                track.BeatsPerMinute = bpm;
                        }
                        else if (atlTrack.AdditionalFields.ContainsKey("TBPM"))
                        {
                            if (int.TryParse(atlTrack.AdditionalFields["TBPM"], out int bpm))
                                track.BeatsPerMinute = bpm;
                        }

                        // Category/Grouping
                        if (atlTrack.AdditionalFields.ContainsKey("TCON") || atlTrack.AdditionalFields.ContainsKey("Category"))
                        {
                            var category = atlTrack.AdditionalFields.ContainsKey("Category") 
                                ? atlTrack.AdditionalFields["Category"]
                                : atlTrack.AdditionalFields["TCON"];
                            if (!string.IsNullOrEmpty(category))
                                track.Category = category;
                        }
                    }

                    // Release Date - try to get from date fields
                    if (atlTrack.Date.HasValue)
                    {
                        track.ReleaseDate = atlTrack.Date.Value;
                    }
                    else if (atlTrack.AdditionalFields != null && atlTrack.AdditionalFields.ContainsKey("TDRC"))
                    {
                        // Try to parse release date from ID3 tag
                        if (DateTime.TryParse(atlTrack.AdditionalFields["TDRC"], out DateTime releaseDate))
                        {
                            track.ReleaseDate = releaseDate;
                        }
                    }

                    // Get duration from ATL
                    if (atlTrack.Duration > 0)
                    {
                        track.DurationTimeSpan = TimeSpan.FromSeconds(atlTrack.Duration);
                        track.Duration = track.DurationTimeSpan.ToString(@"mm\:ss");
                    }
                    else
                    {
                        // Fallback to NAudio for duration
                        using var audioFile = new AudioFileReader(filePath);
                        track.DurationTimeSpan = audioFile.TotalTime;
                        track.Duration = audioFile.TotalTime.ToString(@"mm\:ss");
                    }

                    // Check for embedded album art
                    if (atlTrack.EmbeddedPictures != null && atlTrack.EmbeddedPictures.Count > 0)
                    {
                        track.AlbumArtPath = "embedded";
                    }

                    Console.WriteLine($"ATL metadata: Title='{track.Title}', Artist='{track.Artist}', Album='{track.Album}'");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ATL failed for {filePath}: {ex.Message}");

                    // Fallback to NAudio for duration, bitrate, and sample rate
                    try
                    {
                        using var audioFile = new AudioFileReader(filePath);
                        track.DurationTimeSpan = audioFile.TotalTime;
                        track.Duration = audioFile.TotalTime.ToString(@"mm\:ss");
                        
                        // Get sample rate from NAudio if not already set
                        if (string.IsNullOrEmpty(track.SampleRate))
                        {
                            var sampleRate = audioFile.WaveFormat.SampleRate;
                            if (sampleRate > 0)
                            {
                                track.SampleRate = $"{sampleRate / 1000.0:F1} kHz";
                            }
                        }
                        
                        // Try to get bitrate from NAudio if not already set
                        if (string.IsNullOrEmpty(track.Bitrate))
                        {
                            // Calculate bitrate from file size and duration
                            if (track.FileSize > 0 && track.DurationTimeSpan.TotalSeconds > 0)
                            {
                                var bitrateKbps = (int)((track.FileSize * 8) / (track.DurationTimeSpan.TotalSeconds * 1000));
                                if (bitrateKbps > 0)
                                {
                                    track.Bitrate = $"{bitrateKbps} kbps";
                                }
                            }
                        }
                    }
                    catch (Exception audioEx)
                    {
                        Console.WriteLine($"NAudio failed for {filePath}: {audioEx.Message}");
                    }
                }

                // Ensure FileType is set even if fileInfo was null
                if (string.IsNullOrEmpty(track.FileType))
                {
                    var extension = Path.GetExtension(filePath);
                    if (!string.IsNullOrEmpty(extension))
                    {
                        track.FileType = extension.TrimStart('.').ToUpper();
                    }
                }

                return track;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region Playback Control

        private void LstMusic_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstMusic.SelectedItem is Song selectedTrack)
            {
                // When selecting from library view, always regenerate shuffle queue if shuffle is enabled
                PlayTrack(selectedTrack);
            }
        }

        private void LstRecentlyPlayed_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstRecentlyPlayed.SelectedItem is Song selectedTrack)
            {
                // When selecting from recently played view, always regenerate shuffle queue if shuffle is enabled
                PlayTrack(selectedTrack);
            }
        }

        private void LstArtists_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstArtists.SelectedItem is Song selectedTrack)
            {
                // When selecting from artists view, always regenerate shuffle queue if shuffle is enabled
                PlayTrack(selectedTrack);
            }
        }

        private void LstAlbums_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstAlbums.SelectedItem is Song selectedTrack)
            {
                // When selecting from albums view, always regenerate shuffle queue if shuffle is enabled
                PlayTrack(selectedTrack);
            }
        }

        private void LstGenres_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstGenres.SelectedItem is Song selectedTrack)
            {
                // When selecting from genres view, always regenerate shuffle queue if shuffle is enabled
                PlayTrack(selectedTrack);
            }
        }

        #region Column Sorting

        /// <summary>
        /// Wires up click event handlers for all column headers in a ListView
        /// </summary>
        private void WireUpColumnHeaderSorting(ListView listView)
        {
            if (listView.View is GridView gridView)
            {
                // Wait for the ListView to be loaded and rendered so headers exist
                listView.Loaded += (s, e) =>
                {
                    // Use Dispatcher to ensure headers are fully rendered
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        WireUpHeadersForListView(listView);
                    }), DispatcherPriority.Loaded);
                };

                // Also wire up when the ListView is rendered (in case Loaded fires too early)
                listView.LayoutUpdated += (s, e) =>
                {
                    WireUpHeadersForListView(listView);
                };
            }
        }

        /// <summary>
        /// Helper method to wire up headers for a specific ListView
        /// </summary>
        private void WireUpHeadersForListView(ListView listView)
        {
            // Find all GridViewColumnHeader elements
            var headers = FindVisualChildren<GridViewColumnHeader>(listView);
            foreach (var header in headers)
            {
                // Only wire up if not already wired
                if (!wiredHeaders.Contains(header))
                {
                    header.Click += (sender, args) => ColumnHeader_Click(sender, args, listView);
                    wiredHeaders.Add(header);
                }
            }
        }

        /// <summary>
        /// Finds all visual children of a specific type
        /// </summary>
        private IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null)
                    {
                        if (child is T)
                        {
                            yield return (T)child;
                        }

                        foreach (T childOfChild in FindVisualChildren<T>(child))
                        {
                            yield return childOfChild;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles column header click events for sorting
        /// </summary>
        private void ColumnHeader_Click(object sender, RoutedEventArgs e, ListView listView)
        {
            if (sender is GridViewColumnHeader clickedHeader)
            {
                // Get the column header text
                string columnName = clickedHeader.Content?.ToString() ?? "";

                // Determine sort direction (toggle if same column, otherwise ascending)
                ListSortDirection direction = ListSortDirection.Ascending;
                if (sortStates.ContainsKey(listView) && sortStates[listView].column == columnName)
                {
                    // Toggle direction if clicking the same column
                    direction = sortStates[listView].direction == ListSortDirection.Ascending
                        ? ListSortDirection.Descending
                        : ListSortDirection.Ascending;
                }

                // Update sort state
                sortStates[listView] = (columnName, direction);

                // Perform the sort
                SortListView(listView, columnName, direction);
            }
        }

        /// <summary>
        /// Sorts a ListView by the specified column
        /// </summary>
        private void SortListView(ListView listView, string columnName, ListSortDirection direction)
        {
            if (listView.ItemsSource == null) return;

            // Get the default view of the collection
            ICollectionView view = CollectionViewSource.GetDefaultView(listView.ItemsSource);
            if (view == null) return;

            // Clear existing sort descriptions
            view.SortDescriptions.Clear();

            // Get property name from column definitions
            string propertyName = columnName;
            if (columnDefinitions.TryGetValue(columnName, out var columnDef))
            {
                propertyName = columnDef.SortPropertyName;
            }

            // Add new sort description
            view.SortDescriptions.Add(new SortDescription(propertyName, direction));

            // Apply the sort
            view.Refresh();
        }

        /// <summary>
        /// Wires up handlers to monitor column width changes and enforce minimum width
        /// Uses DependencyPropertyDescriptor for efficient property change monitoring
        /// </summary>
        private void WireUpColumnResizeHandlers(ListView listView)
        {
            // Wire up only once when the ListView is loaded
            listView.Loaded += (s, e) =>
            {
                WireUpColumnWidthMonitoring(listView);
            };
        }

        /// <summary>
        /// Monitors GridViewColumn width property changes and enforces minimum width
        /// This is the most efficient approach as it directly monitors the property without visual tree traversal
        /// </summary>
        private void WireUpColumnWidthMonitoring(ListView listView)
        {
            if (listView.View is GridView gridView)
            {
                const double minWidth = 50.0;

                // Monitor each column's width property directly
                foreach (var column in gridView.Columns)
                {
                    // Only wire up if not already wired
                    if (!wiredColumnWidthHandlers.Contains(column))
                    {
                        // Use DependencyPropertyDescriptor to watch for width changes
                        // This is efficient as it directly monitors the property without events
                        var descriptor = DependencyPropertyDescriptor.FromProperty(GridViewColumn.WidthProperty, typeof(GridViewColumn));
                        if (descriptor != null)
                        {
                            descriptor.AddValueChanged(column, (sender, e) =>
                            {
                                if (sender is GridViewColumn col)
                                {
                                    // Enforce minimum width
                                    if (!double.IsNaN(col.Width) && col.Width < minWidth)
                                    {
                                        col.Width = minWidth;
                                    }
                                    
                                    // Mark column widths as dirty for saving
                                    if (listViewToViewName.TryGetValue(listView, out var viewName))
                                    {
                                        MarkColumnWidthDirty(viewName);
                                    }
                                }
                            });
                            wiredColumnWidthHandlers.Add(column);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Sets up column width tracking and save timer
        /// </summary>
        private void SetupColumnWidthTracking()
        {
            // Initialize the save timer for column widths
            columnWidthSaveTimer = new DispatcherTimer(
                TimeSpan.FromSeconds(0.5),
                DispatcherPriority.Background,
                ColumnWidthSaveTimer_Tick,
                Dispatcher.CurrentDispatcher);
        }

        /// <summary>
        /// Marks the column widths as dirty for a specific view and starts the save timer
        /// </summary>
        private void MarkColumnWidthDirty(string viewName)
        {
            isColumnWidthDirty = true;
            dirtyColumnWidthViews.Add(viewName);

            if (columnWidthSaveTimer != null && !columnWidthSaveTimer.IsEnabled)
            {
                columnWidthSaveTimer.Start();
            }
        }

        /// <summary>
        /// Timer callback to save the column widths for all dirty views
        /// </summary>
        private async void ColumnWidthSaveTimer_Tick(object? sender, EventArgs e)
        {
            if (isColumnWidthDirty)
            {
                isColumnWidthDirty = false;
                var viewsToSave = new HashSet<string>(dirtyColumnWidthViews);
                dirtyColumnWidthViews.Clear();
                columnWidthSaveTimer?.Stop();

                try
                {
                    if (appSettings.WindowState == null)
                    {
                        appSettings = await settingsManager.LoadSettingsAsync();
                    }

                    if (appSettings.WindowState.ColumnWidths == null)
                    {
                        appSettings.WindowState.ColumnWidths = new Dictionary<string, Dictionary<string, double>>();
                    }

                    // Save column widths for each dirty view
                    foreach (var viewName in viewsToSave)
                    {
                        ListView? targetListView = viewName switch
                        {
                            "Songs" => lstMusic,
                            "Queue" => lstQueue,
                            "Artists" => lstArtists,
                            "Albums" => lstAlbums,
                            "Genres" => lstGenres,
                            "Recently Played" => lstRecentlyPlayed,
                            _ => null
                        };

                        if (targetListView?.View is GridView gridView)
                        {
                            var columnWidths = new Dictionary<string, double>();
                            foreach (var column in gridView.Columns)
                            {
                                if (column.Header != null && !double.IsNaN(column.Width))
                                {
                                    columnWidths[column.Header.ToString() ?? ""] = column.Width;
                                }
                            }
                            appSettings.WindowState.ColumnWidths[viewName] = columnWidths;

                            // Also update SongsViewColumnWidths for backward compatibility
                            if (viewName == "Songs")
                            {
                                appSettings.WindowState.SongsViewColumnWidths = columnWidths;
                            }
                        }
                    }

                    await settingsManager.SaveSettingsAsync(appSettings);
                    System.Diagnostics.Debug.WriteLine($"MainWindow: Column widths saved for views: {string.Join(", ", viewsToSave)}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving column widths: {ex.Message}");
                }
            }
        }

        #endregion

        private void PlayTrack(Song track)
        {
            try
            {
                // Safety checks
                if (track == null)
                {
                    Console.WriteLine("PlayTrack called with null track");
                    return;
                }

                if (string.IsNullOrEmpty(track.FilePath))
                {
                    Console.WriteLine($"PlayTrack called with track '{track.Title}' that has no file path");
                    return;
                }

                if (!File.Exists(track.FilePath))
                {
                    Console.WriteLine($"PlayTrack called with track '{track.Title}' but file doesn't exist: {track.FilePath}");
                    return;
                }

                Console.WriteLine($"Playing track: {track.Title} - {track.Artist}");

                // Clean up existing audio objects without triggering PlaybackStopped
                // We need to do this manually to avoid resetting the current track
                try
                {
                    Console.WriteLine("Cleaning up audio objects...");

                    // Set the playing state to false temporarily
                    titleBarPlayer.IsPlaying = false;

                    if (waveOut != null)
                    {
                        Console.WriteLine("Removing PlaybackStopped event handler and stopping waveOut");
                        // Remove the event handler before stopping to prevent triggering PlaybackStopped
                        waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
                        waveOut.Stop();
                        waveOut.Dispose();
                        waveOut = null;
                        Console.WriteLine("waveOut disposed");
                    }

                    if (audioFileReader != null)
                    {
                        Console.WriteLine("Disposing audioFileReader");
                        audioFileReader.Dispose();
                        audioFileReader = null;
                        Console.WriteLine("audioFileReader disposed");
                    }

                    Console.WriteLine("Audio objects cleanup completed");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error during audio cleanup: {ex.Message}");
                }

                currentTrack = track;

                // Set the current track index in both queues
                currentTrackIndex = filteredTracks.IndexOf(track);
                currentShuffledIndex = shuffledTracks.IndexOf(track);

                // If shuffle is enabled and this is NOT manual navigation, regenerate shuffled queue
                // Manual navigation (skip forward/backward) should maintain the existing shuffled order
                if (titleBarPlayer.IsShuffleEnabled && !isManualNavigation)
                {
                    Console.WriteLine("Shuffle enabled and not manual navigation - regenerating shuffled queue");
                    RegenerateShuffledTracks();
                    currentShuffledIndex = shuffledTracks.IndexOf(track);
                }
                else if (titleBarPlayer.IsShuffleEnabled && isManualNavigation)
                {
                    Console.WriteLine("Shuffle enabled but manual navigation - maintaining existing shuffled queue");
                    // Just update the index to the new track position
                    currentShuffledIndex = shuffledTracks.IndexOf(track);
                }

                // Load album art if available
                var albumArt = LoadAlbumArt(track);

                // Update title bar player control
                titleBarPlayer.SetTrackInfo(track.Title, track.Artist, track.Album, albumArt);
                titleBarPlayer.SetAudioObjects(waveOut, audioFileReader);

                // Start playback
                audioFileReader = new AudioFileReader(track.FilePath);
                waveOut = new WaveOutEvent();
                waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
                waveOut.Init(audioFileReader);
                waveOut.Play();

                // Update audio objects in control after creation
                titleBarPlayer.SetAudioObjects(waveOut, audioFileReader);
                titleBarPlayer.IsPlaying = true;

                // Add to recently played
                AddToRecentlyPlayed(track);

                // Update playlists view if it's visible
                if (playlistsView.Visibility == Visibility.Visible)
                {
                    UpdatePlaylistsView();
                }

                // Update queue view if it's visible
                if (queueView.Visibility == Visibility.Visible)
                {
                    UpdateQueueView();
                }

                Console.WriteLine($"Successfully started playing: {track.Title}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error playing track '{track?.Title}': {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                // Try to show error to user
                try
                {
                    System.Windows.MessageBox.Show($"Error playing track: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                catch
                {
                    // If showing message box fails, just log it
                    Console.WriteLine("Failed to show error message box");
                }

                // Try to stop playback safely
                try
                {
                    StopPlayback();
                }
                catch (Exception stopEx)
                {
                    Console.WriteLine($"Error stopping playback: {stopEx.Message}");
                }
            }
        }

        /// <summary>
        /// Loads a track without starting playback - useful for track navigation while preserving play state
        /// </summary>
        private void LoadTrackWithoutPlayback(Song track)
        {
            try
            {
                // Safety checks
                if (track == null)
                {
                    Console.WriteLine("LoadTrackWithoutPlayback called with null track");
                    return;
                }

                if (string.IsNullOrEmpty(track.FilePath))
                {
                    Console.WriteLine($"LoadTrackWithoutPlayback called with track '{track.Title}' that has no file path");
                    return;
                }

                if (!File.Exists(track.FilePath))
                {
                    Console.WriteLine($"LoadTrackWithoutPlayback called with track '{track.Title}' but file doesn't exist: {track.FilePath}");
                    return;
                }

                Console.WriteLine($"Loading track without playback: {track.Title} - {track.Artist}");

                // Store current playback state
                bool wasPlaying = titleBarPlayer.IsPlaying;

                // Clean up existing audio objects without triggering PlaybackStopped
                CleanupAudioObjects();

                currentTrack = track;

                // Set the current track index in both queues
                currentTrackIndex = filteredTracks.IndexOf(track);
                currentShuffledIndex = shuffledTracks.IndexOf(track);

                // If shuffle is enabled and this is NOT manual navigation, regenerate shuffled queue
                // Manual navigation (skip forward/backward) should maintain the existing shuffled order
                if (titleBarPlayer.IsShuffleEnabled && !isManualNavigation)
                {
                    Console.WriteLine("Shuffle enabled and not manual navigation - regenerating shuffled queue");
                    RegenerateShuffledTracks();
                    currentShuffledIndex = shuffledTracks.IndexOf(track);
                }
                else if (titleBarPlayer.IsShuffleEnabled && isManualNavigation)
                {
                    Console.WriteLine("Shuffle enabled but manual navigation - maintaining existing shuffled queue");
                    // Just update the index to the new track position
                    currentShuffledIndex = shuffledTracks.IndexOf(track);
                }

                // Load album art if available
                var albumArt = LoadAlbumArt(track);

                // Update title bar player control
                titleBarPlayer.SetTrackInfo(track.Title, track.Artist, track.Album, albumArt);

                // Create audio objects but don't start playback
                audioFileReader = new AudioFileReader(track.FilePath);
                waveOut = new WaveOutEvent();
                waveOut.PlaybackStopped += WaveOut_PlaybackStopped;
                waveOut.Init(audioFileReader);

                // Update audio objects in control
                titleBarPlayer.SetAudioObjects(waveOut, audioFileReader);

                // Restore the previous playback state
                titleBarPlayer.IsPlaying = wasPlaying;

                // Add to recently played
                AddToRecentlyPlayed(track);

                // Update playlists view if it's visible
                if (playlistsView.Visibility == Visibility.Visible)
                {
                    UpdatePlaylistsView();
                }

                // Update queue view if it's visible
                if (queueView.Visibility == Visibility.Visible)
                {
                    UpdateQueueView();
                }

                Console.WriteLine($"Successfully loaded track without playback: {track.Title}, playback state: {titleBarPlayer.IsPlaying}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading track '{track?.Title}' without playback: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                // Try to show error to user
                try
                {
                    System.Windows.MessageBox.Show($"Error loading track: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
                catch
                {
                    // If showing message box fails, just log it
                    Console.WriteLine("Failed to show error message box");
                }

                // Try to stop playback safely
                try
                {
                    StopPlayback();
                }
                catch (Exception stopEx)
                {
                    Console.WriteLine($"Error stopping playback: {stopEx.Message}");
                }
            }
        }

        private BitmapImage? LoadAlbumArt(Song track)
        {
            try
            {
                // First try to load embedded album art using ATL.NET
                try
                {
                    var atlTrack = new ATL.Track(track.FilePath);
                    var embeddedPictures = atlTrack.EmbeddedPictures;

                    if (embeddedPictures != null && embeddedPictures.Count > 0)
                    {
                        var picture = embeddedPictures[0]; // Get the first picture (usually the album art)
                        var scaledBitmap = CreateHighQualityScaledImage(picture.PictureData);
                        return scaledBitmap; // Successfully loaded embedded art
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error loading embedded album art for {track.Title}: {ex.Message}");
                }

                // Fallback: Try to find album art in the same directory as the music file
                var directory = Path.GetDirectoryName(track.FilePath);
                if (directory != null)
                {
                    var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
                    var imageFiles = Directory.GetFiles(directory, "*.*")
                        .Where(file => imageExtensions.Contains(Path.GetExtension(file).ToLower()))
                        .ToList();

                    // Look for common album art filenames
                    var albumArtFile = imageFiles.FirstOrDefault(file =>
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file).ToLower();
                        return fileName.Contains("album") ||
                               fileName.Contains("cover") ||
                               fileName.Contains("art") ||
                               fileName.Contains("folder");
                    });

                    // If no specific album art found, use the first image file
                    if (albumArtFile == null && imageFiles.Count > 0)
                    {
                        albumArtFile = imageFiles[0];
                    }

                    if (albumArtFile != null)
                    {
                        var scaledBitmap = CreateHighQualityScaledImageFromFile(albumArtFile);
                        return scaledBitmap;
                    }
                    else
                    {
                        // No album art found, return null
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                // If we can't load album art, return null
                Console.WriteLine($"Error loading album art for {track.Title}: {ex.Message}");
                return null;
            }
        }

        private BitmapImage? CreateHighQualityScaledImage(byte[] imageData)
        {
            try
            {
                using var originalStream = new MemoryStream(imageData);
                using var originalBitmap = new System.Drawing.Bitmap(originalStream);

                // Get the target size (assuming the Image control is around 60x60 pixels)
                int targetSize = 120; // Use 2x for high DPI displays

                // Calculate new dimensions maintaining aspect ratio
                int originalWidth = originalBitmap.Width;
                int originalHeight = originalBitmap.Height;

                double ratio = Math.Min((double)targetSize / originalWidth, (double)targetSize / originalHeight);
                int newWidth = (int)(originalWidth * ratio);
                int newHeight = (int)(originalHeight * ratio);

                // Create high-quality scaled bitmap (WPF will handle the rounded corners via clipping)
                using var scaledBitmap = new System.Drawing.Bitmap(newWidth, newHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using var graphics = System.Drawing.Graphics.FromImage(scaledBitmap);

                // Set high-quality rendering options
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                // Draw the scaled image
                graphics.DrawImage(originalBitmap, 0, 0, newWidth, newHeight);

                // Convert to WPF BitmapImage
                var wpfBitmap = new BitmapImage();
                using var stream = new MemoryStream();
                scaledBitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;

                wpfBitmap.BeginInit();
                wpfBitmap.CacheOption = BitmapCacheOption.OnLoad;
                wpfBitmap.StreamSource = stream;
                wpfBitmap.EndInit();
                wpfBitmap.Freeze(); // Freeze for better performance

                return wpfBitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating high-quality scaled image: {ex.Message}");
                return null;
            }
        }

        private BitmapImage? CreateHighQualityScaledImageFromFile(string filePath)
        {
            try
            {
                using var originalBitmap = new System.Drawing.Bitmap(filePath);

                // Get the target size (assuming the Image control is around 60x60 pixels)
                int targetSize = 120; // Use 2x for high DPI displays

                // Calculate new dimensions maintaining aspect ratio
                int originalWidth = originalBitmap.Width;
                int originalHeight = originalBitmap.Height;

                double ratio = Math.Min((double)targetSize / originalWidth, (double)targetSize / originalHeight);
                int newWidth = (int)(originalWidth * ratio);
                int newHeight = (int)(originalHeight * ratio);

                // Create high-quality scaled bitmap (WPF will handle the rounded corners via clipping)
                using var scaledBitmap = new System.Drawing.Bitmap(newWidth, newHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using var graphics = System.Drawing.Graphics.FromImage(scaledBitmap);

                // Set high-quality rendering options
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                // Draw the scaled image
                graphics.DrawImage(originalBitmap, 0, 0, newWidth, newHeight);

                // Convert to WPF BitmapImage
                var wpfBitmap = new BitmapImage();
                using var stream = new MemoryStream();
                scaledBitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                stream.Position = 0;

                wpfBitmap.BeginInit();
                wpfBitmap.CacheOption = BitmapCacheOption.OnLoad;
                wpfBitmap.StreamSource = stream;
                wpfBitmap.EndInit();
                wpfBitmap.Freeze(); // Freeze for better performance

                return wpfBitmap;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error creating high-quality scaled image from file: {ex.Message}");
                return null;
            }
        }



        private void AddToRecentlyPlayed(Song track)
        {
            // Mark track as played
            track.MarkAsPlayed();

            // Remove if already exists
            var existing = recentlyPlayed.FirstOrDefault(t => t.FilePath == track.FilePath);
            if (existing != null)
            {
                recentlyPlayed.Remove(existing);
            }

            // Add to beginning
            recentlyPlayed.Insert(0, track);

            // Keep only last 20 tracks
            while (recentlyPlayed.Count > 20)
            {
                recentlyPlayed.RemoveAt(recentlyPlayed.Count - 1);
            }
        }



        private void PausePlayback()
        {
            if (waveOut != null)
            {
                waveOut.Pause();
                titleBarPlayer.IsPlaying = false;
            }
        }

        private void ResumePlayback()
        {
            if (waveOut != null)
            {
                waveOut.Play();
                titleBarPlayer.IsPlaying = true;
            }
        }

        /// <summary>
        /// Safely resets the app to idle state (no track playing)
        /// </summary>
        private void ResetToIdleState()
        {
            try
            {
                Console.WriteLine("Resetting app to idle state...");

                // Reset current track info
                currentTrack = null;
                currentTrackIndex = -1;
                currentShuffledIndex = -1;

                // Update title bar player to show no track is playing
                titleBarPlayer.SetTrackInfo("No track selected", "", "");

                // Update queue view if it's visible
                if (queueView.Visibility == Visibility.Visible)
                {
                    UpdateQueueView();
                }

                Console.WriteLine("App reset to idle state successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error resetting to idle state: {ex.Message}");
            }
        }

        /// <summary>
        /// Safely cleans up audio objects without triggering PlaybackStopped event
        /// </summary>
        private void CleanupAudioObjects()
        {
            try
            {
                Console.WriteLine("Cleaning up audio objects...");

                // Set the playing state to false
                titleBarPlayer.IsPlaying = false;

                if (waveOut != null)
                {
                    Console.WriteLine("Removing PlaybackStopped event handler and stopping waveOut");
                    // Remove the event handler before stopping to prevent triggering PlaybackStopped
                    waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
                    waveOut.Stop();
                    waveOut.Dispose();
                    waveOut = null;
                    Console.WriteLine("waveOut disposed");
                }

                if (audioFileReader != null)
                {
                    Console.WriteLine("Disposing audioFileReader");
                    audioFileReader.Dispose();
                    audioFileReader = null;
                    Console.WriteLine("audioFileReader disposed");
                }

                Console.WriteLine("Audio objects cleanup completed");

                // Reset app state to idle
                ResetToIdleState();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during audio cleanup: {ex.Message}");
            }
        }

        private void StopPlayback()
        {
            // Set flag to indicate we're manually stopping playback
            isManuallyStopping = true;

            try
            {
                // Use the safe cleanup method
                CleanupAudioObjects();
            }
            finally
            {
                // Reset flag after a short delay to allow for cleanup
                Task.Delay(100).ContinueWith(_ => isManuallyStopping = false);
            }
        }

        private void WaveOut_PlaybackStopped(object? sender, StoppedEventArgs e)
        {
            try
            {
                // Check if we're manually stopping playback
                if (isManuallyStopping)
                {
                    Console.WriteLine("Playback stopped manually, not advancing to next track");
                    return;
                }

                // Additional safety check: ensure we have valid audio objects
                if (waveOut == null || audioFileReader == null)
                {
                    Console.WriteLine("Audio objects are null, not advancing to next track");
                    return;
                }

                // Additional safety check: ensure the audio objects are still valid (not disposed)
                try
                {
                    var _ = audioFileReader.TotalTime;
                }
                catch (ObjectDisposedException)
                {
                    Console.WriteLine("AudioFileReader was disposed, not advancing to next track");
                    return;
                }
                catch (NullReferenceException)
                {
                    Console.WriteLine("AudioFileReader is null, not advancing to next track");
                    return;
                }

                // This event is raised when the audio playback finishes naturally.
                // Handle the track finished logic directly here
                var currentQueue = GetCurrentPlayQueue();
                var currentIndex = GetCurrentTrackIndex();

                Console.WriteLine($"Track finished naturally - currentIndex: {currentIndex}, queue count: {currentQueue.Count}");

                // Safety checks
                if (currentQueue == null || currentQueue.Count == 0)
                {
                    Console.WriteLine("No tracks in queue, stopping playback");
                    CleanupAudioObjects();
                    return;
                }

                if (currentIndex < 0 || currentIndex >= currentQueue.Count)
                {
                    Console.WriteLine($"Invalid current index: {currentIndex}, resetting to 0");
                    currentIndex = 0;
                }

                if (currentIndex < currentQueue.Count - 1)
                {
                    var nextTrack = GetTrackFromCurrentQueue(currentIndex + 1);
                    if (nextTrack != null)
                    {
                        Console.WriteLine($"Advancing to next track: {nextTrack.Title}");
                        PlayTrack(nextTrack);

                        // Update queue view if it's visible
                        if (queueView.Visibility == Visibility.Visible)
                        {
                            UpdateQueueView();
                        }
                    }
                    else
                    {
                        Console.WriteLine("Next track is null or has invalid file path, stopping playback");
                        // Clean up and reset to idle state
                        CleanupAudioObjects();
                    }
                }
                else
                {
                    // If it's the last track, stop playback and reset to idle state
                    Console.WriteLine("Reached end of queue, stopping playback and resetting to idle state");

                    // Clean up audio objects and reset to idle state
                    CleanupAudioObjects();

                    Console.WriteLine("Playback stopped - app reset to idle state");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in WaveOut_PlaybackStopped: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                // Fallback: try to stop playback safely
                try
                {
                    CleanupAudioObjects();
                }
                catch (Exception stopEx)
                {
                    Console.WriteLine($"Error stopping playback: {stopEx.Message}");
                }
            }
        }

        #endregion

        #region Playlist Management

        private void UpdatePlaylistsView()
        {
            // This method can be expanded to show which playlists contain the current track
        }

        #endregion

        #region Settings Management

        private void ClearSettings()
        {
            try
            {
                // Show confirmation dialog
                var result = System.Windows.MessageBox.Show(
                    "This will clear all settings and return the app to a clean state. This action cannot be undone.\n\n" +
                    "The following will be cleared:\n" +
                    "• Music library cache\n" +
                    "• Recently played history\n" +
                    "• Playlists\n" +
                    "• Music folders\n" +
                    "• Window settings\n\n" +
                    "Are you sure you want to continue?",
                    "Clear Settings",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);

                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }

                // Get the AppData\Roaming\MusicApp directory
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MusicApp");

                if (!Directory.Exists(appDataPath))
                {
                    System.Windows.MessageBox.Show("No settings found to clear.", "No Settings", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                // Find all JSON files in the directory
                var jsonFiles = Directory.GetFiles(appDataPath, "*.json", SearchOption.TopDirectoryOnly);

                if (jsonFiles.Length == 0)
                {
                    System.Windows.MessageBox.Show("No settings files found to clear.", "No Settings", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    return;
                }

                // Move files to recycle bin
                int movedFiles = 0;
                foreach (var file in jsonFiles)
                {
                    try
                    {
                        // Use Windows API to move to recycle bin
                        if (MoveToRecycleBin(file))
                        {
                            movedFiles++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error moving file {file} to recycle bin: {ex.Message}");
                    }
                }

                // Clear in-memory collections
                allTracks.Clear();
                filteredTracks.Clear();
                shuffledTracks.Clear();
                playlists.Clear();
                recentlyPlayed.Clear();

                // Reset current track
                currentTrack = null;
                currentTrackIndex = -1;
                currentShuffledIndex = -1;

                // Stop any current playback
                StopPlayback();

                // Clear title bar player track info
                titleBarPlayer.SetTrackInfo("No track selected", "", "");

                // Reset window state to default
                appSettings = new SettingsManager.AppSettings();
                windowManager.ResetWindowState();

                // Update queue view if it's visible
                if (queueView.Visibility == Visibility.Visible)
                {
                    UpdateQueueView();
                }

                // Update UI
                UpdateUI();

                // Show success message
                System.Windows.MessageBox.Show(
                    $"Successfully cleared {movedFiles} settings files.\n\n" +
                    "The app has been reset to a clean state.",
                    "Settings Cleared",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error clearing settings: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private bool MoveToRecycleBin(string filePath)
        {
            try
            {
                // Use Windows API to move file to recycle bin
                var shf = new SHFILEOPSTRUCT
                {
                    wFunc = FO_DELETE,
                    pFrom = filePath + '\0' + '\0', // Double null-terminated string
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT,
                    fAnyOperationsAborted = false
                };

                int result = SHFileOperation(ref shf);
                return result == 0;
            }
            catch
            {
                // Fallback: try to delete the file directly
                try
                {
                    File.Delete(filePath);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        #endregion





        protected override async void OnClosing(CancelEventArgs e)
        {
            try
            {
                // Get current player settings from the title bar player control
                appSettings.Player = new SettingsManager.PlayerSettings
                {
                    IsShuffleEnabled = titleBarPlayer.IsShuffleEnabled,
                    RepeatMode = titleBarPlayer.RepeatMode
                };

                // Save current window state - always save the normal window bounds, not the current maximized dimensions
                appSettings.WindowState = windowManager.GetCurrentWindowState();

                // Also save current sidebar width
                if (appSettings.WindowState != null)
                {
                    appSettings.WindowState.SidebarWidth = sidebarColumn.ActualWidth;

                    // Save column widths for all views
                    if (appSettings.WindowState.ColumnWidths == null)
                    {
                        appSettings.WindowState.ColumnWidths = new Dictionary<string, Dictionary<string, double>>();
                    }

                    // Save column visibility for all views
                    if (appSettings.WindowState.ColumnVisibility == null)
                    {
                        appSettings.WindowState.ColumnVisibility = new Dictionary<string, List<string>>();
                    }

                    foreach (var kvp in listViewToViewName)
                    {
                        var listView = kvp.Key;
                        var viewName = kvp.Value;
                        
                        if (listView.View is GridView gridView)
                        {
                            // Save column widths
                            var columnWidths = new Dictionary<string, double>();
                            foreach (var column in gridView.Columns)
                            {
                                if (column.Header != null && !double.IsNaN(column.Width))
                                {
                                    columnWidths[column.Header.ToString() ?? ""] = column.Width;
                                }
                            }
                            appSettings.WindowState.ColumnWidths[viewName] = columnWidths;

                            // Save column visibility (get current visible columns in order)
                            var visibleColumns = new List<string>();
                            foreach (var column in gridView.Columns)
                            {
                                if (column.Header != null)
                                {
                                    // Find the column name from the header display name
                                    var headerText = column.Header.ToString() ?? "";
                                    // Search column definitions to find matching display name
                                    foreach (var colDef in columnDefinitions)
                                    {
                                        if (colDef.Value.DisplayName == headerText)
                                        {
                                            visibleColumns.Add(colDef.Key);
                                            break;
                                        }
                                    }
                                }
                            }
                            // Always save visibility, even if empty (will use defaults on load)
                            appSettings.WindowState.ColumnVisibility[viewName] = visibleColumns;

                            // Also update SongsViewColumnWidths for backward compatibility
                            if (viewName == "Songs")
                            {
                                appSettings.WindowState.SongsViewColumnWidths = columnWidths;
                            }
                        }
                    }
                }

                // Save settings
                await settingsManager.SaveSettingsAsync(appSettings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings on close: {ex.Message}");
            }

            StopPlayback();
            base.OnClosing(e);
        }
    }
}