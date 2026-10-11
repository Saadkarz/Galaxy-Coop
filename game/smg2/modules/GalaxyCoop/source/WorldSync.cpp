#include "GCoop/GCoop.h"
#include "GCoop/Addresses.h"
#include "Game/Map/StageSwitch.h"
#include "Game/Util/JMapIdInfo.h"

/*
 * Shared world state beyond collectibles: stage switches, enemy defeats,
 * player deaths, and following the host to another galaxy. Switch and enemy
 * sync are opt-in (companion --sync-switches / --sync-enemies) because some
 * galaxies drive per-player things such as camera or music through switches.
 */

extern "C" {
    void *getGameSequenceInGame__20GameSequenceFunctionFv();
    void setInStage__18GameSequenceInGameFPCc(void *pSequence, const char *pStage);
}

/* The exported requestChangeStage symbol is the scenario-select entry. Inside a galaxy the
 * game uses the entry 0x100 bytes later, with a null start id meaning "the scenario entrance". */
void GCoop::requestChangeStageInGame(const char *pStage, s32 scenario) {
    typedef void (*Fn)(const char *, s32, s32, const JMapIdInfo *);
    Fn fn = (Fn)0x804D8510;
    fn(pStage, scenario, scenario, 0);
    setInStage__18GameSequenceInGameFPCc(getGameSequenceInGame__20GameSequenceFunctionFv(), pStage);
}

namespace {
    bool companionWants(u32 flag) {
        return (GCoop::gMailbox.companionFlags & flag) != 0;
    }

    /* ---- Stage switches ---- */

    /* Same layout as SwitchIdInfo, which has no default constructor. */
    struct SwitchIdInfoData {
        JMapIdInfo *mIDInfo;
        bool mIsGlobal;
    };

    u32 switchKey(const SwitchIdInfo &rInfo) {
        const JMapIdInfo *pId = rInfo.mIDInfo;
        if (!pId) {
            return 0;
        }
        u32 zone = rInfo.mIsGlobal ? 0 : ((u32)pId->mZoneId & 0x7FFF);
        return (rInfo.mIsGlobal ? 0x80000000 : 0) | (zone << 16) | ((u32)pId->mId & 0xFFFF);
    }

    void reportSwitch(u16 type, const SwitchIdInfo &rInfo, bool wasOn) {
        if (GCoop::gApplyingRemote || !companionWants(GCOOP_CF_SYNC_SWITCH)) {
            return;
        }
        bool on = type == GCOOP_EV_SWITCH_ON;
        u32 key = switchKey(rInfo);
        if (key && wasOn != on) {
            GCoop::pushEvent(type, key, 0, 0, 0, 0);
        }
    }

    void hookSwitchOn(const SwitchIdInfo &rInfo) {
        bool wasOn = StageSwitchFunction::isOnSwitchBySwitchIdInfo(rInfo);
        StageSwitchFunction::onSwitchBySwitchIdInfo(rInfo);
        reportSwitch(GCOOP_EV_SWITCH_ON, rInfo, wasOn);
    }

    void hookSwitchOff(const SwitchIdInfo &rInfo) {
        bool wasOn = StageSwitchFunction::isOnSwitchBySwitchIdInfo(rInfo);
        StageSwitchFunction::offSwitchBySwitchIdInfo(rInfo);
        reportSwitch(GCOOP_EV_SWITCH_OFF, rInfo, wasOn);
    }

    /* ---- Enemies ---- */

    struct EnemyClass {
        u32 mVtable;
        u32 mInit;
        u32 mKill;
    };

#define GCOOP_ENEMY_ENTRY(N) { GCOOP_VT_ENEMY_##N, GCOOP_ORIG_##N##_INIT, GCOOP_ORIG_##N##_KILL },
    const EnemyClass sEnemyClasses[] = { GCOOP_ENEMY_TABLE(GCOOP_ENEMY_ENTRY) };
#undef GCOOP_ENEMY_ENTRY

    typedef void (*InitFunc)(LiveActor *, const JMapInfoIter &);
    typedef void (*KillFunc)(LiveActor *);

    /* The hooks are only installed in these vtables, so the object's vtable pointer identifies its original functions. */
    const EnemyClass *classOf(const LiveActor *pActor) {
        u32 vtable = *(const u32 *)pActor;
        for (u32 i = 0; i < sizeof(sEnemyClasses) / sizeof(sEnemyClasses[0]); i++) {
            if (sEnemyClasses[i].mVtable == vtable) {
                return &sEnemyClasses[i];
            }
        }
        return 0;
    }

    void hookEnemyInit(LiveActor *pThis, const JMapInfoIter &rIter) {
        const EnemyClass *pClass = classOf(pThis);
        ((InitFunc)pClass->mInit)(pThis, rIter);
        GCoop::registerPlacedObject(GCOOP_EV_ENEMY_KILL, pThis, rIter);
    }

    void hookEnemyKill(LiveActor *pThis) {
        const EnemyClass *pClass = classOf(pThis);
        bool wasAlive = !MR::isDead(pThis);
        ((KillFunc)pClass->mKill)(pThis);
        if (wasAlive && !GCoop::gApplyingRemote && companionWants(GCOOP_CF_SYNC_ENEMY)) {
            u32 key = GCoop::findKey(GCOOP_EV_ENEMY_KILL, pThis);
            if (key) {
                GCoop::pushEvent(GCOOP_EV_ENEMY_KILL, key, 0, 0, &pThis->mTranslation, 0);
            }
        }
    }

    /* ---- Deaths and travel ---- */

    bool sWasDead = false;
    u32 sTravelSerialSeen = 0;
    char sTravelStage[GCOOP_STAGE_NAME_LEN];
}

