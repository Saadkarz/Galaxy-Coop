#include "GCoop/GCoop.h"
#include "Game/Util/JMapIdInfo.h"

/*
 * The in-game co-op menu: pick Mario or Luigi, host a private or public game,
 * browse public games or join by address, and see who is playing. It opens by
 * itself the first time the companion is running in a galaxy, and with the -
 * button afterwards. The game only draws and collects choices; the companion
 * does the networking and answers through gMailbox.menu.
 *
 * It is drawn by a NameObj in the scene's layout pass with its own orthographic
 * projection (restored afterwards): flat GX quads for the panel and the menu
 * font through nw4r's TextWriter for the text.
 */

extern "C" {
    void GXGetProjectionv(f32 *pOut);
}

#define MENU_MAX_ITEMS     16
#define MENU_SCREEN_H      480.0f
#define MENU_PANEL_W       520.0f
#define MENU_LINE_H        24.0f
#define MENU_TEXT_H        20.0f
#define MENU_TITLE_H       26.0f
#define MENU_TAG_H         18.0f
#define MENU_HINT_FRAMES   360
#define MENU_ALIVE_FRAMES  120
#define MENU_CHAT_LIFE     600
#define MENU_REPEAT_DELAY  18
#define MENU_REPEAT_EVERY  4
#define MENU_PASSWORD_LEN  24
#define MENU_TEXT_CAP      80

/* MessageFont colours, close to the galaxy pause screen: navy fill, gold rim, cyan inset. */
#define COLOR_NAVY      0x0C1848F0
#define COLOR_GOLD      0xF0C040FF
#define COLOR_CYAN      0x78E8FFFF
#define COLOR_CURSOR    0x183888E8
#define COLOR_TEXT      0xFFFFFFFF
#define COLOR_DIM       0xC8D4ECFF
#define COLOR_TITLE     0xFFE878FF
#define COLOR_ERROR     0xFF8080FF
#define COLOR_GOOD      0x98FFA8FF
#define COLOR_TAG       0x081028D0

namespace {
    typedef nw4r::ut::TextWriterBase<wchar_t> Writer;

    enum Page { PAGE_MAIN, PAGE_ROOMS, PAGE_EDIT };
    enum ItemId {
        ITEM_CHARACTER, ITEM_NAME, ITEM_HOST_PRIVATE, ITEM_HOST_PUBLIC, ITEM_BROWSE, ITEM_JOIN_ADDRESS,
        ITEM_OPT_STARS, ITEM_OPT_ENEMIES, ITEM_OPT_SWITCHES, ITEM_CHAT, ITEM_LEAVE, ITEM_CLOSE,
        ITEM_REFRESH, ITEM_BACK, ITEM_ROOM
    };
    enum EditTarget { EDIT_ADDRESS, EDIT_ADDRESS_PASSWORD, EDIT_ROOM_PASSWORD, EDIT_NAME, EDIT_CHAT };
    enum Pad { PAD_UP, PAD_DOWN, PAD_LEFT, PAD_RIGHT, PAD_COUNT };

