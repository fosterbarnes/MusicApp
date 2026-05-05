using System.Text.Json.Serialization;

namespace musicApp;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(LibraryManager.LibraryCache))]
[JsonSerializable(typeof(LibraryManager.RecentlyPlayedCache))]
[JsonSerializable(typeof(LibraryManager.PlaylistsCache))]
[JsonSerializable(typeof(LibraryManager.LibraryFolders))]
internal partial class LibraryJsonContext : JsonSerializerContext { }
