/*
 * Galaxy Co-op mailbox: the only interface between the in-game module and the
 * PC companion. The companion finds it by scanning emulated RAM for the magic
 * words, so the layout below is a wire format: every field is big-endian and
 * at a fixed offset. pc/src/GalaxyCoop.Protocol/Mailbox.cs mirrors these
 * offsets and a unit test checks both files agree, so change them together
 * and bump GCOOP_MAILBOX_VERSION.
 */
#pragma once

#define GCOOP_MAGIC0 0x47434F4F /* 'GCOO' */
#define GCOOP_MAGIC1 0x504D4231 /* 'PMB1' */
#define GCOOP_MAILBOX_VERSION 5
#define GCOOP_GAME_SB4E 0x53423445 /* 'SB4E' */

#define GCOOP_MAX_REMOTE 7
#define GCOOP_EVENT_RING_SIZE 32
#define GCOOP_BCK_NAME_LEN 32 /* raw bytes: Mario's animation groups have Shift-JIS names */
#define GCOOP_STAGE_NAME_LEN 36
#define GCOOP_ANIM_TRACKS 4

/* Player state flags */
#define GCOOP_PF_ON_GROUND  0x0001
#define GCOOP_PF_SWIMMING   0x0002
#define GCOOP_PF_DEAD       0x0004
#define GCOOP_PF_HIDDEN     0x0008
#define GCOOP_PF_IN_SCENE   0x0010
#define GCOOP_PF_LUIGI      0x0020
#define GCOOP_PF_DEMO       0x0040
#define GCOOP_PF_YOSHI      0x0080 /* riding Yoshi; the ghost draws a Yoshi under the player */

/* Header flags written by the companion */
#define GCOOP_CF_ATTACHED     0x0001
#define GCOOP_CF_CONNECTED    0x0002
#define GCOOP_CF_SYNC_SWITCH  0x0004
#define GCOOP_CF_SYNC_ENEMY   0x0008
#define GCOOP_CF_SHARE_STARS  0x0010 /* a Power Star collected by anyone is collected by everyone */
#define GCOOP_CF_FORCE_MARIO  0x0020 /* play as Mario regardless of the save file */
#define GCOOP_CF_FORCE_LUIGI  0x0040 /* play as Luigi regardless of the save file */

/* Event types */
#define GCOOP_EV_NONE          0
#define GCOOP_EV_COIN          1
#define GCOOP_EV_STAR_PIECE    2
#define GCOOP_EV_POWER_STAR    3
#define GCOOP_EV_ENEMY_KILL    4
#define GCOOP_EV_SWITCH_ON     5
#define GCOOP_EV_SWITCH_OFF    6
#define GCOOP_EV_PLAYER_DEATH  7
#define GCOOP_EV_COMET_ON      8 /* a comet star is active in this galaxy */
#define GCOOP_EV_COMET_OFF     9

/* PlayerState offsets (size 0xD8). bckName is the animation group and
 * bckFrame/bckRate/trackWeight describe how it is playing. upperName is the
 * upper-body animation layered on top (empty when none). */
#define GCOOP_PS_SEQ        0x00
#define GCOOP_PS_FRAME      0x04
#define GCOOP_PS_POS        0x08
#define GCOOP_PS_VEL        0x14
#define GCOOP_PS_GRAVITY    0x20
#define GCOOP_PS_UP         0x2C
#define GCOOP_PS_FRONT      0x38
#define GCOOP_PS_BCK_FRAME  0x44
#define GCOOP_PS_BCK_RATE   0x48
#define GCOOP_PS_FLAGS      0x4C
#define GCOOP_PS_LIFE       0x50
#define GCOOP_PS_SCENARIO   0x54
#define GCOOP_PS_CHARACTER  0x58
#define GCOOP_PS_ACTIVE     0x59
#define GCOOP_PS_PLAYER_ID  0x5A
#define GCOOP_PS_BCK_NAME   0x5C
#define GCOOP_PS_STAGE      0x7C
#define GCOOP_PS_TRACK_WEIGHT 0xA0 /* f32[4] */
#define GCOOP_PS_UPPER_NAME 0xB0
#define GCOOP_PS_UPPER_FRAME 0xD0
#define GCOOP_PS_UPPER_RATE 0xD4
#define GCOOP_PS_SIZE       0xD8

/* Event offsets (size 0x30) */
#define GCOOP_EV_ID         0x00
#define GCOOP_EV_TYPE       0x04
#define GCOOP_EV_SOURCE     0x06
#define GCOOP_EV_KEY        0x08
#define GCOOP_EV_ARG0       0x0C
#define GCOOP_EV_ARG1       0x10
#define GCOOP_EV_POS        0x14
#define GCOOP_EV_PAYLOAD    0x20
#define GCOOP_EV_PAYLOAD_LEN 16
#define GCOOP_EV_SIZE       0x30

