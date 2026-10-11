using System.Diagnostics;
using System.Net.Sockets;
using GalaxyCoop.Companion;
using GalaxyCoop.Protocol;
using GalaxyCoop.Protocol.Net;

namespace GalaxyCoop.Launcher;

/// <summary>
/// The launcher without a page: starts Dolphin with the patch, stays linked to
/// the game for as long as it runs, and answers the in-game co-op menu through
/// the mailbox (host, join, leave, room list, status and player list).
/// </summary>
public sealed class InGameLauncher : IAsyncDisposable
{
    const int LoopMs = 8;
    const int StatusIntervalMs = 100;

    readonly LauncherSettingsStore _store;
    readonly DolphinSetup.Environment _env;
    readonly GameLink _link;
    readonly Process? _dolphin;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    PlaySession? _session;
    bool _handledKnown;
    uint _handled;
    uint _seenNameSerial;
    uint _seenChatSerial;
    uint _seenCharacter;
    MenuOptions _seenOptions;
    LobbyRoom[] _rooms = Array.Empty<LobbyRoom>();
    uint _roomsSerial;
    Task? _roomQuery;
    string _error = "";
    long _lastStatus;
    bool _statusDirty = true;

    public event Action<string>? Log;

    InGameLauncher(LauncherSettingsStore store, DolphinSetup.Environment env, Process? dolphin, GameLink link)
    {
        _store = store;
        _env = env;
        _dolphin = dolphin;
        _link = link;
        _link.Status += s => Log?.Invoke($"[game] {s}");
        _link.Attached += () => _handledKnown = false;
    }

    /// <summary>For tests: a game that is already running behind <paramref name="link"/>.</summary>
    internal InGameLauncher(LauncherSettingsStore store, GameLink link)
        : this(store, new DolphinSetup.Environment(null, null, null, null, Array.Empty<string>()), null, link) { }

    public static InGameLauncher Start(LauncherSettingsStore store)
    {
        var settings = store.Current;
        var env = DolphinSetup.Inspect(settings);
        Process? dolphin = null;
        if (settings.LaunchDolphin)
        {
            if (!env.Ready) throw new InvalidOperationException(string.Join(" ", env.Problems));
            dolphin = DolphinSetup.Launch(env);
        }
        return new InGameLauncher(store, env, dolphin, new GameLink(dolphin?.Id));
    }

