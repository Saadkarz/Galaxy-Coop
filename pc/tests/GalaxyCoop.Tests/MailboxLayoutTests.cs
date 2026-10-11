using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using GalaxyCoop.Protocol;

namespace GalaxyCoop.Tests;

public class MailboxLayoutTests
{
    internal static string RepoRoot()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "shared", "mailbox.h")))
                dir = dir.Parent;
            if (dir != null) return dir.FullName;
        }
        throw new InvalidOperationException("repo root not found");
    }

    static Dictionary<string, long> ParseHeaderDefines()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot(), "shared", "mailbox.h"));
        var result = new Dictionary<string, long>();
        foreach (Match m in Regex.Matches(text, @"^#define\s+GCOOP_(\w+)\s+(0x[0-9A-Fa-f]+|\d+)\b", RegexOptions.Multiline))
        {
            var v = m.Groups[2].Value;
            result[m.Groups[1].Value] = v.StartsWith("0x")
                ? long.Parse(v[2..], NumberStyles.HexNumber)
                : long.Parse(v);
        }
        return result;
    }

    static string Pascal(string snake) =>
        string.Concat(snake.Split('_').Select(t => t.Length == 0 ? t : char.ToUpperInvariant(t[0]) + t[1..].ToLowerInvariant()));

    [Fact]
    public void EveryHeaderDefineMatchesTheCSharpMirror()
    {
        var layout = typeof(MailboxLayout).GetFields(BindingFlags.Public | BindingFlags.Static)
            .ToDictionary(f => f.Name, f => Convert.ToInt64(f.GetValue(null)));
        var problems = new List<string>();

        foreach (var (name, value) in ParseHeaderDefines())
        {
            long? actual = null;
            if (name.StartsWith("PF_"))
                actual = TryEnum<PlayerFlags>(Pascal(name[3..]));
            else if (name.StartsWith("CF_"))
                actual = TryEnum<CompanionFlags>(Pascal(name[3..]));
            else if (name.StartsWith("DBG_"))
                actual = TryEnum<DebugCounter>(Pascal(name[4..]));
            else if (name.StartsWith("MCMD_"))
                actual = TryEnum<MenuCommand>(Pascal(name[5..]));
            else if (name.StartsWith("MST_"))
                actual = TryEnum<MenuState>(Pascal(name[4..]));
            else if (name.StartsWith("MOPT_"))
                actual = TryEnum<MenuOptions>(Pascal(name[5..]));
            else if (layout.TryGetValue(Pascal(name), out var l))
                actual = l;
            else if (name.StartsWith("EV_"))
                actual = TryEnum<GameEventType>(Pascal(name[3..]));

            if (actual is null)
                problems.Add($"GCOOP_{name} has no C# counterpart");
            else if (actual != value)
                problems.Add($"GCOOP_{name}: header 0x{value:X}, C# 0x{actual:X}");
        }

        Assert.Empty(problems);
    }

    static long? TryEnum<T>(string name) where T : struct, Enum =>
        Enum.TryParse<T>(name, out var v) ? Convert.ToInt64(v) : null;

    [Fact]
    public void DerivedSizesAreConsistent()
    {
        Assert.Equal(MailboxLayout.PsStage + MailboxLayout.StageNameLen, MailboxLayout.PsTrackWeight);
        Assert.Equal(MailboxLayout.PsTrackWeight + MailboxLayout.AnimTracks * 4, MailboxLayout.PsUpperName);
        Assert.Equal(MailboxLayout.PsUpperName + MailboxLayout.BckNameLen, MailboxLayout.PsUpperFrame);
        Assert.Equal(MailboxLayout.PsUpperRate + 4, MailboxLayout.PsSize);
        Assert.Equal(MailboxLayout.MnLines + MailboxLayout.MenuLines * MailboxLayout.MenuTextLen, MailboxLayout.MnLastAddress);
        Assert.Equal(MailboxLayout.MnRooms + MailboxLayout.MenuRooms * MailboxLayout.MrSize, MailboxLayout.MnPlayerCount);
        Assert.True(MailboxLayout.MnPlayers + MailboxLayout.MenuPlayers * MailboxLayout.MpSize <= MailboxLayout.MnSize);
        Assert.Equal(MailboxLayout.EvPayload + MailboxLayout.EvPayloadLen, MailboxLayout.EvSize);
        Assert.Equal(MailboxLayout.MbLocal + MailboxLayout.PsSize, MailboxLayout.MbRemote);
        Assert.Equal(MailboxLayout.MbRemote + MailboxLayout.MaxRemote * MailboxLayout.PsSize, MailboxLayout.MbEventsOut);
        Assert.Equal(MailboxLayout.MbEventsOut + MailboxLayout.RingSize, MailboxLayout.MbEventsIn);
        Assert.Equal(MailboxLayout.MbEventsIn + MailboxLayout.RingSize, MailboxLayout.MbTravel);
        Assert.Equal(MailboxLayout.MbTravel + MailboxLayout.TrSize, MailboxLayout.MbMenu);
        Assert.Equal(MailboxLayout.MbMenu + MailboxLayout.MnSize, MailboxLayout.MbBossOut);
        Assert.Equal(MailboxLayout.MbBossOut + MailboxLayout.BsSize, MailboxLayout.MbBossIn);
        Assert.Equal(MailboxLayout.MbBossIn + MailboxLayout.BsSize, MailboxLayout.MbSocial);
        Assert.Equal(MailboxLayout.MbSocial + MailboxLayout.SoSize, MailboxLayout.MbSize);
        Assert.Equal(MailboxLayout.SoChat + MailboxLayout.ChatLen, MailboxLayout.SoInSeq);
        Assert.Equal(MailboxLayout.SoPreset + MailboxLayout.NameLen, MailboxLayout.SoLineCount);
        Assert.Equal(MailboxLayout.SoLines + MailboxLayout.ChatLines * MailboxLayout.SoLineSize, MailboxLayout.SoNames);
        Assert.Equal(MailboxLayout.SoNames + MailboxLayout.MaxRemote * MailboxLayout.NameLen, MailboxLayout.SoSize);
        Assert.True(MailboxLayout.TrStage + MailboxLayout.StageNameLen <= MailboxLayout.TrSize);
    }

    [Fact]
    public void PlayerStateRoundTripsBigEndian()
    {
        var s = new PlayerState
        {
            Frame = 1234, Pos = new Vector3(1, -2, 3.5f), Vel = new Vector3(0.25f, 0, -9),
            Gravity = new Vector3(0, -1, 0), Up = new Vector3(0, 1, 0), Front = new Vector3(0, 0, 1),
            Flags = PlayerFlags.InScene | PlayerFlags.OnGround, Life = 3, Scenario = 2, Character = 1,
            Active = true, PlayerId = 4, BckName = "Run", Stage = "IslandFleetGalaxy",
            BckFrame = 12.5f, BckRate = 1.25f, TrackWeights = new Vector4(0.25f, 0.75f, 0, 0),
            UpperName = "Spin", UpperFrame = 3.5f, UpperRate = 2f,
        };
        var buf = new byte[MailboxLayout.PsSize];
        s.WriteBody(buf);
        Assert.Equal(new byte[] { 0x3F, 0x80, 0x00, 0x00 }, buf[MailboxLayout.PsPos..(MailboxLayout.PsPos + 4)]);
        Assert.Equal(s, PlayerState.Read(buf));
    }

    [Fact]
    public void ShiftJisAnimationNamesSurviveTheRoundTrip()
    {
        var sjis = new byte[] { 0x8A, 0xEE, 0x96, 0x7B }; // 基本
        var buf = new byte[MailboxLayout.PsSize];
        sjis.CopyTo(buf, MailboxLayout.PsBckName);
        var s = PlayerState.Read(buf);

        var again = new byte[MailboxLayout.PsSize];
        s.WriteBody(again);
        Assert.Equal(sjis, again[MailboxLayout.PsBckName..(MailboxLayout.PsBckName + 4)]);
        Assert.Equal("基本", s.BckNameForDisplay);
    }

    [Fact]
    public void MenuBlockRoundTrips()
    {
        var buf = new byte[MailboxLayout.MnSize];
        var request = new MenuRequest
        {
            Serial = 3, Command = MenuCommand.JoinAddress, Arg = 1, Character = 2,
            Options = MenuOptions.Valid | MenuOptions.ShareStars, MenuOpen = true, Text = "192.168.1.20:7777", Password = "luma",
        };
        request.Write(buf);
        var status = new MenuStatus
        {
            State = MenuState.Hosting, IsHost = true, PrefCharacter = 1, PrefOptions = MenuOptions.Valid | MenuOptions.SyncEnemies,
            RoomsSerial = 9, Lines = new[] { "Online", "Invite 1.2.3.4:7777", "", "" }, LastAddress = "1.2.3.4:7777",
            Rooms = new[] { new MenuRoom("Fahd's galaxy", 1, 4, true) },
            Players = new[] { new MenuPlayer("Fahd", 1, true, true), new MenuPlayer("Bro", 0, false, false) },
        };
        status.Write(buf);

        Assert.Equal(request, MenuRequest.Read(buf));
        var back = MenuStatus.Read(buf);
        Assert.Equal(status.Lines, back.Lines);
        Assert.Equal(status.Rooms, back.Rooms);
        Assert.Equal(status.Players, back.Players);
        Assert.Equal(status with { Lines = back.Lines, Rooms = back.Rooms, Players = back.Players }, back);
    }

    [Fact]
    public void EventRoundTrips()
    {
        var e = new GameEvent { Id = 7, Type = GameEventType.Coin, Source = 2, Key = 0xDEADBEEF, Arg0 = -1, Arg1 = 5, Pos = new Vector3(1, 2, 3), Payload = "RedBlueExGalaxy" };
        var buf = new byte[MailboxLayout.EvSize];
        e.Write(buf);
        Assert.Equal(e, GameEvent.Read(buf));
    }
}