/* EventRing offsets: head is written only by the producer, tail only by the consumer */
#define GCOOP_RING_HEAD     0x00
#define GCOOP_RING_TAIL     0x04
#define GCOOP_RING_ENTRIES  0x10
#define GCOOP_RING_SIZE     (GCOOP_RING_ENTRIES + GCOOP_EVENT_RING_SIZE * GCOOP_EV_SIZE)

/* Travel request offsets (companion -> game). The companion writes the stage
 * and scenario, then increments serial; the game travels when serial changes. */
#define GCOOP_TR_SERIAL     0x00
#define GCOOP_TR_SCENARIO   0x04
#define GCOOP_TR_STAGE      0x08
#define GCOOP_TR_SIZE       0x30

/*
 * In-game co-op menu. The game writes requests (serial last) and its current
 * choices; the companion answers by bumping handledSerial and keeps the status
 * part up to date under statusSeq (odd while writing). Strings are Latin-1.
 */
#define GCOOP_MENU_TEXT_LEN     48
#define GCOOP_MENU_LINES        4
#define GCOOP_MENU_ROOMS        8
#define GCOOP_MENU_ROOM_NAME_LEN 28
#define GCOOP_MENU_PLAYERS      8
#define GCOOP_MENU_PLAYER_NAME_LEN 24

/* Requests */
#define GCOOP_MCMD_NONE          0
#define GCOOP_MCMD_HOST_PRIVATE  1
#define GCOOP_MCMD_HOST_PUBLIC   2
#define GCOOP_MCMD_JOIN_ROOM     3 /* arg: room index */
#define GCOOP_MCMD_JOIN_ADDRESS  4 /* text: address */
#define GCOOP_MCMD_LEAVE         5
#define GCOOP_MCMD_REFRESH_ROOMS 6

/* Session state reported by the companion */
#define GCOOP_MST_IDLE       0
#define GCOOP_MST_CONNECTING 1
#define GCOOP_MST_HOSTING    2
#define GCOOP_MST_JOINED     3
#define GCOOP_MST_ERROR      4

/* Menu option bits (game choices and companion defaults) */
#define GCOOP_MOPT_SHARE_STARS  0x0001
#define GCOOP_MOPT_SYNC_ENEMIES 0x0002
#define GCOOP_MOPT_SYNC_SWITCHES 0x0004
#define GCOOP_MOPT_VALID        0x8000

/* Menu room entry (size 0x20) and player entry (size 0x1C) */
#define GCOOP_MR_NAME       0x00
#define GCOOP_MR_PLAYERS    0x1C
#define GCOOP_MR_MAX        0x1D
#define GCOOP_MR_LOCKED     0x1E
#define GCOOP_MR_SIZE       0x20
#define GCOOP_MP_NAME       0x00
#define GCOOP_MP_CHARACTER  0x18 /* 0 Mario, 1 Luigi, 0xFF unknown */
#define GCOOP_MP_IS_HOST    0x19
#define GCOOP_MP_IS_YOU     0x1A
#define GCOOP_MP_SIZE       0x1C

/* Menu block offsets (size 0x360). character: 0 keep the save, 1 Mario, 2 Luigi. */
#define GCOOP_MN_REQ_SERIAL     0x000
#define GCOOP_MN_REQ_COMMAND    0x004
#define GCOOP_MN_REQ_ARG        0x008
#define GCOOP_MN_CHARACTER      0x00C
#define GCOOP_MN_OPTIONS        0x010
#define GCOOP_MN_MENU_OPEN      0x014
#define GCOOP_MN_REQ_TEXT       0x018
#define GCOOP_MN_REQ_PASSWORD   0x048
#define GCOOP_MN_STATUS_SEQ     0x060
#define GCOOP_MN_HANDLED_SERIAL 0x064
#define GCOOP_MN_STATE          0x068
#define GCOOP_MN_IS_HOST        0x06C
#define GCOOP_MN_PREF_CHARACTER 0x070
#define GCOOP_MN_PREF_OPTIONS   0x074
#define GCOOP_MN_ROOM_COUNT     0x078
#define GCOOP_MN_ROOMS_SERIAL   0x07C
#define GCOOP_MN_LINES          0x080 /* char[4][48]: status, invite, detail, error */
#define GCOOP_MN_LAST_ADDRESS   0x140
#define GCOOP_MN_ROOMS          0x170
#define GCOOP_MN_PLAYER_COUNT   0x270
#define GCOOP_MN_PLAYERS        0x278
#define GCOOP_MN_SIZE           0x360

