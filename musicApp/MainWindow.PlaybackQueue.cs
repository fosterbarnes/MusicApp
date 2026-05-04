using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using musicApp.Helpers;
using musicApp.Views;

namespace musicApp;

public partial class MainWindow
{
    private ObservableCollection<Song>? contextualPlaybackFuture;
    private readonly List<Song> contextualShuffledFuture = new();
    private readonly List<Song> contextualPlaybackHistoryMru = new();
    private List<Song>? contextualSessionOrderedFull;
    private readonly HashSet<Song> userQueuedSongs = new();

    private static void FisherYatesRange(IList<Song> list, int loInclusive, int hiInclusive, Random? rnd = null)
    {
        if (list == null || loInclusive >= hiInclusive)
            return;

        rnd ??= new Random();
        for (int i = hiInclusive; i > loInclusive; i--)
        {
            int j = rnd.Next(loInclusive, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static bool SameSongPath(Song? a, Song? b)
    {
        if (a == null || b == null)
            return ReferenceEquals(a, b);
        if (!string.IsNullOrWhiteSpace(a.FilePath) && !string.IsNullOrWhiteSpace(b.FilePath))
            return string.Equals(a.FilePath, b.FilePath, StringComparison.OrdinalIgnoreCase);
        return ReferenceEquals(a, b);
    }

    private static bool SongListsIdenticalOrderByPath(IReadOnlyList<Song> linear, IList<Song> other)
    {
        if (linear.Count != other.Count)
            return false;
        for (int i = 0; i < linear.Count; i++)
        {
            if (!SameSongPath(linear[i], other[i]))
                return false;
        }
        return true;
    }

    private const int ShuffleDiffersMaxAttempts = 64;

    /// <summary>
    /// Shuffles <paramref name="mutableOrder"/> in [rangeLo, rangeHi] until it differs from
    /// <paramref name="linearOrder"/> by position, when linear has more than 2 items.
    /// </summary>
    private static void ShuffleRangeUntilOrderDiffersFromLinear(
        IList<Song> mutableOrder,
        IReadOnlyList<Song> linearOrder,
        int rangeLoInclusive,
        int rangeHiInclusive)
    {
        if (mutableOrder == null || linearOrder == null || mutableOrder.Count != linearOrder.Count)
            return;

        if (rangeLoInclusive >= rangeHiInclusive)
            return;

        if (linearOrder.Count <= 2)
        {
            FisherYatesRange(mutableOrder, rangeLoInclusive, rangeHiInclusive);
            return;
        }

        var rnd = new Random();
        for (int attempt = 0; attempt < ShuffleDiffersMaxAttempts; attempt++)
        {
            FisherYatesRange(mutableOrder, rangeLoInclusive, rangeHiInclusive, rnd);
            if (!SongListsIdenticalOrderByPath(linearOrder, mutableOrder))
                return;
        }
    }

    private HashSet<string> ContextualHistoryPathSet()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in contextualPlaybackHistoryMru)
            if (t != null && !string.IsNullOrWhiteSpace(t.FilePath))
                set.Add(t.FilePath);
        return set;
    }

    /// <summary>
    /// Returns the natural-order future from <paramref name="anchor"/> onward, dropping anything
    /// already in history (unless the user explicitly queued it).
    /// </summary>
    private List<Song> DeriveNaturalFutureFromAnchor(Song? anchor)
    {
        var result = new List<Song>();
        if (contextualSessionOrderedFull == null || contextualSessionOrderedFull.Count == 0)
            return result;

        var hist = ContextualHistoryPathSet();
        int idx = anchor != null
            ? ArtistPlaybackOrder.IndexOfTrackInOrderedList(contextualSessionOrderedFull, anchor)
            : -1;
        int start = idx >= 0 ? idx : 0;

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = start; i < contextualSessionOrderedFull.Count; i++)
        {
            var t = contextualSessionOrderedFull[i];
            if (t == null || string.IsNullOrWhiteSpace(t.FilePath))
                continue;
            bool isAnchor = i == idx;
            bool inHistory = hist.Contains(t.FilePath);
            bool isInjected = userQueuedSongs.Contains(t);
            if (!isAnchor && inHistory && !isInjected)
                continue;
            if (!seenPaths.Add(t.FilePath))
                continue;
            result.Add(t);
        }
        return result;
    }