    const char sCharset[] = " 0123456789.:-_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    struct Item {
        u16 mId;
        bool mEnabled;
        char mText[GCOOP_MENU_TEXT_LEN];
    };

    bool sOpen = false;
    bool sApplyCharacter = false;
    bool sAutoOpened = false;
    bool sChoicesReady = false;
    bool sStatusValid = false;
    Page sPage = PAGE_MAIN;
    s32 sCursor = 0;
    s32 sMainCursor = 0;
    Item sItems[MENU_MAX_ITEMS];
    s32 sItemCount = 0;
    GCoopMenu sStatus;
    GCoopMenu sStatusRead;
    u32 sLastHeartbeat = 0;
    s32 sHeartbeatAge = MENU_ALIVE_FRAMES;
    u32 sCharacter = 1;
    u32 sOptions = GCOOP_MOPT_SHARE_STARS | GCOOP_MOPT_SYNC_ENEMIES | GCOOP_MOPT_SYNC_SWITCHES | GCOOP_MOPT_VALID;
    s32 sHintFrames = 0;
    s32 sPadHeld[PAD_COUNT];
    u32 sSentSerial = 0;
    u32 sRoomsSerialSeen = 0;

    EditTarget sEditTarget = EDIT_ADDRESS;
    char sEditBuf[GCOOP_MENU_TEXT_LEN];
    s32 sEditPos = 0;
    s32 sEditMax = GCOOP_MENU_TEXT_LEN - 1;
    char sAddress[GCOOP_MENU_TEXT_LEN];
    u32 sJoinRoom = 0;

    /* Name and chat. The companion fills the preset and the incoming lines; we publish a name or a line by bumping its serial. */
    char sLocalName[GCOOP_NAME_LEN];
    u32 sNameSerial = 0;
    bool sNameEdit = false;
    struct ShownChat {
        char mName[GCOOP_NAME_LEN];
        char mText[GCOOP_CHAT_LEN];
        s32 mAge;
    };
    ShownChat sShown[GCOOP_CHAT_LINES];
    s32 sShownCount = 0;
    /* Lines we have already displayed, including ones that scrolled off, so a repeated log does not cycle. */
    ShownChat sSeen[8];
    s32 sSeenCount = 0;
    char sRemoteNames[GCOOP_MAX_REMOTE][GCOOP_NAME_LEN];

    bool companionAlive() {
        return sHeartbeatAge < MENU_ALIVE_FRAMES;
    }

    bool inSession() {
        u32 state = sStatus.state;
        return sStatusValid && (state == GCOOP_MST_CONNECTING || state == GCOOP_MST_HOSTING || state == GCOOP_MST_JOINED);
    }

    bool waitingForCompanion() {
        return sSentSerial != 0 && sStatus.handledSerial != sSentSerial;
    }

    void readStatus() {
        GCoopMenu &rMenu = GCoop::gMailbox.menu;
        u32 seq = rMenu.statusSeq;
        if (seq & 1) {
            return;
        }
        __sync();
        memcpy((u8 *)&sStatusRead + GCOOP_MN_STATUS_SEQ, (const u8 *)&rMenu + GCOOP_MN_STATUS_SEQ, GCOOP_MN_SIZE - GCOOP_MN_STATUS_SEQ);
        __sync();
        if (rMenu.statusSeq != seq || seq == 0) {
            return;
        }
        memcpy(&sStatus, &sStatusRead, sizeof(GCoopMenu));
        for (s32 i = 0; i < GCOOP_MENU_LINES; i++) {
            sStatus.lines[i][GCOOP_MENU_TEXT_LEN - 1] = 0;
        }
        sStatus.lastAddress[GCOOP_MENU_TEXT_LEN - 1] = 0;
        if (sStatus.roomCount > GCOOP_MENU_ROOMS) {
            sStatus.roomCount = GCOOP_MENU_ROOMS;
        }
        if (sStatus.playerCount > GCOOP_MENU_PLAYERS) {
            sStatus.playerCount = GCOOP_MENU_PLAYERS;
        }
        sStatusValid = true;
    }

    void publishChoices() {
        GCoopMenu &rMenu = GCoop::gMailbox.menu;
        rMenu.character = sCharacter;
        rMenu.options = sOptions;
    }

    /* Starts from the companion's saved preferences the first time it answers. */
    void adoptPreferences() {
        if (sChoicesReady || !sStatusValid) {
            return;
        }
        sChoicesReady = true;
        GCoopMenu &rMenu = GCoop::gMailbox.menu;
        if (rMenu.character == 1 || rMenu.character == 2) {
            sCharacter = rMenu.character;
        }
        else if (sStatus.prefCharacter == 1 || sStatus.prefCharacter == 2) {
            sCharacter = sStatus.prefCharacter;
        }
        else {
            sCharacter = MR::isPlayerLuigi() ? 2 : 1;
        }
        if (rMenu.options & GCOOP_MOPT_VALID) {
            sOptions = rMenu.options;
        }
        else if (sStatus.prefOptions & GCOOP_MOPT_VALID) {
            sOptions = sStatus.prefOptions;
        }
        GCoop::copyName(sAddress, sStatus.lastAddress, sizeof(sAddress));
        publishChoices();
    }

    void sendRequest(u32 command, u32 arg, const char *pText, const char *pPassword) {
        GCoopMenu &rMenu = GCoop::gMailbox.menu;
        publishChoices();
        rMenu.reqCommand = command;
        rMenu.reqArg = arg;
        GCoop::copyName(rMenu.reqText, pText, GCOOP_MENU_TEXT_LEN);
        GCoop::copyName(rMenu.reqPassword, pPassword, MENU_PASSWORD_LEN);
        __sync();
        u32 serial = rMenu.reqSerial + 1;
        if (serial == 0) {
            serial = 1;
        }
        rMenu.reqSerial = serial;
        sSentSerial = serial;
    }

    bool canOpen() {
        return !MR::isDemoActive() && !MR::isPlayerDead() && !MR::isStageStateScenarioOpeningCamera();
    }

    /* Host, join and character live in a real galaxy (the ship counts). The title and file select only get a name card. */
    bool isFullMenuScene() {
        if (MR::isStageFileSelect() || MR::isStageStoryBook() || MR::isStageStaffRoll()) {
            return false;
        }
        const char *pStage = MR::getCurrentStageName();
        if (!pStage || strcmp(pStage, "PeachCastleGalaxy") == 0) {
            return false;
        }
        return strstr(pStage, "Galaxy") != 0 || MR::isStageWorldMap();
    }

    bool isNameCardScene() {
        return !isFullMenuScene() && !MR::isStageStoryBook() && !MR::isStageStaffRoll()
            && !(MR::getCurrentStageName() && strcmp(MR::getCurrentStageName(), "PeachCastleGalaxy") == 0);
    }

    void openMenu() {
        sOpen = true;
        sNameEdit = false;
        sPage = PAGE_MAIN;
        sCursor = 0;
        for (s32 i = 0; i < PAD_COUNT; i++) {
            sPadHeld[i] = -1;
        }
        if (isFullMenuScene()) {
            MR::offPlayerControl();
            GCoop::gMailbox.menu.menuOpen = 1;
        }
    }

    void closeMenu() {
        sOpen = false;
        sApplyCharacter = isFullMenuScene();
        GCoop::gMailbox.menu.menuOpen = 0;
        if (isFullMenuScene()) {
            MR::onPlayerControl(true);
        }
        publishChoices();
    }

    /* The character is fixed per scene, so a new choice reloads the stage the player is in. */
    void applyCharacter() {
        sApplyCharacter = false;
        if (!isFullMenuScene()) {
            return;
        }
        bool wantLuigi = sCharacter == 2;
        if (wantLuigi != MR::isPlayerLuigi()) {
            const TVec3f *pPos = MR::getPlayerPos();
            TVec3f pos;
            pos.set(pPos ? pPos->x : 0.0f, pPos ? pPos->y : 0.0f, pPos ? pPos->z : 0.0f);
            GCOOP_INFO("character changed to %s, reloading %s\n", wantLuigi ? "Luigi" : "Mario", MR::getCurrentStageName());
            GCoop::setSceneCharacter(sCharacter);
            publishChoices();
            GCoop::beginCharacterReload(pos);
        }
    }

    /* Press, then auto-repeat while held. */
    bool padRepeat(s32 pad, bool held) {
        if (!held) {
            sPadHeld[pad] = 0;
            return false;
        }
        if (sPadHeld[pad] < 0) {
            return false; /* still held from before the menu opened */
        }
        s32 t = sPadHeld[pad]++;
        return t == 0 || (t >= MENU_REPEAT_DELAY && (t - MENU_REPEAT_DELAY) % MENU_REPEAT_EVERY == 0);
    }

    void addItem(u16 id, bool enabled, const char *pFormat, ...) {
        if (sItemCount >= MENU_MAX_ITEMS) {
            return;
        }
        Item &rItem = sItems[sItemCount++];
        rItem.mId = id;
        rItem.mEnabled = enabled;
        va_list args;
        va_start(args, pFormat);
        vsnprintf(rItem.mText, sizeof(rItem.mText), pFormat, args);
        va_end(args);
    }

    const char *onOff(u32 bit) {
        return (sOptions & bit) ? "On" : "Off";
    }

    void buildItems() {
        sItemCount = 0;
        bool ready = companionAlive() && sStatusValid && !waitingForCompanion();
        if (sPage == PAGE_MAIN) {
            addItem(ITEM_CHARACTER, true, "Character:  %s", sCharacter == 2 ? "Luigi" : "Mario");
            addItem(ITEM_NAME, true, "Name:  %s", sLocalName[0] ? sLocalName : "...");
            if (inSession()) {
                addItem(ITEM_LEAVE, ready, sStatus.isHost ? "Stop hosting" : "Leave the game");
            }
            else {
                addItem(ITEM_HOST_PRIVATE, ready, "Host a private game");
                addItem(ITEM_HOST_PUBLIC, ready, "Host a public game");
                addItem(ITEM_BROWSE, ready, "Join a public game");
                addItem(ITEM_JOIN_ADDRESS, ready, "Join by address");
                addItem(ITEM_OPT_STARS, true, "Shared Power Stars:  %s", onOff(GCOOP_MOPT_SHARE_STARS));
                addItem(ITEM_OPT_ENEMIES, true, "Shared enemy defeats:  %s", onOff(GCOOP_MOPT_SYNC_ENEMIES));
                addItem(ITEM_OPT_SWITCHES, true, "Shared switches:  %s", onOff(GCOOP_MOPT_SYNC_SWITCHES));
            }
            addItem(ITEM_CHAT, companionAlive(), "Send a message");
            addItem(ITEM_CLOSE, true, "Close");
        }
        else if (sPage == PAGE_ROOMS) {
            for (u32 i = 0; i < sStatus.roomCount; i++) {
                const GCoopMenuRoom &rRoom = sStatus.rooms[i];
                char name[GCOOP_MENU_ROOM_NAME_LEN];
                GCoop::copyName(name, rRoom.name, sizeof(name));
                addItem(ITEM_ROOM + i, ready, "%s   %d/%d%s", name, rRoom.players, rRoom.maxPlayers, rRoom.locked ? "   (password)" : "");
            }
            addItem(ITEM_REFRESH, ready, "Refresh");
            addItem(ITEM_BACK, true, "Back");
        }
        if (sCursor >= sItemCount) {
            sCursor = sItemCount - 1;
        }
        if (sCursor < 0) {
            sCursor = 0;
        }
    }

    void startEdit(EditTarget target, const char *pInitial, s32 maxLen) {
        sPage = PAGE_EDIT;
        sEditTarget = target;
        sEditMax = maxLen;
        GCoop::copyName(sEditBuf, pInitial, sizeof(sEditBuf));
        sEditBuf[maxLen] = 0;
        sEditPos = strlen(sEditBuf);
        if (sEditPos >= maxLen) {
            sEditPos = maxLen - 1;
        }
    }

    void trimEdit(char *pOut, u32 size) {
        const char *p = sEditBuf;
        while (*p == ' ') {
            p++;
        }
        GCoop::copyName(pOut, p, size);
        s32 len = strlen(pOut);
        while (len > 0 && pOut[len - 1] == ' ') {
            pOut[--len] = 0;
        }
    }

    void publishName();
    void publishChat(const char *pText);
    void rememberChat(const char *pName, const char *pText);

    void finishEdit() {
        char text[GCOOP_MENU_TEXT_LEN];
        trimEdit(text, sizeof(text));
        switch (sEditTarget) {
        case EDIT_ADDRESS:
            if (text[0] == 0) {
                return;
            }
            GCoop::copyName(sAddress, text, sizeof(sAddress));
            startEdit(EDIT_ADDRESS_PASSWORD, "", MENU_PASSWORD_LEN - 1);
            return;
        case EDIT_ADDRESS_PASSWORD:
            sendRequest(GCOOP_MCMD_JOIN_ADDRESS, 0, sAddress, text);
            break;
        case EDIT_ROOM_PASSWORD:
            sendRequest(GCOOP_MCMD_JOIN_ROOM, sJoinRoom, "", text);
            break;
        case EDIT_NAME:
            GCoop::copyName(sLocalName, text, GCOOP_NAME_LEN);
            publishName();
            sNameEdit = false;
            break;
        case EDIT_CHAT:
            if (text[0]) {
                publishChat(text);
            }
            break;
        }
        sPage = PAGE_MAIN;
        sCursor = 0;
    }

    void publishName() {
        GCoopSocial &rSocial = GCoop::gMailbox.social;
        GCoop::copyName(rSocial.name, sLocalName, GCOOP_NAME_LEN);
        __sync();
        u32 serial = rSocial.nameSerial + 1;
        if (serial == 0) {
            serial = 1;
        }
        rSocial.nameSerial = serial;
        sNameSerial = serial;
    }

    void publishChat(const char *pText) {
        GCoopSocial &rSocial = GCoop::gMailbox.social;
        GCoop::copyName(rSocial.chat, pText, GCOOP_CHAT_LEN);
        __sync();
        u32 serial = rSocial.chatSerial + 1;
        if (serial == 0) {
            serial = 1;
        }
        rSocial.chatSerial = serial;
        rememberChat(sLocalName[0] ? sLocalName : "you", pText);
    }

    bool chatSeen(const char *pName, const char *pText) {
        for (s32 i = 0; i < sSeenCount; i++) {
            if (strcmp(sSeen[i].mText, pText) == 0 && strcmp(sSeen[i].mName, pName) == 0) {
                return true;
            }
        }
        return false;
    }

    void rememberChat(const char *pName, const char *pText) {
        if (!pText || !pText[0] || chatSeen(pName, pText)) {
            return;
        }
        if (sSeenCount == (s32)(sizeof(sSeen) / sizeof(sSeen[0]))) {
            for (s32 i = 1; i < sSeenCount; i++) {
                sSeen[i - 1] = sSeen[i];
            }
            sSeenCount--;
        }
        GCoop::copyName(sSeen[sSeenCount].mName, pName, GCOOP_NAME_LEN);
        GCoop::copyName(sSeen[sSeenCount].mText, pText, GCOOP_CHAT_LEN);
        sSeenCount++;
        if (sShownCount == GCOOP_CHAT_LINES) {
            for (s32 i = 1; i < GCOOP_CHAT_LINES; i++) {
                sShown[i - 1] = sShown[i];
            }
            sShownCount--;
        }
        ShownChat &rLine = sShown[sShownCount++];
        GCoop::copyName(rLine.mName, pName, GCOOP_NAME_LEN);
        GCoop::copyName(rLine.mText, pText, GCOOP_CHAT_LEN);
        rLine.mAge = 0;
    }

    /* The saved name arrives as a preset. Copy it once; a cleared name stays cleared. */
    void readSocialIn() {
        GCoopSocial &rSocial = GCoop::gMailbox.social;
        u32 seq = rSocial.inSeq;
        if ((seq & 1) || seq == 0) {
            return;
        }
        __sync();
        char preset[GCOOP_NAME_LEN];
        memcpy(preset, rSocial.preset, GCOOP_NAME_LEN);
        u32 count = rSocial.lineCount;
        GCoopChatLine lines[GCOOP_CHAT_LINES];
        memcpy(lines, rSocial.lines, sizeof(lines));
        char names[GCOOP_MAX_REMOTE][GCOOP_NAME_LEN];
        memcpy(names, rSocial.remoteNames, sizeof(names));
        __sync();
        if (rSocial.inSeq != seq) {
            return;
        }
        preset[GCOOP_NAME_LEN - 1] = 0;
        if (sNameSerial == 0 && sLocalName[0] == 0 && preset[0]) {
            GCoop::copyName(sLocalName, preset, GCOOP_NAME_LEN);
        }
        if (count > GCOOP_CHAT_LINES) {
            count = GCOOP_CHAT_LINES;
        }
        for (u32 i = 0; i < count; i++) {
            lines[i].name[GCOOP_NAME_LEN - 1] = 0;
            lines[i].text[GCOOP_CHAT_LEN - 1] = 0;
            rememberChat(lines[i].name, lines[i].text);
        }
        memcpy(sRemoteNames, names, sizeof(sRemoteNames));
        for (s32 i = 0; i < GCOOP_MAX_REMOTE; i++) {
            sRemoteNames[i][GCOOP_NAME_LEN - 1] = 0;
        }
    }

    s32 charsetIndex(char c) {
        for (s32 i = 0; sCharset[i]; i++) {
            if (sCharset[i] == c) {
                return i;
            }
        }
        return 0;
    }

    void updateEdit(bool up, bool down, bool left, bool right, bool confirmWithA) {
        s32 count = sizeof(sCharset) - 1;
        s32 len = strlen(sEditBuf);
        if (up || down) {
            if (sEditPos >= len) {
                sEditBuf[sEditPos] = ' ';
                sEditBuf[sEditPos + 1] = 0;
            }
            s32 idx = charsetIndex(sEditBuf[sEditPos]) + (up ? 1 : -1);
            sEditBuf[sEditPos] = sCharset[(idx + count) % count];
        }
        if (left && sEditPos > 0) {
            sEditPos--;
        }
        if (right && sEditPos < len && sEditPos + 1 < sEditMax) {
            sEditPos++;
        }
        if (MR::testCorePadTrigger1(0) && len > 0) {
            /* delete the character under the cursor */
            for (s32 i = sEditPos; i < len; i++) {
                sEditBuf[i] = sEditBuf[i + 1];
            }
            if (sEditPos > 0 && sEditPos >= (s32)strlen(sEditBuf)) {
                sEditPos--;
            }
        }
        if (confirmWithA && MR::testCorePadTriggerA(0)) {
            finishEdit();
        }
        else if (MR::testCorePadTriggerB(0)) {
            if (sEditTarget == EDIT_NAME && sNameEdit) {
                sNameEdit = false;
                sPage = PAGE_MAIN;
            }
            else {
                sPage = sEditTarget == EDIT_ROOM_PASSWORD ? PAGE_ROOMS : PAGE_MAIN;
            }
        }
    }

    void toggleOption(u32 bit) {
        sOptions = (sOptions ^ bit) | GCOOP_MOPT_VALID;
        publishChoices();
    }

    void activate(const Item &rItem, s32 direction) {
        if (!rItem.mEnabled) {
            return;
        }
        u16 id = rItem.mId;
        switch (id) {
        case ITEM_CHARACTER:
            sCharacter = sCharacter == 2 ? 1 : 2;
            publishChoices();
            return;
        case ITEM_NAME:
            startEdit(EDIT_NAME, sLocalName, GCOOP_NAME_LEN - 1);
            return;
        case ITEM_OPT_STARS:
            toggleOption(GCOOP_MOPT_SHARE_STARS);
            return;
        case ITEM_OPT_ENEMIES:
            toggleOption(GCOOP_MOPT_SYNC_ENEMIES);
            return;
        case ITEM_OPT_SWITCHES:
            toggleOption(GCOOP_MOPT_SYNC_SWITCHES);
            return;
        }
        if (direction != 0) {
            return; /* left/right only change values */
        }
        switch (id) {
        case ITEM_HOST_PRIVATE:
            sendRequest(GCOOP_MCMD_HOST_PRIVATE, 0, "", "");
            break;
        case ITEM_HOST_PUBLIC:
            sendRequest(GCOOP_MCMD_HOST_PUBLIC, 0, "", "");
            break;
        case ITEM_BROWSE:
            sMainCursor = sCursor;
            sPage = PAGE_ROOMS;
            sCursor = 0;
            sendRequest(GCOOP_MCMD_REFRESH_ROOMS, 0, "", "");
            break;
        case ITEM_JOIN_ADDRESS:
            startEdit(EDIT_ADDRESS, sAddress, GCOOP_MENU_TEXT_LEN - 1);
            break;
        case ITEM_CHAT:
            startEdit(EDIT_CHAT, "", GCOOP_CHAT_LEN - 1);
            break;
        case ITEM_LEAVE:
            sendRequest(GCOOP_MCMD_LEAVE, 0, "", "");
            break;
        case ITEM_CLOSE:
            closeMenu();
            break;
        case ITEM_REFRESH:
            sendRequest(GCOOP_MCMD_REFRESH_ROOMS, 0, "", "");
            break;
        case ITEM_BACK:
            sPage = PAGE_MAIN;
            sCursor = sMainCursor;
            break;
        default:
            if (id >= ITEM_ROOM && id < ITEM_ROOM + GCOOP_MENU_ROOMS) {
                sJoinRoom = id - ITEM_ROOM;
                if (sStatus.rooms[sJoinRoom].locked) {
                    startEdit(EDIT_ROOM_PASSWORD, "", MENU_PASSWORD_LEN - 1);
                }
                else {
                    sendRequest(GCOOP_MCMD_JOIN_ROOM, sJoinRoom, "", "");
                    sPage = PAGE_MAIN;
                    sCursor = 0;
                }
            }
            break;
        }
    }

    void updateOpenMenu() {
        if (!canOpen()) {
            closeMenu();
            return;
        }
        bool up = padRepeat(PAD_UP, MR::testCorePadButtonUp(0));
        bool down = padRepeat(PAD_DOWN, MR::testCorePadButtonDown(0));
        bool left = padRepeat(PAD_LEFT, MR::testCorePadButtonLeft(0));
        bool right = padRepeat(PAD_RIGHT, MR::testCorePadButtonRight(0));

        if (sPage == PAGE_EDIT) {
            updateEdit(up, down, left, right, true);
            return;
        }
        if (MR::testCorePadTriggerMinus(0)) {
            closeMenu();
            return;
        }
        buildItems();
        if (up && sItemCount > 0) {
            sCursor = (sCursor + sItemCount - 1) % sItemCount;
        }
        if (down && sItemCount > 0) {
            sCursor = (sCursor + 1) % sItemCount;
        }
        if (sItemCount == 0) {
            return;
        }
        if (left || right) {
            activate(sItems[sCursor], left ? -1 : 1);
        }
        else if (MR::testCorePadTriggerA(0)) {
            activate(sItems[sCursor], 0);
        }
        else if (MR::testCorePadTriggerB(0)) {
            if (sPage == PAGE_ROOMS) {
                sPage = PAGE_MAIN;
                sCursor = sMainCursor;
            }
            else {
                closeMenu();
            }
        }
    }

    /* ---- Drawing ---- */

    f32 screenWidth() {
        return MR::isScreen16Per9() ? MENU_SCREEN_H * 16.0f / 9.0f : MENU_SCREEN_H * 4.0f / 3.0f;
    }

    void setupQuads() {
        GXClearVtxDesc();
        GXSetVtxDesc(GX_VA_POS, GX_DIRECT);
        GXSetVtxDesc(GX_VA_CLR0, GX_DIRECT);
        GXSetVtxAttrFmt(GX_VTXFMT0, GX_VA_POS, GX_POS_XYZ, GX_F32, 0);
        GXSetVtxAttrFmt(GX_VTXFMT0, GX_VA_CLR0, GX_CLR_RGBA, GX_RGBA8, 0);
        GXSetNumChans(1);
        GXSetChanCtrl(GX_COLOR0A0, GX_FALSE, GX_SRC_REG, GX_SRC_VTX, GX_LIGHT_NULL, GX_DF_NONE, GX_AF_NONE);
        GXSetNumTexGens(0);
        GXSetNumIndStages(0);
        GXSetNumTevStages(1);
        GXSetTevDirect(GX_TEVSTAGE0);
        GXSetTevOrder(GX_TEVSTAGE0, GX_TEXCOORD_NULL, GX_TEXMAP_NULL, GX_COLOR0A0);
        GXSetTevOp(GX_TEVSTAGE0, GX_PASSCLR);
        GXSetBlendMode(GX_BM_BLEND, GX_BL_SRCALPHA, GX_BL_INVSRCALPHA, GX_LO_CLEAR);
        GXSetAlphaCompare(GX_ALWAYS, 0, GX_AOP_AND, GX_ALWAYS, 0);
        GXSetZMode(GX_FALSE, GX_ALWAYS, GX_FALSE);
        GXSetCullMode(GX_CULL_NONE);
        GXColor black = { 0, 0, 0, 0 };
        GXSetFog(GX_FOG_NONE, 0.0f, 0.0f, 0.0f, 0.0f, black);
        GXSetColorUpdate(GX_TRUE);
        GXSetAlphaUpdate(GX_FALSE);
    }

    void quad(f32 x, f32 y, f32 w, f32 h, u32 color) {
        volatile f32 *pF = (volatile f32 *)0xCC008000;
        volatile u32 *pU = (volatile u32 *)0xCC008000;
        GXBegin(GX_QUADS, GX_VTXFMT0, 4);
        *pF = x;     *pF = y;     *pF = 0.0f; *pU = color;
        *pF = x + w; *pF = y;     *pF = 0.0f; *pU = color;
        *pF = x + w; *pF = y + h; *pF = 0.0f; *pU = color;
        *pF = x;     *pF = y + h; *pF = 0.0f; *pU = color;
    }

    /* Gold rim, navy fill, cyan inset. Same shapes the galaxy menus use, drawn with quads. */
    void galaxyFrame(f32 x, f32 y, f32 w, f32 h) {
        quad(x, y, w, h, COLOR_GOLD);
        quad(x + 3.0f, y + 3.0f, w - 6.0f, h - 6.0f, COLOR_NAVY);
        f32 ix = x + 6.0f;
        f32 iy = y + 6.0f;
        f32 iw = w - 12.0f;
        f32 ih = h - 12.0f;
        quad(ix, iy, iw, 2.0f, COLOR_CYAN);
        quad(ix, iy + ih - 2.0f, iw, 2.0f, COLOR_CYAN);
        quad(ix, iy, 2.0f, ih, COLOR_CYAN);
        quad(ix + iw - 2.0f, iy, 2.0f, ih, COLOR_CYAN);
    }

    /* Text is queued while the quads are drawn, then printed in one go with the font's GX setup. */
    struct TextLine {
        f32 mX, mY, mHeight;
        u32 mColor;
        char mText[MENU_TEXT_CAP];
    };
    TextLine sText[40];
    s32 sTextCount = 0;

    void text(f32 x, f32 y, f32 height, u32 color, const char *pFormat, ...) {
        if (sTextCount >= (s32)(sizeof(sText) / sizeof(sText[0]))) {
            return;
        }
        TextLine &rLine = sText[sTextCount++];
        rLine.mX = x;
        rLine.mY = y;
        rLine.mHeight = height;
        rLine.mColor = color;
        va_list args;
        va_start(args, pFormat);
        vsnprintf(rLine.mText, sizeof(rLine.mText), pFormat, args);
        va_end(args);
    }

    void printQueuedText(const nw4r::ut::Font &rFont) {
        Writer writer;
        writer.SetFont(rFont);
        /* The layouts drawn before us bind other textures to the slot the glyph cache thinks it owns. */
        writer.ResetTextureCache();
        writer.SetupGX();
        f32 fontHeight = (f32)rFont.GetHeight();
        if (fontHeight < 1.0f) {
            fontHeight = 1.0f;
        }
        u16 wide[MENU_TEXT_CAP];
        for (s32 i = 0; i < sTextCount; i++) {
            const TextLine &rLine = sText[i];
            s32 len = 0;
            for (; rLine.mText[len] && len < (s32)(sizeof(wide) / sizeof(wide[0])) - 1; len++) {
                wide[len] = (u8)rLine.mText[len];
            }
            wide[len] = 0;
            writer.SetScale(rLine.mHeight / fontHeight);
            writer.SetTextColor(nw4r::ut::Color(rLine.mColor));
            writer.SetCursor(rLine.mX, rLine.mY, 0.0f);
            writer.Print((const wchar_t *)wide, len);
        }
        sTextCount = 0;
    }

    const char *characterName(u8 character) {
        return character == 0 ? "Mario" : character == 1 ? "Luigi" : "?";
    }

    void layoutHint(f32 width) {
        const char *pStatus;
        if (!companionAlive()) {
            pStatus = "companion not running";
        }
        else if (inSession()) {
            pStatus = sStatus.lines[0];
        }
        else {
            pStatus = "offline";
        }
        f32 w = 360.0f;
        galaxyFrame(16.0f, MENU_SCREEN_H - 52.0f, w, 36.0f);
        text(28.0f, MENU_SCREEN_H - 42.0f, MENU_TEXT_H, COLOR_TEXT, "(-) Co-op    %s", pStatus);
    }

    void layoutNamePlate(f32 width) {
        f32 w = 280.0f;
        f32 x = 20.0f;
        f32 y = 16.0f;
        if (!sNameEdit) {
            galaxyFrame(x, y, w, 40.0f);
            text(x + 16.0f, y + 12.0f, MENU_TEXT_H, COLOR_TITLE, "(-) Name: %s", sLocalName[0] ? sLocalName : "...");
            return;
        }
        galaxyFrame(x, y, w, 96.0f);
        text(x + 16.0f, y + 12.0f, MENU_TITLE_H, COLOR_TITLE, "Your name");
        char shown[GCOOP_NAME_LEN + 8];
        s32 len = strlen(sEditBuf);
        s32 o = 0;
        for (s32 i = 0; i <= len && o < (s32)sizeof(shown) - 2; i++) {
            if (i == sEditPos) {
                shown[o++] = '[';
            }
            char c = i < len ? sEditBuf[i] : ' ';
            shown[o++] = (i == sEditPos && c == ' ') ? '_' : c;
            if (i == sEditPos) {
                shown[o++] = ']';
            }
        }
        shown[o] = 0;
        text(x + 16.0f, y + 40.0f, MENU_TEXT_H, COLOR_TEXT, "%s", shown);
        text(x + 16.0f, y + 66.0f, MENU_TEXT_H - 2.0f, COLOR_DIM, "(-) done   B cancel");
    }

    bool readRemoteSlot(s32 slot, GCoopPlayerState *pOut) {
        const GCoopPlayerState &src = GCoop::gMailbox.remote[slot];
        u32 seq = src.seq;
        if (seq & 1) {
            return false;
        }
        __sync();
        memcpy(pOut, (const void *)&src, sizeof(GCoopPlayerState));
        __sync();
        return src.seq == seq;
    }

    void nameTag(f32 width, const TVec3f &rPos, const TVec3f &rUp, const char *pName, bool you) {
        TVec3f above;
        above.set(rPos.x + rUp.x * 160.0f, rPos.y + rUp.y * 160.0f, rPos.z + rUp.z * 160.0f);
        TVec2f screen;
        if (!MR::calcScreenPosition(&screen, above)) {
            return;
        }
        f32 gameW = (f32)MR::getScreenWidth();
        f32 gameH = (f32)MR::getScreenHeight();
        if (gameW < 1.0f) {
            gameW = width;
        }
        if (gameH < 1.0f) {
            gameH = MENU_SCREEN_H;
        }
        f32 sx = screen.x * (width / gameW);
        f32 sy = screen.y * (MENU_SCREEN_H / gameH);
        s32 len = strlen(pName);
        if (len < 1) {
            return;
        }
        f32 tw = len * MENU_TAG_H * 0.55f;
        if (sx < -40.0f || sy < -10.0f || sx > width + 40.0f || sy > MENU_SCREEN_H + 10.0f) {
            return;
        }
        quad(sx - tw * 0.5f - 6.0f, sy - 2.0f, tw + 12.0f, MENU_TAG_H + 6.0f, COLOR_TAG);
        text(sx - tw * 0.5f, sy, MENU_TAG_H, you ? COLOR_TITLE : COLOR_TEXT, "%s", pName);
    }

    void layoutNameTags(f32 width) {
        if (!isFullMenuScene() || MR::isDemoActive()) {
            return;
        }
        const TVec3f *pPos = MR::getPlayerPos();
        if (pPos && !MR::isPlayerDead()) {
            TVec3f up;
            MR::getPlayerUpVec(&up);
            const char *pLocal = sLocalName[0] ? sLocalName : (MR::isPlayerLuigi() ? "Luigi" : "Mario");
            nameTag(width, *pPos, up, pLocal, true);
        }
        const char *pStage = MR::getCurrentStageName();
        s32 scenario = MR::getCurrentScenarioNo();
        for (s32 i = 0; i < GCOOP_MAX_REMOTE; i++) {
            GCoopPlayerState state;
            if (!readRemoteSlot(i, &state)) {
                continue;
            }
            state.stage[GCOOP_STAGE_NAME_LEN - 1] = 0;
            if (!state.active || !(state.flags & GCOOP_PF_IN_SCENE) || (state.flags & (GCOOP_PF_HIDDEN | GCOOP_PF_DEAD))) {
                continue;
            }
            if (!pStage || state.scenario != scenario || strcmp(state.stage, pStage) != 0) {
                continue;
            }
            TVec3f pos;
            TVec3f up;
            pos.set(state.pos[0], state.pos[1], state.pos[2]);
            up.set(state.up[0], state.up[1], state.up[2]);
            const char *pName = sRemoteNames[i][0] ? sRemoteNames[i] : (state.character ? "Luigi" : "Mario");
            nameTag(width, pos, up, pName, false);
        }
    }

    void layoutChat(f32 width) {
        s32 visible = 0;
        for (s32 i = 0; i < sShownCount; i++) {
            if (sOpen || sShown[i].mAge < MENU_CHAT_LIFE) {
                visible++;
            }
        }
        if (visible == 0) {
            return;
        }
        f32 w = 420.0f;
        f32 h = visible * MENU_LINE_H + 16.0f;
        f32 y = MENU_SCREEN_H - (sOpen || sHintFrames > 0 ? 96.0f : 24.0f) - h;
        galaxyFrame(16.0f, y, w, h);
        f32 cy = y + 10.0f;
        for (s32 i = 0; i < sShownCount; i++) {
            if (!sOpen && sShown[i].mAge >= MENU_CHAT_LIFE) {
                continue;
            }
            u32 color = COLOR_TEXT;
            if (!sOpen && sShown[i].mAge > MENU_CHAT_LIFE - 120) {
                u32 alpha = (u32)((MENU_CHAT_LIFE - sShown[i].mAge) * 255 / 120);
                color = (COLOR_TEXT & 0xFFFFFF00) | alpha;
            }
            text(28.0f, cy, MENU_TEXT_H, color, "%s: %s", sShown[i].mName, sShown[i].mText);
            cy += MENU_LINE_H;
        }
    }

    void layoutMenu(f32 width) {
        buildItems();
        f32 x = (width - MENU_PANEL_W) * 0.5f;
        f32 left = x + 18.0f;
        s32 infoLines = 0;
        if (sPage == PAGE_MAIN) {
            infoLines = 2 + (inSession() ? 2 + (s32)sStatus.playerCount : 1);
        }
        else if (sPage == PAGE_ROOMS) {
            infoLines = 2;
        }
        else {
            infoLines = 6;
        }
        f32 height = 56.0f + infoLines * MENU_LINE_H + sItemCount * MENU_LINE_H + 40.0f;
        f32 y = (MENU_SCREEN_H - height) * 0.5f;
        if (y < 16.0f) {
            y = 16.0f;
        }
        galaxyFrame(x, y, MENU_PANEL_W, height);
        text(left, y + 12.0f, MENU_TITLE_H, COLOR_TITLE, sPage == PAGE_ROOMS ? "Public games" : "Galaxy Co-op");
        f32 cy = y + 48.0f;

        if (!companionAlive()) {
            text(left, cy, MENU_TEXT_H, COLOR_ERROR, "The co-op companion is not running.");
            text(left, cy + MENU_LINE_H, MENU_TEXT_H, COLOR_DIM, "Start it on the PC: tools\\play.ps1");
            cy += 2 * MENU_LINE_H;
        }
        else if (sPage == PAGE_EDIT) {
            const char *pTitle = "Password (leave empty if none)";
            if (sEditTarget == EDIT_ADDRESS) {
                pTitle = "Host address (IP:port)";
            }
            else if (sEditTarget == EDIT_NAME) {
                pTitle = "Your name";
            }
            else if (sEditTarget == EDIT_CHAT) {
                pTitle = "Message";
            }
            text(left, cy, MENU_TEXT_H, COLOR_DIM, pTitle);
            cy += MENU_LINE_H + 4.0f;
            quad(left - 4.0f, cy - 3.0f, MENU_PANEL_W - 36.0f, MENU_LINE_H + 4.0f, 0x000000A0);
            char shown[GCOOP_MENU_TEXT_LEN + 2];
            s32 len = strlen(sEditBuf);
            s32 o = 0;
            for (s32 i = 0; i <= len && o < (s32)sizeof(shown) - 2; i++) {
                if (i == sEditPos) {
                    shown[o++] = '[';
                }
                char c = i < len ? sEditBuf[i] : ' ';
                shown[o++] = (i == sEditPos && c == ' ') ? '_' : c;
                if (i == sEditPos) {
                    shown[o++] = ']';
                }
            }
            shown[o] = 0;
            text(left, cy, MENU_TEXT_H, COLOR_TEXT, "%s", shown);
            cy += 2 * MENU_LINE_H;
            text(left, cy, MENU_TEXT_H, COLOR_DIM, "Up/Down: change letter   Left/Right: move");
            text(left, cy + MENU_LINE_H, MENU_TEXT_H, COLOR_DIM, "1: delete   A: OK   B: cancel");
            cy += 3 * MENU_LINE_H;
        }
        else if (sPage == PAGE_MAIN) {
            u32 color = sStatus.state == GCOOP_MST_ERROR ? COLOR_ERROR : inSession() ? COLOR_GOOD : COLOR_TEXT;
            text(left, cy, MENU_TEXT_H, color, "%s", waitingForCompanion() ? "Working..." : sStatus.lines[0]);
            cy += MENU_LINE_H;
            if (sStatus.lines[3][0]) {
                text(left, cy, MENU_TEXT_H, COLOR_ERROR, "%s", sStatus.lines[3]);
            }
            else if (sStatus.lines[2][0]) {
                text(left, cy, MENU_TEXT_H, COLOR_DIM, "%s", sStatus.lines[2]);
            }
            cy += MENU_LINE_H;
            if (inSession()) {
                text(left, cy, MENU_TEXT_H, COLOR_TITLE, "%s", sStatus.lines[1]);
                cy += MENU_LINE_H;
                text(left, cy, MENU_TEXT_H, COLOR_DIM, "Players:");
                cy += MENU_LINE_H;
                for (u32 i = 0; i < sStatus.playerCount; i++) {
                    const GCoopMenuPlayer &rPlayer = sStatus.players[i];
                    char name[GCOOP_MENU_PLAYER_NAME_LEN];
                    GCoop::copyName(name, rPlayer.name, sizeof(name));
                    text(left + 16.0f, cy, MENU_TEXT_H, rPlayer.isYou ? COLOR_TITLE : COLOR_TEXT, "%s (%s)%s%s", name,
                         characterName(rPlayer.character), rPlayer.isHost ? "  host" : "", rPlayer.isYou ? "  you" : "");
                    cy += MENU_LINE_H;
                }
            }
            else {
                cy += MENU_LINE_H;
            }
        }
        else {
            const char *pLine = waitingForCompanion() ? "Searching..." : sStatus.roomCount == 0 ? "No public games right now." : "Pick a game to join:";
            text(left, cy, MENU_TEXT_H, COLOR_DIM, "%s", pLine);
            cy += MENU_LINE_H;
            if (sStatus.lines[3][0]) {
                text(left, cy, MENU_TEXT_H, COLOR_ERROR, "%s", sStatus.lines[3]);
            }
            cy += MENU_LINE_H;
        }

        if (sPage != PAGE_EDIT) {
            cy += 8.0f;
            for (s32 i = 0; i < sItemCount; i++) {
                const Item &rItem = sItems[i];
                if (i == sCursor) {
                    quad(x + 8.0f, cy - 2.0f, MENU_PANEL_W - 16.0f, MENU_LINE_H, COLOR_CURSOR);
                }
                text(left + 8.0f, cy, MENU_TEXT_H, rItem.mEnabled ? COLOR_TEXT : COLOR_DIM, "%s", rItem.mText);
                cy += MENU_LINE_H;
            }
            text(left, y + height - 26.0f, MENU_TEXT_H - 2.0f, COLOR_DIM, "A: select   Left/Right: change   B: back");
        }
    }

    class MenuDrawer : public NameObj {
    public:
        MenuDrawer() : NameObj("GCoopMenu") {}

        virtual void movement() {
            GCoop::applyBossPose();
        }

        virtual void draw() const {
            bool plate = isNameCardScene();
            bool tags = isFullMenuScene();
            bool chat = false;
            for (s32 i = 0; i < sShownCount; i++) {
                if (sOpen || sShown[i].mAge < MENU_CHAT_LIFE) {
                    chat = true;
                }
            }
            if (!sOpen && !sNameEdit && sHintFrames <= 0 && !plate && !tags && !chat) {
                return;
            }
            /* MenuFont64 is a scan font with only some letters. The dialogue font has the whole alphabet. */
            nw4r::ut::Font *pFont = MR::getFontOnCurrentLanguage();
            if (!pFont) {
                pFont = MR::getMenuFontNW4R();
            }
            if (!pFont) {
                return;
            }
            f32 savedProjection[7];
            GXGetProjectionv(savedProjection);

            f32 width = screenWidth();
            Mtx44 projection;
            C_MTXOrtho(projection, 0.0f, MENU_SCREEN_H, 0.0f, width, -1.0f, 1.0f);
            GXSetProjection(projection, GX_ORTHOGRAPHIC);
            Mtx identity;
            PSMTXIdentity(identity);
            GXLoadPosMtxImm(identity, GX_PNMTX0);
            GXSetCurrentMtx(GX_PNMTX0);

            setupQuads();
            sTextCount = 0;
            if (sOpen && isFullMenuScene()) {
                layoutMenu(width);
            }
            else if (sHintFrames > 0 && isFullMenuScene()) {
                layoutHint(width);
            }
            if (plate) {
                layoutNamePlate(width);
            }
            if (tags) {
                layoutNameTags(width);
            }
            if (chat) {
                layoutChat(width);
            }
            printQueuedText(*pFont);

            GXSetProjectionv(savedProjection);
        }
    };
}