/* Names and chat. The game writes the name and outgoing chat (serial last);
 * the companion writes the preset name, the chat log and remote name tags
 * under inSeq. Strings are Latin-1. */
#define GCOOP_NAME_LEN          16
#define GCOOP_CHAT_LEN          48
#define GCOOP_CHAT_LINES        4
#define GCOOP_SO_NAME_SERIAL    0x000
#define GCOOP_SO_NAME           0x004
#define GCOOP_SO_CHAT_SERIAL    0x014
#define GCOOP_SO_CHAT           0x018
#define GCOOP_SO_IN_SEQ         0x048
#define GCOOP_SO_PRESET         0x04C
#define GCOOP_SO_LINE_COUNT     0x05C
#define GCOOP_SO_LINES          0x060 /* GCOOP_CHAT_LINES entries, name then text */
#define GCOOP_SO_LINE_SIZE      0x040 /* name 16 + text 48 */
#define GCOOP_SO_NAMES          0x160 /* remote name tags, one per mailbox slot */
#define GCOOP_SO_SIZE           0x1D0

/* One boss the local player is fighting, and the boss a remote player reported.
 * seq is a seqlock on each block. rot is the LiveActor euler rotation. */
#define GCOOP_BS_SEQ            0x00
#define GCOOP_BS_KEY            0x04
#define GCOOP_BS_POS            0x08
#define GCOOP_BS_ROT            0x14
#define GCOOP_BS_ACTIVE         0x20
#define GCOOP_BS_SIZE           0x24

/* Mailbox offsets */
#define GCOOP_MB_MAGIC0         0x000
#define GCOOP_MB_MAGIC1         0x004
#define GCOOP_MB_VERSION        0x008
#define GCOOP_MB_GAME           0x00C
#define GCOOP_MB_SIZE_FIELD     0x010
#define GCOOP_MB_GAME_FRAME     0x014
#define GCOOP_MB_HEARTBEAT      0x018
#define GCOOP_MB_SCENE_SERIAL   0x01C
#define GCOOP_MB_GAME_FLAGS     0x020
#define GCOOP_MB_COMPANION_FLAGS 0x024
#define GCOOP_MB_DEBUG          0x028 /* u32[6] */
#define GCOOP_MB_LOCAL          0x040
#define GCOOP_MB_REMOTE         0x118
#define GCOOP_MB_EVENTS_OUT     0x700
#define GCOOP_MB_EVENTS_IN      0xD10
#define GCOOP_MB_TRAVEL         0x1320
#define GCOOP_MB_MENU           0x1350
#define GCOOP_MB_BOSS_OUT       0x16B0
#define GCOOP_MB_BOSS_IN        0x16D4
#define GCOOP_MB_SOCIAL         0x16F8
#define GCOOP_MB_SIZE           0x18C8

/* Debug counter slots */
#define GCOOP_DBG_HOOKS_OK      0
#define GCOOP_DBG_EVENTS_OUT    1
#define GCOOP_DBG_EVENTS_IN     2
#define GCOOP_DBG_EVENTS_DROPPED 3
#define GCOOP_DBG_KEY_MISMATCH  4
#define GCOOP_DBG_REMOTE_SPAWNED 5

#if defined(__MWERKS__) || defined(SYATICLANGD)

typedef struct GCoopPlayerState {
    volatile u32 seq;
    u32 frame;
    f32 pos[3];
    f32 vel[3];
    f32 gravity[3];
    f32 up[3];
    f32 front[3];
    f32 bckFrame;
    f32 bckRate;
    u32 flags;
    s32 life;
    s32 scenario;
    u8 character;
    u8 active;
    u8 playerId;
    u8 _pad5B;
    char bckName[GCOOP_BCK_NAME_LEN];
    char stage[GCOOP_STAGE_NAME_LEN];
    f32 trackWeight[GCOOP_ANIM_TRACKS];
    char upperName[GCOOP_BCK_NAME_LEN];
    f32 upperFrame;
    f32 upperRate;
} GCoopPlayerState;

typedef struct GCoopEvent {
    u32 id;
    u16 type;
    u8 source;
    u8 _pad07;
    u32 key;
    s32 arg0;
    s32 arg1;
    f32 pos[3];
    char payload[GCOOP_EV_PAYLOAD_LEN];
} GCoopEvent;

typedef struct GCoopEventRing {
    volatile u32 head;
    volatile u32 tail;
    u32 _pad[2];
    GCoopEvent entries[GCOOP_EVENT_RING_SIZE];
} GCoopEventRing;