    /// <summary>
    /// Builds <see cref="contextualShuffledFuture"/> with <paramref name="anchor"/> as head and a
    /// fresh Fisher-Yates of the remaining unplayed (and injected) tracks.
    /// </summary>
    private void BuildShuffledFutureForAnchor(Song? anchor)
    {
        contextualShuffledFuture.Clear();
        if (contextualSessionOrderedFull == null || contextualSessionOrderedFull.Count == 0)
            return;

        var hist = ContextualHistoryPathSet();
        var pool = new List<Song>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (anchor != null && !string.IsNullOrWhiteSpace(anchor.FilePath))
            seenPaths.Add(anchor.FilePath);

        foreach (var t in contextualSessionOrderedFull)
        {
            if (t == null || string.IsNullOrWhiteSpace(t.FilePath))
                continue;
            if (anchor != null && SameSongPath(t, anchor))
                continue;
            bool inHistory = hist.Contains(t.FilePath);
            bool isInjected = userQueuedSongs.Contains(t);
            if (inHistory && !isInjected)
                continue;
            if (!seenPaths.Add(t.FilePath))
                continue;
            pool.Add(t);
        }

        if (anchor != null)
            contextualShuffledFuture.Add(anchor);

        if (pool.Count > 1)
            FisherYatesRange(pool, 0, pool.Count - 1);

        foreach (var t in pool)
            contextualShuffledFuture.Add(t);
    }

    /// <summary>
    /// Repopulates <see cref="contextualPlaybackFuture"/> from the active source (shuffled tail or
    /// natural derivation), with <paramref name="anchor"/> as the implied head.
    /// </summary>
    private void SetActivePlaybackFuture(Song? anchor)
    {
        contextualPlaybackFuture ??= new ObservableCollection<Song>();
        contextualPlaybackFuture.Clear();

        if (titleBarPlayer.IsShuffleEnabled)
        {
            foreach (var t in contextualShuffledFuture)
                contextualPlaybackFuture.Add(t);
        }
        else
        {
            var nat = DeriveNaturalFutureFromAnchor(anchor);
            foreach (var t in nat)
                contextualPlaybackFuture.Add(t);
        }
    }

    private void InitializeContextualSession(IReadOnlyList<Song> ordered, Song selected)
    {
        if (ordered.Count == 0 || selected == null)
            return;

        var src = ordered.Where(t => t != null).ToList();
        if (src.Count == 0)
            return;

        int idx = ArtistPlaybackOrder.IndexOfTrackInOrderedList(src, selected);
        if (idx < 0)
            return;

        ResetUserQueuedFlagsForCurrentSession();

        contextualSessionOrderedFull = new List<Song>(src);
        contextualPlaybackHistoryMru.Clear();
        contextualShuffledFuture.Clear();
        userQueuedSongs.Clear();

        if (titleBarPlayer.IsShuffleEnabled)
        {
            BuildShuffledFutureForAnchor(selected);
        }
        else
        {
            for (int i = idx - 1; i >= 0; i--)
                contextualPlaybackHistoryMru.Add(src[i]);
        }

        SetActivePlaybackFuture(selected);
        if (contextualPlaybackFuture == null || contextualPlaybackFuture.Count == 0)
            ClearContextualPlaybackQueue();
    }

    private void TryInitializeContextFromPlayTrack(object? requestSource, Song selectedTrack)
    {
        TryInitializeArtistContextQueue(requestSource, selectedTrack);
        TryInitializeGenreContextQueue(requestSource, selectedTrack);
        TryInitializeSongsContextQueue(requestSource, selectedTrack);
        TryInitializePlaylistContextQueue(requestSource, selectedTrack);
        TryInitializeAlbumContextQueue(requestSource, selectedTrack);
    }

