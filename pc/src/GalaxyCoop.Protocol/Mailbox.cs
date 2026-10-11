using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace GalaxyCoop.Protocol;

/// <summary>
/// Offsets of the in-game mailbox. Mirrors shared/mailbox.h exactly;
/// MailboxLayoutTests parses the header and fails if the two drift apart.
/// </summary>
public static class MailboxLayout
{
    public const uint Magic0 = 0x47434F4F; // 'GCOO'
    public const uint Magic1 = 0x504D4231; // 'PMB1'
    public const uint MailboxVersion = 5;
    public const uint GameSb4e = 0x53423445; // 'SB4E'

    public const int MaxRemote = 7;
    public const int EventRingSize = 32;
    public const int BckNameLen = 32;
    public const int StageNameLen = 36;
    public const int AnimTracks = 4;

    public const int PsSeq = 0x00;
    public const int PsFrame = 0x04;
    public const int PsPos = 0x08;
    public const int PsVel = 0x14;
    public const int PsGravity = 0x20;
    public const int PsUp = 0x2C;
    public const int PsFront = 0x38;
    public const int PsBckFrame = 0x44;
    public const int PsBckRate = 0x48;
    public const int PsFlags = 0x4C;
    public const int PsLife = 0x50;
    public const int PsScenario = 0x54;
    public const int PsCharacter = 0x58;
    public const int PsActive = 0x59;
    public const int PsPlayerId = 0x5A;
    public const int PsBckName = 0x5C;
    public const int PsStage = 0x7C;
    public const int PsTrackWeight = 0xA0;
    public const int PsUpperName = 0xB0;
    public const int PsUpperFrame = 0xD0;
    public const int PsUpperRate = 0xD4;
    public const int PsSize = 0xD8;

    public const int MenuTextLen = 48;
    public const int MenuLines = 4;
    public const int MenuRooms = 8;
    public const int MenuRoomNameLen = 28;
    public const int MenuPlayers = 8;
    public const int MenuPlayerNameLen = 24;

    public const int MrName = 0x00;
    public const int MrPlayers = 0x1C;
    public const int MrMax = 0x1D;
    public const int MrLocked = 0x1E;
    public const int MrSize = 0x20;
    public const int MpName = 0x00;
    public const int MpCharacter = 0x18;
    public const int MpIsHost = 0x19;
    public const int MpIsYou = 0x1A;
    public const int MpSize = 0x1C;

    public const int MnReqSerial = 0x000;
    public const int MnReqCommand = 0x004;
    public const int MnReqArg = 0x008;
    public const int MnCharacter = 0x00C;
    public const int MnOptions = 0x010;
    public const int MnMenuOpen = 0x014;
    public const int MnReqText = 0x018;
    public const int MnReqPassword = 0x048;
    public const int MnStatusSeq = 0x060;
    public const int MnHandledSerial = 0x064;
    public const int MnState = 0x068;
    public const int MnIsHost = 0x06C;
    public const int MnPrefCharacter = 0x070;
    public const int MnPrefOptions = 0x074;
    public const int MnRoomCount = 0x078;
    public const int MnRoomsSerial = 0x07C;
    public const int MnLines = 0x080;
    public const int MnLastAddress = 0x140;
    public const int MnRooms = 0x170;
    public const int MnPlayerCount = 0x270;
    public const int MnPlayers = 0x278;
    public const int MnSize = 0x360;

    public const int NameLen = 16;
    public const int ChatLen = 48;
    public const int ChatLines = 4;
    public const int SoNameSerial = 0x000;
    public const int SoName = 0x004;
    public const int SoChatSerial = 0x014;
    public const int SoChat = 0x018;
    public const int SoInSeq = 0x048;
    public const int SoPreset = 0x04C;
    public const int SoLineCount = 0x05C;
    public const int SoLines = 0x060;
    public const int SoLineSize = 0x040;
    public const int SoNames = 0x160;
    public const int SoSize = 0x1D0;

    public const int BsSeq = 0x00;
    public const int BsKey = 0x04;
    public const int BsPos = 0x08;
    public const int BsRot = 0x14;
    public const int BsActive = 0x20;
    public const int BsSize = 0x24;