    public int? DolphinPid => _dolphin?.Id;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_dolphin is { HasExited: true })
            {
                Log?.Invoke("[game] Dolphin closed");
                break;
            }
            await StepAsync(ct);
            try { await Task.Delay(LoopMs, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    internal async Task StepAsync(CancellationToken ct)
    {
        var snap = _link.Poll();
        if (snap is not null) NoteSocial(_link.Bridge!.ReadSocial());
        if (_session is { } session)
        {
            session.PresetName = _store.Current.Name;
            session.Pump(snap);
            await DropRejectedSessionAsync();
        }
        else if (snap is not null)
        {
            var bridge = _link.Bridge!;
            bridge.WriteHeartbeat(CompanionFlags.Attached | SyncFlags(CurrentOptions(snap.Menu)));
            bridge.WriteSocialIn(_store.Current.Name, Array.Empty<Chat>(), Array.Empty<string?>());
        }

        if (snap?.Menu is not { } menu || !_link.IsReady) return;
        await HandleMenuAsync(menu, ct);
        long now = _clock.ElapsedMilliseconds;
        if (_statusDirty || now - _lastStatus >= StatusIntervalMs)
        {
            _statusDirty = false;
            _lastStatus = now;
            _link.Bridge!.WriteMenuStatus(BuildStatus(), _handled);
        }
    }

    void NoteSocial(MailboxBridge.SocialOut social)
    {
        if (social.NameSerial != 0 && social.NameSerial != _seenNameSerial)
        {
            _seenNameSerial = social.NameSerial;
            var name = social.Name.Trim();
            if (name.Length > 0 && name != _store.Current.Name)
            {
                _store.Update(_store.Current with { Name = name });
                _session?.Rename(name);
                Log?.Invoke($"[menu] name is {name}");
            }
        }
        if (social.ChatSerial != 0 && social.ChatSerial != _seenChatSerial)
        {
            _seenChatSerial = social.ChatSerial;
            if (social.Chat.Length > 0)
            {
                _session?.SendChat(social.Chat);
                Log?.Invoke($"[chat] {social.Chat}");
            }
        }
    }

    async Task HandleMenuAsync(MenuRequest menu, CancellationToken ct)
    {
        RememberChoices(menu);
        if (!_handledKnown)
        {
            // Requests made before we attached were meant for an earlier companion.
            _handledKnown = true;
            _handled = menu.Serial;
            return;
        }
        if (menu.Serial == _handled) return;
        _handled = menu.Serial;
        _statusDirty = true;
        try
        {
            await ExecuteAsync(menu, ct);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or SocketException)
        {
            _error = ex.Message;
            Log?.Invoke($"[menu] {ex.Message}");
        }
    }

    async Task ExecuteAsync(MenuRequest menu, CancellationToken ct)
    {
        var settings = _store.Current;
        var flags = SyncFlags(CurrentOptions(menu));
        switch (menu.Command)
        {
            case MenuCommand.HostPrivate:
            case MenuCommand.HostPublic:
            {
                RequireNoSession();
                bool isPublic = menu.Command == MenuCommand.HostPublic;
                Log?.Invoke($"[menu] host a {(isPublic ? "public" : "private")} game");
                _error = "";
                StartSession(PlaySession.Host(settings, _env,
                    new HostRequest(isPublic ? "public" : "private", settings.RoomName, "", settings.MaxPlayers, settings.Port), _link, flags));
                break;
            }
            case MenuCommand.JoinRoom:
            {
                RequireNoSession();
                if (menu.Arg >= _rooms.Length) throw new InvalidOperationException("That game is gone. Refresh the list.");
                var room = _rooms[menu.Arg];
                Log?.Invoke($"[menu] join '{room.Name}' at {room.Address}:{room.Port}");
                _error = "";
                StartSession(PlaySession.Join(settings, _env, new JoinRequest($"{room.Address}:{room.Port}", menu.Password, room.Name), _link, flags));
                break;
            }
            case MenuCommand.JoinAddress:
            {
                RequireNoSession();
                var address = menu.Text.Trim();
                if (address.Length == 0) throw new ArgumentException("Enter the host's address, like 203.0.113.7:7777");
                Log?.Invoke($"[menu] join {address}");
                _error = "";
                StartSession(PlaySession.Join(settings, _env, new JoinRequest(address, menu.Password), _link, flags));
                _store.Update(_store.Current with { LastJoinAddress = address });
                break;
            }
            case MenuCommand.Leave:
                Log?.Invoke("[menu] leave the game");
                _error = "";
                await StopSessionAsync();
                break;
            case MenuCommand.RefreshRooms:
                if (_roomQuery is { IsCompleted: false }) break;
                _roomQuery = RefreshRoomsAsync(settings.Lobby, ct);
                break;
        }
    }

    void RequireNoSession()
    {
        if (_session is not null) throw new InvalidOperationException("Leave the current game first.");
    }

    void StartSession(PlaySession session)
    {
        _session = session;
    }

    async Task StopSessionAsync()
    {
        if (_session is not { } s) return;
        _session = null;
        await s.DisposeAsync();
    }

    /// <summary>A server that turned us away (wrong password, full, wrong game) ends the attempt, keeping the reason.</summary>
    async Task DropRejectedSessionAsync()
    {
        if (_session is not { } s || s.GetView().Status != "rejected") return;
        _error = s.LastNetStatus.Length > 0 ? s.LastNetStatus : "The server turned us away.";
        Log?.Invoke($"[menu] {_error}");
        await StopSessionAsync();
    }

    async Task RefreshRoomsAsync(string lobby, CancellationToken ct)
    {
        try
        {
            var (rooms, error) = await LobbyBrowser.QueryAsync(lobby, "SB4E", ct);
            _rooms = rooms.Take(MailboxLayout.MenuRooms).ToArray();
            _error = error is null ? "" : $"No answer from the lobby ({lobby}).";
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or SocketException)
        {
            _rooms = Array.Empty<LobbyRoom>();
            _error = $"Bad lobby address '{lobby}'.";
        }
        _roomsSerial++;
        _statusDirty = true;
    }

    /// <summary>Keeps the launcher settings in step with the choices made in the game.</summary>
    void RememberChoices(MenuRequest menu)
    {
        if (menu.Character is 1 or 2 && menu.Character != _seenCharacter)
        {
            _seenCharacter = menu.Character;
            var character = menu.Character == 2 ? "luigi" : "mario";
            if (_store.Current.Character != character) _store.Update(_store.Current with { Character = character });
        }
        if (menu.Options.HasFlag(MenuOptions.Valid) && menu.Options != _seenOptions)
        {
            _seenOptions = menu.Options;
            var s = _store.Current;
            var next = s with
            {
                ShareStars = menu.Options.HasFlag(MenuOptions.ShareStars),
                SyncEnemies = menu.Options.HasFlag(MenuOptions.SyncEnemies),
                SyncSwitches = menu.Options.HasFlag(MenuOptions.SyncSwitches),
            };
            if (next != s) _store.Update(next);
        }
    }

    MenuOptions CurrentOptions(MenuRequest? menu) =>
        menu is { } m && m.Options.HasFlag(MenuOptions.Valid) ? m.Options : PreferredOptions(_store.Current);

    static MenuOptions PreferredOptions(LauncherSettings s) =>
        MenuOptions.Valid
        | (s.ShareStars ? MenuOptions.ShareStars : 0)
        | (s.SyncEnemies ? MenuOptions.SyncEnemies : 0)
        | (s.SyncSwitches ? MenuOptions.SyncSwitches : 0);

    static CompanionFlags SyncFlags(MenuOptions options) =>
        (options.HasFlag(MenuOptions.ShareStars) ? CompanionFlags.ShareStars : 0)
        | (options.HasFlag(MenuOptions.SyncEnemies) ? CompanionFlags.SyncEnemy : 0)
        | (options.HasFlag(MenuOptions.SyncSwitches) ? CompanionFlags.SyncSwitch : 0);

    MenuStatus BuildStatus()
    {
        var settings = _store.Current;
        var lines = new string[MailboxLayout.MenuLines];
        var state = MenuState.Idle;
        bool isHost = false;
        MenuPlayer[] players = Array.Empty<MenuPlayer>();

        if (_session is { } s)
        {
            var v = s.GetView();
            isHost = v.Role == "host";
            state = v.Status switch
            {
                "online" => isHost ? MenuState.Hosting : MenuState.Joined,
                "rejected" => MenuState.Error,
                _ => MenuState.Connecting,
            };
            if (isHost)
            {
                lines[0] = state == MenuState.Connecting ? "Starting the server..." : $"Hosting a {v.Visibility} game";
                lines[1] = v.PublicAddress is { } pub ? $"Invite: {pub}" : $"Invite (same network): {v.LanAddress}";
                lines[2] = v.PublicAddress is null ? v.PortNote ?? "Checking the router..." : $"Same network: {v.LanAddress}";
            }
            else
            {
                lines[0] = state == MenuState.Connecting ? $"Connecting to {v.RoomName}..." : v.StatusText;
                lines[1] = $"Game: {v.RoomName}";
                lines[2] = "";
            }
            players = v.Players.Select(p => new MenuPlayer(p.Name,
                p.Character switch { "Mario" => (byte)0, "Luigi" => (byte)1, _ => (byte)0xFF }, p.IsHost, p.IsYou)).ToArray();
        }
        else
        {
            lines[0] = $"Not in a co-op game. You are {settings.Name}.";
            lines[1] = "";
            lines[2] = _roomQuery is { IsCompleted: false } ? "Searching for public games..." : "";
        }
        lines[3] = _error;

        return new MenuStatus
        {
            State = state,
            IsHost = isHost,
            PrefCharacter = settings.Character == "luigi" ? 2u : 1u,
            PrefOptions = PreferredOptions(settings),
            RoomsSerial = _roomsSerial,
            Lines = lines,
            LastAddress = settings.LastJoinAddress,
            Rooms = _rooms.Select(r => new MenuRoom(r.Name, r.Players, r.MaxPlayers, r.HasPassword)).ToArray(),
            Players = players,
        };
    }

    public async ValueTask DisposeAsync()
    {
        await StopSessionAsync();
        _link.Dispose();
    }
}