    private void TryInitializeArtistContextQueue(object? requestSource, Song selectedTrack)
    {
        if (artistsViewControl == null ||
            !ReferenceEquals(requestSource, artistsViewControl) ||
            !string.Equals(artistsViewControl.ViewName, "Artists", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(selectedTrack.Artist))
            return;

        ClearContextualPlaybackQueue();

        var ordered = ArtistPlaybackOrder.BuildOrderedArtistTracks(allTracks, selectedTrack.Artist);
        InitializeContextualSession(ordered, selectedTrack);
    }

    private void TryInitializeGenreContextQueue(object? requestSource, Song selectedTrack)
    {
        if (genresViewControl == null ||
            !ReferenceEquals(requestSource, genresViewControl) ||
            !string.Equals(genresViewControl.ViewName, "Genres", StringComparison.OrdinalIgnoreCase))
            return;

        if (string.IsNullOrWhiteSpace(selectedTrack.Genre))
            return;

        ClearContextualPlaybackQueue();

        var ordered = GenrePlaybackOrder.BuildOrderedGenreTracks(allTracks, selectedTrack.Genre);
        InitializeContextualSession(ordered, selectedTrack);
    }

    private void TryInitializeSongsContextQueue(object? requestSource, Song selectedTrack)
    {
        if (!ReferenceEquals(requestSource, songsView))
            return;

        ClearContextualPlaybackQueue();

        var ordered = allTracks.ToList();
        InitializeContextualSession(ordered, selectedTrack);
    }

    private void TryInitializePlaylistContextQueue(object? requestSource, Song selectedTrack)
    {
        if (playlistsViewControl == null || !ReferenceEquals(requestSource, playlistsViewControl))
            return;

        var pl = playlistsViewControl.SelectedPlaylist;
        if (pl == null)
            return;

        ClearContextualPlaybackQueue();

        var ordered = pl.Tracks.ToList();
        InitializeContextualSession(ordered, selectedTrack);
    }

    private void TryInitializeAlbumContextQueue(object? requestSource, Song selectedTrack)
    {
        if (!ReferenceEquals(requestSource, albumsViewControl) || albumsViewControl == null)
            return;

        if (albumsViewControl.BrowseMode == AlbumsBrowseMode.RecentlyAdded)
        {
            ClearContextualPlaybackQueue();

            var ordered = RecentlyAddedPlaybackOrder.BuildOrderedTracks(allTracks);
            if (ordered.Count == 0)
                return;

            InitializeContextualSession(ordered, selectedTrack);
            return;
        }

        string albumTitle = selectedTrack.Album ?? string.Empty;
        if (string.IsNullOrWhiteSpace(albumTitle))
            return;

        string selectedAlbumArtist = !string.IsNullOrWhiteSpace(selectedTrack.AlbumArtist)
            ? selectedTrack.AlbumArtist
            : selectedTrack.Artist ?? string.Empty;

        ClearContextualPlaybackQueue();

        var albumTracks = AlbumTrackOrder.SortByAlbumSequence(
            allTracks.Where(s =>
                string.Equals(s.Album, albumTitle, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    !string.IsNullOrWhiteSpace(s.AlbumArtist) ? s.AlbumArtist : s.Artist,
                    selectedAlbumArtist,
                    StringComparison.OrdinalIgnoreCase)));

        if (albumTracks.Count == 0)
            return;

        InitializeContextualSession(albumTracks, selectedTrack);
    }

    private bool HasContextualPlaybackQueue()
    {
        return contextualPlaybackFuture != null && contextualPlaybackFuture.Count > 0;
    }

    private void ResetUserQueuedFlagsForCurrentSession()
    {
        foreach (var s in userQueuedSongs)
        {
            if (s != null)
                s.IsUserQueued = false;
        }
    }

    private void ClearContextualPlaybackQueue()
    {
        ResetUserQueuedFlagsForCurrentSession();
        userQueuedSongs.Clear();

        contextualPlaybackFuture = null;
        contextualShuffledFuture.Clear();
        contextualPlaybackHistoryMru.Clear();
        contextualSessionOrderedFull = null;
    }

    /// <summary>
    /// Wraps the contextual session for Repeat-All: clears history, regenerates the active future
    /// rooted at the first track of the session, and yields the new starting track.
    /// </summary>
    private bool TryWrapContextualForRepeatAll(out Song? startTrack)
    {
        startTrack = null;
        if (contextualSessionOrderedFull == null || contextualSessionOrderedFull.Count == 0)
            return false;

        var first = contextualSessionOrderedFull[0];
        if (first == null)
            return false;

        contextualPlaybackHistoryMru.Clear();

        if (titleBarPlayer.IsShuffleEnabled)
            BuildShuffledFutureForAnchor(first);
        else
            contextualShuffledFuture.Clear();

        SetActivePlaybackFuture(first);

        if (contextualPlaybackFuture == null || contextualPlaybackFuture.Count == 0)
            return false;

        startTrack = contextualPlaybackFuture[0];
        return startTrack != null;
    }

    private void ClearInjectedFlagFor(Song? song)
    {
        if (song == null) return;
        if (userQueuedSongs.Remove(song))
            song.IsUserQueued = false;
    }

    private static int IndexOfBySongPath(IList<Song> list, Song target)
    {
        if (list == null || target == null)
            return -1;
        for (int i = 0; i < list.Count; i++)
        {
            if (SameSongPath(list[i], target))
                return i;
        }
        return -1;
    }

    private Song? FindNaturalNextAfter(Song finished)
    {
        if (contextualSessionOrderedFull == null || finished == null)
            return null;

        int idx = ArtistPlaybackOrder.IndexOfTrackInOrderedList(contextualSessionOrderedFull, finished);
        var hist = ContextualHistoryPathSet();
        int start = idx >= 0 ? idx + 1 : 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = start; i < contextualSessionOrderedFull.Count; i++)
        {
            var t = contextualSessionOrderedFull[i];
            if (t == null || string.IsNullOrWhiteSpace(t.FilePath))
                continue;
            bool inHistory = hist.Contains(t.FilePath);
            bool isInjected = userQueuedSongs.Contains(t);
            if (inHistory && !isInjected)
                continue;
            if (!seen.Add(t.FilePath))
                continue;
            return t;
        }
        return null;
    }