    public const int EvId = 0x00;
    public const int EvType = 0x04;
    public const int EvSource = 0x06;
    public const int EvKey = 0x08;
    public const int EvArg0 = 0x0C;
    public const int EvArg1 = 0x10;
    public const int EvPos = 0x14;
    public const int EvPayload = 0x20;
    public const int EvPayloadLen = 16;
    public const int EvSize = 0x30;

    public const int RingHead = 0x00;
    public const int RingTail = 0x04;
    public const int RingEntries = 0x10;
    public const int RingSize = RingEntries + EventRingSize * EvSize;

    public const int TrSerial = 0x00;
    public const int TrScenario = 0x04;
    public const int TrStage = 0x08;
    public const int TrSize = 0x30;

    public const int MbMagic0 = 0x000;
    public const int MbMagic1 = 0x004;
    public const int MbVersion = 0x008;
    public const int MbGame = 0x00C;
    public const int MbSizeField = 0x010;
    public const int MbGameFrame = 0x014;
    public const int MbHeartbeat = 0x018;
    public const int MbSceneSerial = 0x01C;
    public const int MbGameFlags = 0x020;
    public const int MbCompanionFlags = 0x024;
    public const int MbDebug = 0x028;
    public const int MbLocal = 0x040;
    public const int MbRemote = 0x118;
    public const int MbEventsOut = 0x700;
    public const int MbEventsIn = 0xD10;
    public const int MbTravel = 0x1320;
    public const int MbMenu = 0x1350;
    public const int MbBossOut = 0x16B0;
    public const int MbBossIn = 0x16D4;
    public const int MbSocial = 0x16F8;
    public const int MbSize = 0x18C8;

    public static int RemoteSlot(int index) => MbRemote + index * PsSize;
}

[Flags]
public enum PlayerFlags : uint
{
    None = 0,
    OnGround = 0x0001,
    Swimming = 0x0002,
    Dead = 0x0004,
    Hidden = 0x0008,
    InScene = 0x0010,
    Luigi = 0x0020,
    Demo = 0x0040,
    Yoshi = 0x0080,
}

[Flags]
public enum CompanionFlags : uint
{
    None = 0,
    Attached = 0x0001,
    Connected = 0x0002,
    SyncSwitch = 0x0004,
    SyncEnemy = 0x0008,
    ShareStars = 0x0010,
    ForceMario = 0x0020,
    ForceLuigi = 0x0040,
}

public enum GameEventType : ushort
{
    None = 0,
    Coin = 1,
    StarPiece = 2,
    PowerStar = 3,
    EnemyKill = 4,
    SwitchOn = 5,
    SwitchOff = 6,
    PlayerDeath = 7,
    CometOn = 8,
    CometOff = 9,
}

public enum MenuCommand : uint
{
    None = 0,
    HostPrivate = 1,
    HostPublic = 2,
    JoinRoom = 3,
    JoinAddress = 4,
    Leave = 5,
    RefreshRooms = 6,
}

public enum MenuState : uint
{
    Idle = 0,
    Connecting = 1,
    Hosting = 2,
    Joined = 3,
    Error = 4,
}

[Flags]
public enum MenuOptions : uint
{
    None = 0,
    ShareStars = 0x0001,
    SyncEnemies = 0x0002,
    SyncSwitches = 0x0004,
    Valid = 0x8000,
}

public enum DebugCounter
{
    HooksOk = 0,
    EventsOut = 1,
    EventsIn = 2,
    EventsDropped = 3,
    KeyMismatch = 4,
    RemoteSpawned = 5,
}

