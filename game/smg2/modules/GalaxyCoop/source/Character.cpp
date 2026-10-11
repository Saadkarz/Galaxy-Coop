#include "GCoop/GCoop.h"
#include "GCoop/Addresses.h"

/*
 * Lets each player pick Mario or Luigi in the co-op menu instead of following
 * the save file. Everything that asks "is the player Luigi" (model loading in
 * MarioActor, physics, voices, NPC lines) goes through
 * GameDataFunction::isLuigiPlayer, so replacing that one function is enough.
 * The answer is fixed when a scene starts building: the model can't change
 * mid-scene, and physics and voices must keep matching it.
 */

extern "C" {
    u8 *getCurrentGameDataPlayerStatus__16GameDataFunctionFv();
    /* true keeps Mario's archive, false swaps in Luigi's. */
    bool requestChangeArchivePlayer__18GameSystemFunctionFb(bool isMario);
    bool isEndChangeArchivePlayer__18GameSystemFunctionFv();
}

namespace {
    /* 0 follow the companion flags or the save file, 1 Mario, 2 Luigi */
    u32 sSceneCharacter = 0;
    bool sAwaitArchive = false;
    bool sReturnPending = false;
    u32 sReturnSerial = 0;
    s32 sReturnWait = 0;
    TVec3f sReturnPos;
    char sReturnStage[GCOOP_STAGE_NAME_LEN];

    bool hookIsLuigiPlayer() {
        if (sSceneCharacter == 2) {
            return true;
        }
        if (sSceneCharacter == 1) {
            return false;
        }
        u32 flags = GCoop::gMailbox.companionFlags;
        if (flags & GCOOP_CF_FORCE_LUIGI) {
            return true;
        }
        if (flags & GCOOP_CF_FORCE_MARIO) {
            return false;
        }
        return (getCurrentGameDataPlayerStatus__16GameDataFunctionFv()[GCOOP_PLAYER_STATUS_LUIGI_BYTE] & 1) != 0;
    }
}

kmBranch(GCOOP_FN_IS_LUIGI_PLAYER, hookIsLuigiPlayer);

namespace GCoop {
    void setSceneCharacter(u32 character) {
        if (character == 1 || character == 2) {
            sSceneCharacter = character;
        }
    }

    void lockSceneCharacter() {
        const GCoopMenu &rMenu = gMailbox.menu;
        u32 character = rMenu.character;
        if (character != 1 && character != 2) {
            character = rMenu.prefCharacter;
        }
        if (character == 1 || character == 2) {
            sSceneCharacter = character;
        }
    }

    /* Swap the player archive, then reload this galaxy once that archive is ready.
     * Clearing the "after miss" state is what dumped the player back on the early ship. */
    void beginCharacterReload(const TVec3f &rPos) {
        sReturnPos = rPos;
        copyName(sReturnStage, MR::getCurrentStageName(), sizeof(sReturnStage));
        sReturnSerial = gMailbox.sceneSerial;
        sReturnPending = true;
        sReturnWait = 0;
        sAwaitArchive = true;
        requestChangeArchivePlayer__18GameSystemFunctionFb(sSceneCharacter != 2);
    }

    void updateCharacterReload() {
        if (sAwaitArchive) {
            sReturnWait++;
            if (!isEndChangeArchivePlayer__18GameSystemFunctionFv() && sReturnWait < 180) {
                return;
            }
            sAwaitArchive = false;
            sReturnWait = 0;
            const char *pStage = MR::getCurrentStageName();
            s32 scenario = MR::getCurrentScenarioNo();
            if (!pStage || !pStage[0] || scenario < 1) {
                sReturnPending = false;
                return;
            }
            requestChangeStageInGame(pStage, scenario);
            return;
        }
        if (!sReturnPending || gMailbox.sceneSerial == sReturnSerial) {
            return;
        }
        const char *pStage = MR::getCurrentStageName();
        if (!pStage || strcmp(pStage, sReturnStage) != 0) {
            sReturnPending = false;
            return;
        }
        if (MR::isPlayerDead() || MR::isDemoActive() || MR::isStageStateScenarioOpeningCamera()) {
            if (++sReturnWait > 600) {
                sReturnPending = false;
            }
            return;
        }
        MR::setPlayerPos(sReturnPos);
        sReturnPending = false;
    }
}