    private bool TryAdvanceContextualSessionMovingFinishedToHistory(out Song? nextTrack)
    {
        nextTrack = null;
        if (!HasContextualPlaybackQueue() || contextualPlaybackFuture == null)
            return false;
        if (contextualPlaybackFuture.Count < 2)
            return false;

        var finished = contextualPlaybackFuture[0];
        if (finished == null)
            return false;

        contextualPlaybackHistoryMru.Insert(0, finished);
        ClearInjectedFlagFor(finished);

        if (titleBarPlayer.IsShuffleEnabled && contextualShuffledFuture.Count > 0)
            contextualShuffledFuture.RemoveAt(0);

        Song? next = titleBarPlayer.IsShuffleEnabled
            ? (contextualShuffledFuture.Count > 0 ? contextualShuffledFuture[0] : null)
            : FindNaturalNextAfter(finished);

        SetActivePlaybackFuture(next);
        nextTrack = next;
        return nextTrack != null;
    }

    private bool TryManualAdvanceContextualSession()
    {
        return TryAdvanceContextualSessionMovingFinishedToHistory(out _);
    }

    private bool TryRewindContextualSessionOne(out Song? trackToPlay)
    {
        trackToPlay = null;
        if (!HasContextualPlaybackQueue() || contextualPlaybackFuture == null)
            return false;
        if (contextualPlaybackHistoryMru.Count == 0)
            return false;

        var prev = contextualPlaybackHistoryMru[0];
        contextualPlaybackHistoryMru.RemoveAt(0);

        if (titleBarPlayer.IsShuffleEnabled)
        {
            int existing = IndexOfBySongPath(contextualShuffledFuture, prev);
            if (existing >= 0)
                contextualShuffledFuture.RemoveAt(existing);
            contextualShuffledFuture.Insert(0, prev);
        }

        SetActivePlaybackFuture(prev);
        trackToPlay = prev;
        return trackToPlay != null;
    }

