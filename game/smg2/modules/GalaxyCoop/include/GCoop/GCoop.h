#pragma once

#include "syati.h"
#include "mailbox.h"

class MarioActor;
class XanimePlayer;
class XanimeResourceTable;

#ifdef GCOOP_DEBUG
#define GCOOP_LOG(...) OSReport("[GCoop] " __VA_ARGS__)
#else
#define GCOOP_LOG(...) do { } while (0)
#endif
#define GCOOP_INFO(...) OSReport("[GCoop] " __VA_ARGS__)

namespace GCoop {
    extern GCoopMailbox gMailbox;

    /* True while an incoming event is being applied, so the hooks it triggers do not echo it back out. */
    extern bool gApplyingRemote;

    void initMailbox();
    void onSceneInit(MarioActor *pMario);
    void onFrame(MarioActor *pMario);

    bool pushEvent(u16 type, u32 key, s32 arg0, s32 arg1, const TVec3f *pPos, const char *pPayload);
    bool popEvent(GCoopEvent *pOut);

    void writeLocalPlayer(MarioActor *pMario);

    /* Mario's body animation player and the animation table it plays from. */
    XanimePlayer *getPlayerXanime(MarioActor *pMario);
    /* The upper-body layer, or 0 while Mario isn't playing one. */
    XanimePlayer *getPlayerUpperXanime(MarioActor *pMario);
    XanimeResourceTable *getPlayerAnimTable(MarioActor *pMario);
    f32 *xanimeTrackWeights(XanimePlayer *pPlayer);
    u8 *xanimeFrameCtrl(XanimePlayer *pPlayer);
    const char *xanimeGroupName(const XanimePlayer *pPlayer);

    void initRemotePlayers(MarioActor *pMario);
    void updateRemotePlayers();

    /* In-game co-op menu (Menu.cpp). */
    void initMenuScene();
    void updateMenu();

    /* Fixes Mario or Luigi for the scene about to be built (Character.cpp). */
    void setSceneCharacter(u32 character);
    void beginCharacterReload(const TVec3f &rPos);
    void lockSceneCharacter();
    void updateCharacterReload();

    /* Reload the galaxy the player is standing in. Not the scenario-select entry. */
    void requestChangeStageInGame(const char *pStage, s32 scenario);

    /*
     * Object registry: maps synced actors to keys that are identical on every
     * client in the same stage, because they are derived only from placement
     * data (zone, link id, position) plus creation order. Object types use the
     * event type numbers (GCOOP_EV_COIN, ...).
     */
    void resetObjectRegistry();
    u32 placementKey(const JMapInfoIter &rIter);
    u32 registerObject(u16 type, LiveActor *pActor, u32 baseKey);
    u32 registerPlacedObject(u16 type, LiveActor *pActor, const JMapInfoIter &rIter);
    u32 findKey(u16 type, const LiveActor *pActor);
    LiveActor *findObject(u16 type, u32 key);

    enum ApplyResult {
        APPLY_DONE,
        APPLY_RETRY,   /* object exists but cannot take the event yet (dead, mid-demo); try again next frame */
        APPLY_UNKNOWN, /* no object with that key in this scene */
    };
    ApplyResult applyCollectibleEvent(const GCoopEvent &rEvent);
    ApplyResult applyWorldEvent(const GCoopEvent &rEvent);
    void onSceneStartWorld();
    void updateWorld();

    /* Bosses (Boss.cpp): registered as they are placed, posed by whoever is fighting one. */
    void resetBosses();
    void writeBoss();
    void applyBossPose();
    void resetIncomingEvents();
    void applyIncomingEvents();

    u32 hashString(const char *pStr, u32 seed);
    u32 hashMix(u32 h, u32 value);
    u32 hashPosition(const TVec3f &rPos);
    void copyName(char *pDst, const char *pSrc, u32 size);
}