typedef struct GCoopTravelRequest {
    volatile u32 serial;
    s32 scenario;
    char stage[GCOOP_STAGE_NAME_LEN];
    u32 _pad;
} GCoopTravelRequest;

typedef struct GCoopMenuRoom {
    char name[GCOOP_MENU_ROOM_NAME_LEN];
    u8 players;
    u8 maxPlayers;
    u8 locked;
    u8 _pad;
} GCoopMenuRoom;

typedef struct GCoopMenuPlayer {
    char name[GCOOP_MENU_PLAYER_NAME_LEN];
    u8 character;
    u8 isHost;
    u8 isYou;
    u8 _pad;
} GCoopMenuPlayer;

typedef struct GCoopMenu {
    volatile u32 reqSerial;
    u32 reqCommand;
    u32 reqArg;
    u32 character;
    u32 options;
    u32 menuOpen;
    char reqText[GCOOP_MENU_TEXT_LEN];
    char reqPassword[24];
    volatile u32 statusSeq;
    volatile u32 handledSerial;
    u32 state;
    u32 isHost;
    u32 prefCharacter;
    u32 prefOptions;
    u32 roomCount;
    u32 roomsSerial;
    char lines[GCOOP_MENU_LINES][GCOOP_MENU_TEXT_LEN];
    char lastAddress[GCOOP_MENU_TEXT_LEN];
    GCoopMenuRoom rooms[GCOOP_MENU_ROOMS];
    u32 playerCount;
    u32 _pad274;
    GCoopMenuPlayer players[GCOOP_MENU_PLAYERS];
    u8 _pad358[8];
} GCoopMenu;

typedef struct GCoopBoss {
    volatile u32 seq;
    u32 key;
    f32 pos[3];
    f32 rot[3];
    u8 active;
    u8 _pad21[3];
} GCoopBoss;

typedef struct GCoopChatLine {
    char name[GCOOP_NAME_LEN];
    char text[GCOOP_CHAT_LEN];
} GCoopChatLine;

typedef struct GCoopSocial {
    volatile u32 nameSerial;
    char name[GCOOP_NAME_LEN];
    volatile u32 chatSerial;
    char chat[GCOOP_CHAT_LEN];
    volatile u32 inSeq;
    char preset[GCOOP_NAME_LEN];
    u32 lineCount;
    GCoopChatLine lines[GCOOP_CHAT_LINES];
    char remoteNames[GCOOP_MAX_REMOTE][GCOOP_NAME_LEN];
} GCoopSocial;

typedef struct GCoopMailbox {
    u32 magic0;
    u32 magic1;
    u32 version;
    u32 game;
    u32 size;
    volatile u32 gameFrame;
    volatile u32 heartbeat;
    u32 sceneSerial;
    u32 gameFlags;
    volatile u32 companionFlags;
    u32 debug[6];
    GCoopPlayerState local;
    GCoopPlayerState remote[GCOOP_MAX_REMOTE];
    GCoopEventRing eventsOut;
    GCoopEventRing eventsIn;
    GCoopTravelRequest travel;
    GCoopMenu menu;
    GCoopBoss bossOut;
    GCoopBoss bossIn;
    GCoopSocial social;
} GCoopMailbox;

#define GCOOP_OFFSETOF(type, member) ((u32)&(((type *)0)->member))
#define GCOOP_STATIC_ASSERT(name, cond) typedef char gcoop_assert_##name[(cond) ? 1 : -1]