namespace GCoop {
    void initMenuScene() {
        MenuDrawer *pDrawer = new MenuDrawer();
        pDrawer->initWithoutIter();
        MR::connectToSceneLayout(pDrawer);
        sOpen = false;
        sHintFrames = MENU_HINT_FRAMES;
        for (s32 i = 0; i < PAD_COUNT; i++) {
            sPadHeld[i] = -1;
        }
    }

    void updateMenu() {
        u32 heartbeat = gMailbox.heartbeat;
        if (heartbeat != sLastHeartbeat) {
            sLastHeartbeat = heartbeat;
            sHeartbeatAge = 0;
        }
        else if (sHeartbeatAge < MENU_ALIVE_FRAMES) {
            sHeartbeatAge++;
        }
        readStatus();
        readSocialIn();
        if (!sOpen && !sNameEdit) {
            for (s32 i = 0; i < sShownCount; i++) {
                if (sShown[i].mAge < MENU_CHAT_LIFE) {
                    sShown[i].mAge++;
                }
            }
        }
        if (companionAlive()) {
            adoptPreferences();
        }
        if (sStatus.roomsSerial != sRoomsSerialSeen) {
            sRoomsSerialSeen = sStatus.roomsSerial;
            if (sPage == PAGE_ROOMS) {
                sCursor = 0;
            }
        }

        if (sNameEdit && !isNameCardScene()) {
            sNameEdit = false;
            sPage = PAGE_MAIN;
        }
        if (sOpen) {
            if (!isFullMenuScene()) {
                sOpen = false;
                gMailbox.menu.menuOpen = 0;
            }
            else {
                updateOpenMenu();
                return;
            }
        }
        if (sNameEdit) {
            bool up = padRepeat(PAD_UP, MR::testCorePadButtonUp(0));
            bool down = padRepeat(PAD_DOWN, MR::testCorePadButtonDown(0));
            bool left = padRepeat(PAD_LEFT, MR::testCorePadButtonLeft(0));
            bool right = padRepeat(PAD_RIGHT, MR::testCorePadButtonRight(0));
            updateEdit(up, down, left, right, false);
            if (sNameEdit && MR::testCorePadTriggerMinus(0)) {
                finishEdit();
            }
            return;
        }
        if (sHintFrames > 0) {
            sHintFrames--;
        }
        if (isNameCardScene()) {
            if (MR::testCorePadTriggerMinus(0)) {
                sNameEdit = true;
                sEditTarget = EDIT_NAME;
                startEdit(EDIT_NAME, sLocalName, GCOOP_NAME_LEN - 1);
                for (s32 i = 0; i < PAD_COUNT; i++) {
                    sPadHeld[i] = -1;
                }
            }
            return;
        }
        if (!isFullMenuScene() || !canOpen()) {
            return;
        }
        if (sApplyCharacter) {
            applyCharacter();
            return;
        }
        bool autoOpen = !sAutoOpened && companionAlive() && sChoicesReady && !inSession();
        if (MR::testCorePadTriggerMinus(0) || autoOpen) {
            sAutoOpened = true;
            openMenu();
        }
    }
}