    private static int FindTrackIndexInPlayQueue(IList<Song> queue, Song track)
    {
        if (queue == null || track == null)
            return -1;

        if (!string.IsNullOrWhiteSpace(track.FilePath))
        {
            for (int i = 0; i < queue.Count; i++)
            {
                var t = queue[i];
                if (t != null && !string.IsNullOrWhiteSpace(t.FilePath) &&
                    string.Equals(t.FilePath, track.FilePath, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }

        for (int i = 0; i < queue.Count; i++)
        {
            if (ReferenceEquals(queue[i], track))
                return i;
        }

        return -1;
    }

    private bool TrySyncPlaybackIndicesFromQueueView(Song track)
    {
        if (track == null)
            return false;

        Song t = track;

        if (queueViewControl == null)
            return false;

        var queue = GetCurrentPlayQueue();
        if (queue == null || queue.Count == 0)
            return false;

        if (queue is not IList<Song> list)
            return false;

        int idx = FindTrackIndexInPlayQueue(list, t);
        if (idx < 0)
            return false;

        if (HasContextualPlaybackQueue())
        {
            RepairContextualFutureHeadToMatchTrack(t);

            if (!HasContextualPlaybackQueue() || contextualPlaybackFuture == null || contextualPlaybackFuture.Count == 0)
                return false;

            var head = contextualPlaybackFuture[0];
            bool headMatches = head != null && SameSongPath(head, t);

            if (!headMatches)
                return false;

            currentTrackIndex = filteredTracks.IndexOf(t);
            currentShuffledIndex = shuffledTracks.IndexOf(t);
            return true;
        }

        if (titleBarPlayer.IsShuffleEnabled)
        {
            currentShuffledIndex = idx;
            currentTrackIndex = filteredTracks.IndexOf(t);
        }
        else
        {
            currentTrackIndex = idx;
            currentShuffledIndex = shuffledTracks.IndexOf(t);
        }

        return true;
    }

    private void RepairContextualFutureHeadToMatchTrack(Song track)
    {
        if (track == null)
            return;

        if (!HasContextualPlaybackQueue() || contextualPlaybackFuture == null)
            return;

        int j = IndexOfBySongPath(contextualPlaybackFuture, track);
        if (j < 0)
        {
            ClearContextualPlaybackQueue();
            return;
        }

        for (int k = 0; k < j; k++)
        {
            var head = contextualPlaybackFuture[0];
            if (head == null) break;

            contextualPlaybackHistoryMru.Insert(0, head);
            if (titleBarPlayer.IsShuffleEnabled && contextualShuffledFuture.Count > 0)
                contextualShuffledFuture.RemoveAt(0);
            contextualPlaybackFuture.RemoveAt(0);

            ClearInjectedFlagFor(head);
        }

        SetActivePlaybackFuture(track);
    }

    private void SyncCurrentTrackIndices(Song track, object? requestSource = null)
    {
        if (track == null)
            return;

        Song t = track;

        if (queueViewControl != null &&
            (ReferenceEquals(requestSource, queueViewControl) || ReferenceEquals(requestSource, queuePopupView)) &&
            TrySyncPlaybackIndicesFromQueueView(t))
            return;

        if (HasContextualPlaybackQueue())
        {
            RepairContextualFutureHeadToMatchTrack(t);

            if (HasContextualPlaybackQueue() &&
                contextualPlaybackFuture != null &&
                contextualPlaybackFuture.Count > 0)
            {
                var head = contextualPlaybackFuture[0];
                bool headMatches = head != null && SameSongPath(head, t);

                if (headMatches)
                {
                    currentTrackIndex = filteredTracks.IndexOf(t);
                    currentShuffledIndex = shuffledTracks.IndexOf(t);
                    return;
                }
            }

            ClearContextualPlaybackQueue();
        }

        currentTrackIndex = filteredTracks.IndexOf(t);
        currentShuffledIndex = shuffledTracks.IndexOf(t);
    }

    /// <summary>
    /// Removes <paramref name="song"/> from <see cref="contextualSessionOrderedFull"/> by path,
    /// skipping the current track. Used to dedupe before injecting a Play Next / Add to Queue.
    /// </summary>
    private void RemoveFromSessionOrderedFullSkippingCurrent(Song song)
    {
        if (contextualSessionOrderedFull == null || song == null) return;
        for (int i = contextualSessionOrderedFull.Count - 1; i >= 0; i--)
        {
            var t = contextualSessionOrderedFull[i];
            if (t == null) continue;
            if (currentTrack != null && SameSongPath(t, currentTrack)) continue;
            if (SameSongPath(t, song))
                contextualSessionOrderedFull.RemoveAt(i);
        }
    }

    private void RemoveFromShuffledFutureSkippingHead(Song song)
    {
        if (song == null) return;
        for (int i = contextualShuffledFuture.Count - 1; i >= 1; i--)
        {
            if (SameSongPath(contextualShuffledFuture[i], song))
                contextualShuffledFuture.RemoveAt(i);
        }
    }

    private void OnQueueTracksReordered(object? sender, (int fromViewIndex, int toViewIndex) e)
    {
        if (e.fromViewIndex < 1)
            return;

        var queue = GetCurrentPlayQueue();
        int baseIdx = GetCurrentTrackIndex();
        if (queue == null || baseIdx < 0 || queue.Count == 0)
            return;

        int fromQ = baseIdx + e.fromViewIndex;
        int toQ = baseIdx + e.toViewIndex;

        if (fromQ < 0 || fromQ >= queue.Count || toQ < 0 || toQ >= queue.Count)
            return;

        if (fromQ == toQ)
            return;

        Song fromTrack = queue[fromQ];
        Song toTrack = queue[toQ];
        if (fromTrack == null || toTrack == null)
            return;

        if (HasContextualPlaybackQueue() && contextualSessionOrderedFull != null)
        {
            if (titleBarPlayer.IsShuffleEnabled &&
                fromQ < contextualShuffledFuture.Count &&
                toQ < contextualShuffledFuture.Count)
            {
                var movedShuffle = contextualShuffledFuture[fromQ];
                contextualShuffledFuture.RemoveAt(fromQ);
                contextualShuffledFuture.Insert(toQ, movedShuffle);
            }

            int absFrom = ArtistPlaybackOrder.IndexOfTrackInOrderedList(contextualSessionOrderedFull, fromTrack);
            int absTo = ArtistPlaybackOrder.IndexOfTrackInOrderedList(contextualSessionOrderedFull, toTrack);
            if (absFrom >= 0 && absTo >= 0)
            {
                var movedNatural = contextualSessionOrderedFull[absFrom];
                contextualSessionOrderedFull.RemoveAt(absFrom);
                if (absTo > absFrom) absTo -= 1;
                contextualSessionOrderedFull.Insert(absTo, movedNatural);
            }

            SetActivePlaybackFuture(currentTrack);
        }
        else
        {
            try
            {
                queue.Move(fromQ, toQ);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnQueueTracksReordered Move: {ex.Message}");
                return;
            }
        }

        UpdateQueueView();
        RefreshVisibleViews();
    }
}