GCOOP_STATIC_ASSERT(ps_size, sizeof(GCoopPlayerState) == GCOOP_PS_SIZE);
GCOOP_STATIC_ASSERT(ps_bck, GCOOP_OFFSETOF(GCoopPlayerState, bckName) == GCOOP_PS_BCK_NAME);
GCOOP_STATIC_ASSERT(ps_stage, GCOOP_OFFSETOF(GCoopPlayerState, stage) == GCOOP_PS_STAGE);
GCOOP_STATIC_ASSERT(ps_char, GCOOP_OFFSETOF(GCoopPlayerState, character) == GCOOP_PS_CHARACTER);
GCOOP_STATIC_ASSERT(ps_weight, GCOOP_OFFSETOF(GCoopPlayerState, trackWeight) == GCOOP_PS_TRACK_WEIGHT);
GCOOP_STATIC_ASSERT(ev_size, sizeof(GCoopEvent) == GCOOP_EV_SIZE);
GCOOP_STATIC_ASSERT(ev_payload, GCOOP_OFFSETOF(GCoopEvent, payload) == GCOOP_EV_PAYLOAD);
GCOOP_STATIC_ASSERT(ring_size, sizeof(GCoopEventRing) == GCOOP_RING_SIZE);
GCOOP_STATIC_ASSERT(mb_local, GCOOP_OFFSETOF(GCoopMailbox, local) == GCOOP_MB_LOCAL);
GCOOP_STATIC_ASSERT(mb_remote, GCOOP_OFFSETOF(GCoopMailbox, remote) == GCOOP_MB_REMOTE);
GCOOP_STATIC_ASSERT(mb_out, GCOOP_OFFSETOF(GCoopMailbox, eventsOut) == GCOOP_MB_EVENTS_OUT);
GCOOP_STATIC_ASSERT(mb_in, GCOOP_OFFSETOF(GCoopMailbox, eventsIn) == GCOOP_MB_EVENTS_IN);
GCOOP_STATIC_ASSERT(tr_size, sizeof(GCoopTravelRequest) == GCOOP_TR_SIZE);
GCOOP_STATIC_ASSERT(tr_stage, GCOOP_OFFSETOF(GCoopTravelRequest, stage) == GCOOP_TR_STAGE);
GCOOP_STATIC_ASSERT(mb_travel, GCOOP_OFFSETOF(GCoopMailbox, travel) == GCOOP_MB_TRAVEL);
GCOOP_STATIC_ASSERT(ps_upper, GCOOP_OFFSETOF(GCoopPlayerState, upperName) == GCOOP_PS_UPPER_NAME);
GCOOP_STATIC_ASSERT(mr_size, sizeof(GCoopMenuRoom) == GCOOP_MR_SIZE);
GCOOP_STATIC_ASSERT(mp_size, sizeof(GCoopMenuPlayer) == GCOOP_MP_SIZE);
GCOOP_STATIC_ASSERT(mn_status, GCOOP_OFFSETOF(GCoopMenu, statusSeq) == GCOOP_MN_STATUS_SEQ);
GCOOP_STATIC_ASSERT(mn_lines, GCOOP_OFFSETOF(GCoopMenu, lines) == GCOOP_MN_LINES);
GCOOP_STATIC_ASSERT(mn_rooms, GCOOP_OFFSETOF(GCoopMenu, rooms) == GCOOP_MN_ROOMS);
GCOOP_STATIC_ASSERT(mn_players, GCOOP_OFFSETOF(GCoopMenu, players) == GCOOP_MN_PLAYERS);
GCOOP_STATIC_ASSERT(mn_size, sizeof(GCoopMenu) == GCOOP_MN_SIZE);
GCOOP_STATIC_ASSERT(mb_menu, GCOOP_OFFSETOF(GCoopMailbox, menu) == GCOOP_MB_MENU);
GCOOP_STATIC_ASSERT(bs_size, sizeof(GCoopBoss) == GCOOP_BS_SIZE);
GCOOP_STATIC_ASSERT(bs_rot, GCOOP_OFFSETOF(GCoopBoss, rot) == GCOOP_BS_ROT);
GCOOP_STATIC_ASSERT(chat_size, sizeof(GCoopChatLine) == GCOOP_SO_LINE_SIZE);
GCOOP_STATIC_ASSERT(so_name, GCOOP_OFFSETOF(GCoopSocial, name) == GCOOP_SO_NAME);
GCOOP_STATIC_ASSERT(so_chat, GCOOP_OFFSETOF(GCoopSocial, chat) == GCOOP_SO_CHAT);
GCOOP_STATIC_ASSERT(so_in, GCOOP_OFFSETOF(GCoopSocial, inSeq) == GCOOP_SO_IN_SEQ);
GCOOP_STATIC_ASSERT(so_preset, GCOOP_OFFSETOF(GCoopSocial, preset) == GCOOP_SO_PRESET);
GCOOP_STATIC_ASSERT(so_lines, GCOOP_OFFSETOF(GCoopSocial, lines) == GCOOP_SO_LINES);
GCOOP_STATIC_ASSERT(so_names, GCOOP_OFFSETOF(GCoopSocial, remoteNames) == GCOOP_SO_NAMES);
GCOOP_STATIC_ASSERT(so_size, sizeof(GCoopSocial) == GCOOP_SO_SIZE);
GCOOP_STATIC_ASSERT(mb_boss, GCOOP_OFFSETOF(GCoopMailbox, bossOut) == GCOOP_MB_BOSS_OUT);
GCOOP_STATIC_ASSERT(mb_social, GCOOP_OFFSETOF(GCoopMailbox, social) == GCOOP_MB_SOCIAL);
GCOOP_STATIC_ASSERT(mb_size, sizeof(GCoopMailbox) == GCOOP_MB_SIZE);

#endif