kmBranch(GCOOP_BR_SWITCH_A_ON, hookSwitchOn);
kmBranch(GCOOP_BR_SWITCH_B_ON, hookSwitchOn);
kmBranch(GCOOP_BR_SWITCH_DEAD_ON, hookSwitchOn);
kmBranch(GCOOP_BR_SWITCH_A_OFF, hookSwitchOff);
kmBranch(GCOOP_BR_SWITCH_B_OFF, hookSwitchOff);
kmBranch(GCOOP_BR_SWITCH_DEAD_OFF, hookSwitchOff);

extern "C" {
    void startCometEvent__13EventFunctionFv();
    void endCometEvent__13EventFunctionFv();
}

void hookCometOn() {
    startCometEvent__13EventFunctionFv();
    if (!GCoop::gApplyingRemote) {
        GCoop::pushEvent(GCOOP_EV_COMET_ON, 0, 0, 0, 0, 0);
    }
}

void hookCometOff() {
    endCometEvent__13EventFunctionFv();
    if (!GCoop::gApplyingRemote) {
        GCoop::pushEvent(GCOOP_EV_COMET_OFF, 0, 0, 0, 0, 0);
    }
}

kmBranch(GCOOP_BR_COMET_ON, hookCometOn);
kmBranch(GCOOP_BR_COMET_OFF, hookCometOff);

#define GCOOP_ENEMY_PATCH(N) \
    kmCondWritePointer(GCOOP_VT_ENEMY_##N + GCOOP_VT_SLOT_INIT, GCOOP_ORIG_##N##_INIT, hookEnemyInit); \
    kmCondWritePointer(GCOOP_VT_ENEMY_##N + GCOOP_VT_SLOT_KILL, GCOOP_ORIG_##N##_KILL, hookEnemyKill);
GCOOP_ENEMY_TABLE(GCOOP_ENEMY_PATCH)
#undef GCOOP_ENEMY_PATCH

namespace GCoop {
    ApplyResult applyWorldEvent(const GCoopEvent &rEvent) {
        switch (rEvent.type) {
        case GCOOP_EV_SWITCH_ON:
        case GCOOP_EV_SWITCH_OFF: {
            if (!companionWants(GCOOP_CF_SYNC_SWITCH) || !rEvent.key) {
                return APPLY_DONE;
            }
            JMapIdInfo id;
            id.mId = (s32)(rEvent.key & 0xFFFF);
            id.mZoneId = (s32)((rEvent.key >> 16) & 0x7FFF);
            SwitchIdInfoData info;
            info.mIDInfo = &id;
            info.mIsGlobal = (rEvent.key & 0x80000000) != 0;
            const SwitchIdInfo &rInfo = *reinterpret_cast<const SwitchIdInfo *>(&info);
            if (rEvent.type == GCOOP_EV_SWITCH_ON) {
                StageSwitchFunction::onSwitchBySwitchIdInfo(rInfo);
            }
            else {
                StageSwitchFunction::offSwitchBySwitchIdInfo(rInfo);
            }
            return APPLY_DONE;
        }

        case GCOOP_EV_ENEMY_KILL: {
            if (!companionWants(GCOOP_CF_SYNC_ENEMY)) {
                return APPLY_DONE;
            }
            LiveActor *pEnemy = findObject(GCOOP_EV_ENEMY_KILL, rEvent.key);
            if (!pEnemy) {
                return APPLY_UNKNOWN;
            }
            if (MR::isDead(pEnemy)) {
                return APPLY_RETRY;
            }
            pEnemy->kill();
            return APPLY_DONE;
        }

        case GCOOP_EV_PLAYER_DEATH:
            GCOOP_LOG("player %d died\n", rEvent.source);
            return APPLY_DONE;

        case GCOOP_EV_COMET_ON:
            MR::startGalaxyCometEvent();
            return APPLY_DONE;
        case GCOOP_EV_COMET_OFF:
            MR::endGalaxyCometEvent();
            return APPLY_DONE;
        }
        return APPLY_UNKNOWN;
    }

    void onSceneStartWorld() {
        sWasDead = false;
    }

    void updateWorld() {
        bool dead = MR::isPlayerDead();
        if (dead && !sWasDead) {
            pushEvent(GCOOP_EV_PLAYER_DEATH, 0, MR::getPlayerLife(), 0, MR::getPlayerPos(), 0);
        }
        sWasDead = dead;

        const GCoopTravelRequest &travel = gMailbox.travel;
        u32 serial = travel.serial;
        if (serial == sTravelSerialSeen) {
            return;
        }
        /* The opening story is a demo too, but someone joining an existing session should
         * leave it for the host's galaxy instead of watching it from the beginning. */
        const char *pStageNow = MR::getCurrentStageName();
        bool intro = MR::isStageStoryBook() || (pStageNow && strcmp(pStageNow, "PeachCastleGalaxy") == 0);
        if ((dead || MR::isDemoActive()) && !intro) {
            return; /* try again once the player can be moved */
        }
        sTravelSerialSeen = serial;
        copyName(sTravelStage, travel.stage, GCOOP_STAGE_NAME_LEN);
        s32 scenario = travel.scenario;
        if (sTravelStage[0] == 0 || scenario < 1) {
            return;
        }
        if (strcmp(sTravelStage, MR::getCurrentStageName()) == 0 && scenario == MR::getCurrentScenarioNo()) {
            return;
        }
        GCOOP_INFO("travelling to %s scenario %d\n", sTravelStage, scenario);
        requestChangeStageInGame(sTravelStage, scenario);
    }
}
