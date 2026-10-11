using L = GalaxyCoop.Protocol.MailboxLayout;

namespace GalaxyCoop.Protocol;

/// <summary>What the in-game co-op menu asks for, and the player's current choices in it.</summary>
public sealed record MenuRequest
{
    public uint Serial { get; init; }
    public MenuCommand Command { get; init; }
    public uint Arg { get; init; }
    /// <summary>0 keep the save file's character, 1 Mario, 2 Luigi.</summary>
    public uint Character { get; init; }
    public MenuOptions Options { get; init; }
    public bool MenuOpen { get; init; }
    public string Text { get; init; } = "";
    public string Password { get; init; } = "";

    public static MenuRequest Read(ReadOnlySpan<byte> menu) => new()
    {
        Serial = BE.U32(menu, L.MnReqSerial),
        Command = (MenuCommand)BE.U32(menu, L.MnReqCommand),
        Arg = BE.U32(menu, L.MnReqArg),
        Character = BE.U32(menu, L.MnCharacter),
        Options = (MenuOptions)BE.U32(menu, L.MnOptions),
        MenuOpen = BE.U32(menu, L.MnMenuOpen) != 0,
        Text = BE.Str(menu, L.MnReqText, L.MenuTextLen),
        Password = BE.Str(menu, L.MnReqPassword, L.MnStatusSeq - L.MnReqPassword),
    };

    /// <summary>The game's side of the block, as the in-game menu writes it (used by tests and fakes).</summary>
    public void Write(Span<byte> menu)
    {
        BE.U32(menu, L.MnReqCommand, (uint)Command);
        BE.U32(menu, L.MnReqArg, Arg);
        BE.U32(menu, L.MnCharacter, Character);
        BE.U32(menu, L.MnOptions, (uint)Options);
        BE.U32(menu, L.MnMenuOpen, MenuOpen ? 1u : 0);
        BE.Str(menu, L.MnReqText, L.MenuTextLen, Text);
        BE.Str(menu, L.MnReqPassword, L.MnStatusSeq - L.MnReqPassword, Password);
        BE.U32(menu, L.MnReqSerial, Serial);
    }
}

public sealed record MenuRoom(string Name, byte Players, byte MaxPlayers, bool Locked);

/// <summary>0 Mario, 1 Luigi, 0xFF not known yet.</summary>
public sealed record MenuPlayer(string Name, byte Character, bool IsHost, bool IsYou);

/// <summary>What the companion shows in the in-game menu.</summary>
public sealed record MenuStatus
{
    public MenuState State { get; init; }
    public bool IsHost { get; init; }
    public uint PrefCharacter { get; init; }
    public MenuOptions PrefOptions { get; init; }
    public uint RoomsSerial { get; init; }
    /// <summary>Status, invite address, detail and error lines.</summary>
    public string[] Lines { get; init; } = new string[L.MenuLines];
    public string LastAddress { get; init; } = "";
    public MenuRoom[] Rooms { get; init; } = Array.Empty<MenuRoom>();
    public MenuPlayer[] Players { get; init; } = Array.Empty<MenuPlayer>();

    /// <summary>Writes the companion's fields (everything from MnState on); the caller handles statusSeq.</summary>
    public void Write(Span<byte> menu)
    {
        menu[L.MnState..L.MnSize].Clear();
        BE.U32(menu, L.MnState, (uint)State);
        BE.U32(menu, L.MnIsHost, IsHost ? 1u : 0);
        BE.U32(menu, L.MnPrefCharacter, PrefCharacter);
        BE.U32(menu, L.MnPrefOptions, (uint)PrefOptions);
        int rooms = Math.Min(Rooms.Length, L.MenuRooms);
        BE.U32(menu, L.MnRoomCount, (uint)rooms);
        BE.U32(menu, L.MnRoomsSerial, RoomsSerial);
        for (int i = 0; i < L.MenuLines; i++)
            BE.Str(menu, L.MnLines + i * L.MenuTextLen, L.MenuTextLen, MenuText.Ascii(i < Lines.Length ? Lines[i] : null));
        BE.Str(menu, L.MnLastAddress, L.MenuTextLen, LastAddress);
        for (int i = 0; i < rooms; i++)
        {
            int o = L.MnRooms + i * L.MrSize;
            var r = Rooms[i];
            BE.Str(menu, o + L.MrName, L.MenuRoomNameLen, MenuText.Ascii(r.Name));
            menu[o + L.MrPlayers] = r.Players;
            menu[o + L.MrMax] = r.MaxPlayers;
            menu[o + L.MrLocked] = (byte)(r.Locked ? 1 : 0);
        }
        int players = Math.Min(Players.Length, L.MenuPlayers);
        BE.U32(menu, L.MnPlayerCount, (uint)players);
        for (int i = 0; i < players; i++)
        {
            int o = L.MnPlayers + i * L.MpSize;
            var p = Players[i];
            BE.Str(menu, o + L.MpName, L.MenuPlayerNameLen, MenuText.Ascii(p.Name));
            menu[o + L.MpCharacter] = p.Character;
            menu[o + L.MpIsHost] = (byte)(p.IsHost ? 1 : 0);
            menu[o + L.MpIsYou] = (byte)(p.IsYou ? 1 : 0);
        }
    }

    public static MenuStatus Read(ReadOnlySpan<byte> menu)
    {
        var lines = new string[L.MenuLines];
        for (int i = 0; i < lines.Length; i++) lines[i] = BE.Str(menu, L.MnLines + i * L.MenuTextLen, L.MenuTextLen);
        var rooms = new MenuRoom[Math.Min(BE.U32(menu, L.MnRoomCount), L.MenuRooms)];
        for (int i = 0; i < rooms.Length; i++)
        {
            int o = L.MnRooms + i * L.MrSize;
            rooms[i] = new MenuRoom(BE.Str(menu, o + L.MrName, L.MenuRoomNameLen), menu[o + L.MrPlayers], menu[o + L.MrMax], menu[o + L.MrLocked] != 0);
        }
        var players = new MenuPlayer[Math.Min(BE.U32(menu, L.MnPlayerCount), L.MenuPlayers)];
        for (int i = 0; i < players.Length; i++)
        {
            int o = L.MnPlayers + i * L.MpSize;
            players[i] = new MenuPlayer(BE.Str(menu, o + L.MpName, L.MenuPlayerNameLen), menu[o + L.MpCharacter], menu[o + L.MpIsHost] != 0, menu[o + L.MpIsYou] != 0);
        }
        return new MenuStatus
        {
            State = (MenuState)BE.U32(menu, L.MnState),
            IsHost = BE.U32(menu, L.MnIsHost) != 0,
            PrefCharacter = BE.U32(menu, L.MnPrefCharacter),
            PrefOptions = (MenuOptions)BE.U32(menu, L.MnPrefOptions),
            RoomsSerial = BE.U32(menu, L.MnRoomsSerial),
            Lines = lines,
            LastAddress = BE.Str(menu, L.MnLastAddress, L.MenuTextLen),
            Rooms = rooms,
            Players = players,
        };
    }
}

public static class MenuText
{
    /// <summary>The menu font is drawn from Latin-1 bytes; anything else becomes '?'.</summary>
    public static string Ascii(string? text) =>
        text is null ? "" : string.Concat(text.Select(c => c is >= ' ' and <= '\u00FF' ? c : '?'));
}