/// <summary>Big-endian field helpers for game memory.</summary>
public static class BE
{
    public static uint U32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt32BigEndian(b[o..]);
    public static int S32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadInt32BigEndian(b[o..]);
    public static ushort U16(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt16BigEndian(b[o..]);
    public static float F32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadSingleBigEndian(b[o..]);
    public static Vector3 Vec(ReadOnlySpan<byte> b, int o) => new(F32(b, o), F32(b, o + 4), F32(b, o + 8));

    public static void U32(Span<byte> b, int o, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b[o..], v);
    public static void S32(Span<byte> b, int o, int v) => BinaryPrimitives.WriteInt32BigEndian(b[o..], v);
    public static void U16(Span<byte> b, int o, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(b[o..], v);
    public static void F32(Span<byte> b, int o, float v) => BinaryPrimitives.WriteSingleBigEndian(b[o..], v);
    public static void Vec(Span<byte> b, int o, Vector3 v)
    {
        F32(b, o, v.X);
        F32(b, o + 4, v.Y);
        F32(b, o + 8, v.Z);
    }

    public static Vector4 Vec4(ReadOnlySpan<byte> b, int o) => new(F32(b, o), F32(b, o + 4), F32(b, o + 8), F32(b, o + 12));

    public static void Vec4(Span<byte> b, int o, Vector4 v)
    {
        F32(b, o, v.X);
        F32(b, o + 4, v.Y);
        F32(b, o + 8, v.Z);
        F32(b, o + 12, v.W);
    }

    /// <summary>
    /// Game strings are raw bytes (Mario's animation names are Shift-JIS), so they are
    /// carried as Latin-1, which maps every byte to one char and back unchanged.
    /// </summary>
    public static string Str(ReadOnlySpan<byte> b, int o, int len)
    {
        var s = b.Slice(o, len);
        int end = s.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? s : s[..end]);
    }

    public static void Str(Span<byte> b, int o, int len, string? v)
    {
        var dst = b.Slice(o, len);
        dst.Clear();
        if (string.IsNullOrEmpty(v)) return;
        int n = Math.Min(len - 1, v.Length);
        Encoding.Latin1.GetBytes(v.AsSpan(0, n), dst);
    }
}

/// <summary>One player's state as stored in a mailbox slot.</summary>
public sealed record PlayerState
{
    public uint Seq { get; init; }
    public uint Frame { get; init; }
    public Vector3 Pos { get; init; }
    public Vector3 Vel { get; init; }
    public Vector3 Gravity { get; init; }
    public Vector3 Up { get; init; }
    public Vector3 Front { get; init; }
    public float BckFrame { get; init; }
    public float BckRate { get; init; } = 1f;
    public PlayerFlags Flags { get; init; }
    public int Life { get; init; }
    public int Scenario { get; init; }
    public byte Character { get; init; }
    public bool Active { get; init; }
    public byte PlayerId { get; init; }
    public string BckName { get; init; } = "";
    public string Stage { get; init; } = "";
    public Vector4 TrackWeights { get; init; }
    public string UpperName { get; init; } = "";
    public float UpperFrame { get; init; }
    public float UpperRate { get; init; } = 1f;

    public static PlayerState Read(ReadOnlySpan<byte> b) => new()
    {
        Seq = BE.U32(b, MailboxLayout.PsSeq),
        Frame = BE.U32(b, MailboxLayout.PsFrame),
        Pos = BE.Vec(b, MailboxLayout.PsPos),
        Vel = BE.Vec(b, MailboxLayout.PsVel),
        Gravity = BE.Vec(b, MailboxLayout.PsGravity),
        Up = BE.Vec(b, MailboxLayout.PsUp),
        Front = BE.Vec(b, MailboxLayout.PsFront),
        BckFrame = BE.F32(b, MailboxLayout.PsBckFrame),
        BckRate = BE.F32(b, MailboxLayout.PsBckRate),
        Flags = (PlayerFlags)BE.U32(b, MailboxLayout.PsFlags),
        Life = BE.S32(b, MailboxLayout.PsLife),
        Scenario = BE.S32(b, MailboxLayout.PsScenario),
        Character = b[MailboxLayout.PsCharacter],
        Active = b[MailboxLayout.PsActive] != 0,
        PlayerId = b[MailboxLayout.PsPlayerId],
        BckName = BE.Str(b, MailboxLayout.PsBckName, MailboxLayout.BckNameLen),
        Stage = BE.Str(b, MailboxLayout.PsStage, MailboxLayout.StageNameLen),
        TrackWeights = BE.Vec4(b, MailboxLayout.PsTrackWeight),
        UpperName = BE.Str(b, MailboxLayout.PsUpperName, MailboxLayout.BckNameLen),
        UpperFrame = BE.F32(b, MailboxLayout.PsUpperFrame),
        UpperRate = BE.F32(b, MailboxLayout.PsUpperRate),
    };

