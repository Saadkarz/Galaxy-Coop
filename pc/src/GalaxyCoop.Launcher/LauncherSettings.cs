using System.Text.Json;
using GalaxyCoop.Protocol.Net;

namespace GalaxyCoop.Launcher;

/// <summary>Everything the launcher remembers between runs (%APPDATA%\GalaxyCoop\launcher.json).</summary>
public sealed record LauncherSettings
{
    public string Name { get; init; } = Environment.UserName;
    /// <summary>"mario" or "luigi".</summary>
    public string Character { get; init; } = "mario";
    public string DolphinDir { get; init; } = "";
    public string GameImage { get; init; } = "";
    public string Lobby { get; init; } = $"127.0.0.1:{Codec.DefaultLobbyPort}";
    public int Port { get; init; } = Codec.DefaultPort;
    public string RoomName { get; init; } = "";
    public int MaxPlayers { get; init; } = 4;
    public bool ShareStars { get; init; } = true;
    public bool SyncEnemies { get; init; } = true;
    public bool SyncSwitches { get; init; } = true;
    public string LastJoinAddress { get; init; } = "";
    public bool LaunchDolphin { get; init; } = true;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GalaxyCoop", "launcher.json");

    public static LauncherSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), Json) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Console.Error.WriteLine($"ignoring unreadable settings: {ex.Message}");
        }
        return new();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }

    public string EffectiveRoomName => string.IsNullOrWhiteSpace(RoomName) ? $"{Name}'s galaxy" : RoomName.Trim();
}