    /// <summary>Writes the body (everything except Seq, which the seqlock writer owns).</summary>
    public void WriteBody(Span<byte> b)
    {
        BE.U32(b, MailboxLayout.PsFrame, Frame);
        BE.Vec(b, MailboxLayout.PsPos, Pos);
        BE.Vec(b, MailboxLayout.PsVel, Vel);
        BE.Vec(b, MailboxLayout.PsGravity, Gravity);
        BE.Vec(b, MailboxLayout.PsUp, Up);
        BE.Vec(b, MailboxLayout.PsFront, Front);
        BE.F32(b, MailboxLayout.PsBckFrame, BckFrame);
        BE.F32(b, MailboxLayout.PsBckRate, BckRate);
        BE.U32(b, MailboxLayout.PsFlags, (uint)Flags);
        BE.S32(b, MailboxLayout.PsLife, Life);
        BE.S32(b, MailboxLayout.PsScenario, Scenario);
        b[MailboxLayout.PsCharacter] = Character;
        b[MailboxLayout.PsActive] = (byte)(Active ? 1 : 0);
        b[MailboxLayout.PsPlayerId] = PlayerId;
        b[MailboxLayout.PsPlayerId + 1] = 0;
        BE.Str(b, MailboxLayout.PsBckName, MailboxLayout.BckNameLen, BckName);
        BE.Str(b, MailboxLayout.PsStage, MailboxLayout.StageNameLen, Stage);
        BE.Vec4(b, MailboxLayout.PsTrackWeight, TrackWeights);
        BE.Str(b, MailboxLayout.PsUpperName, MailboxLayout.BckNameLen, UpperName);
        BE.F32(b, MailboxLayout.PsUpperFrame, UpperFrame);
        BE.F32(b, MailboxLayout.PsUpperRate, UpperRate);
    }

    public bool InScene => Flags.HasFlag(PlayerFlags.InScene);

    /// <summary>The animation name as the game spells it, for logs.</summary>
    public string BckNameForDisplay => GameText.Decode(BckName);
}

public static class GameText
{
    static readonly Encoding ShiftJis = CreateShiftJis();

    static Encoding CreateShiftJis()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }

    /// <summary>Turns a raw game string (carried as Latin-1) into readable text.</summary>
    public static string Decode(string raw) =>
        raw.All(c => c < 0x80) ? raw : ShiftJis.GetString(Encoding.Latin1.GetBytes(raw));
}

/// <summary>An entry of the mailbox event rings.</summary>
public sealed record GameEvent
{
    public uint Id { get; init; }
    public GameEventType Type { get; init; }
    public byte Source { get; init; }
    public uint Key { get; init; }
    public int Arg0 { get; init; }
    public int Arg1 { get; init; }
    public Vector3 Pos { get; init; }
    public string Payload { get; init; } = "";

    public static GameEvent Read(ReadOnlySpan<byte> b) => new()
    {
        Id = BE.U32(b, MailboxLayout.EvId),
        Type = (GameEventType)BE.U16(b, MailboxLayout.EvType),
        Source = b[MailboxLayout.EvSource],
        Key = BE.U32(b, MailboxLayout.EvKey),
        Arg0 = BE.S32(b, MailboxLayout.EvArg0),
        Arg1 = BE.S32(b, MailboxLayout.EvArg1),
        Pos = BE.Vec(b, MailboxLayout.EvPos),
        Payload = BE.Str(b, MailboxLayout.EvPayload, MailboxLayout.EvPayloadLen),
    };

    public void Write(Span<byte> b)
    {
        b[..MailboxLayout.EvSize].Clear();
        BE.U32(b, MailboxLayout.EvId, Id);
        BE.U16(b, MailboxLayout.EvType, (ushort)Type);
        b[MailboxLayout.EvSource] = Source;
        BE.U32(b, MailboxLayout.EvKey, Key);
        BE.S32(b, MailboxLayout.EvArg0, Arg0);
        BE.S32(b, MailboxLayout.EvArg1, Arg1);
        BE.Vec(b, MailboxLayout.EvPos, Pos);
        BE.Str(b, MailboxLayout.EvPayload, MailboxLayout.EvPayloadLen, Payload);
    }
}
