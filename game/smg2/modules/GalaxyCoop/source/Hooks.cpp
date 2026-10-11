#include "GCoop/GCoop.h"
#include "GCoop/Addresses.h"
#include "Game/Player/MarioActor.h"

/*
 * Entry points. MarioActor exists exactly once per galaxy scene, so its init
 * marks "a scene with a player is being built" and its movement runs once per
 * gameplay frame. Both vtable slots are patched conditionally.
 */

namespace {
    u32 sFrameInScene = 0;

    void hookMarioActorInit(MarioActor *pThis, const JMapInfoIter &rIter) {
        pThis->MarioActor::init(rIter);
        GCoop::initMailbox();
        GCoop::onSceneInit(pThis);
        sFrameInScene = 0;
    }

    void hookMarioActorMovement(MarioActor *pThis) {
        pThis->MarioActor::movement();
        GCoop::onFrame(pThis);
        if ((sFrameInScene++ % 600) == 0) {
            GCOOP_INFO("hello from Galaxy Co-op, frame %d, stage %s\n", GCoop::gMailbox.gameFrame, MR::getCurrentStageName());
        }
    }
}

kmCondWritePointer(GCOOP_VT_MARIOACTOR + GCOOP_VT_SLOT_INIT, GCOOP_ORIG_MARIOACTOR_INIT, hookMarioActorInit);
kmCondWritePointer(GCOOP_VT_MARIOACTOR + GCOOP_VT_SLOT_MOVEMENT, GCOOP_ORIG_MARIOACTOR_MOVEMENT, hookMarioActorMovement);

namespace GCoop {
    void onSceneInit(MarioActor *pMario) {
        gMailbox.sceneSerial++;
        gMailbox.debug[GCOOP_DBG_HOOKS_OK] = 1;
        GCOOP_INFO("scene init #%d: %s scenario %d\n", gMailbox.sceneSerial, MR::getCurrentStageName(), MR::getCurrentScenarioNo());
        initRemotePlayers(pMario);
        onSceneStartWorld();
        initMenuScene();
    }

    void onFrame(MarioActor *pMario) {
        gMailbox.gameFrame++;
        writeLocalPlayer(pMario);
        updateCharacterReload();
        updateRemotePlayers();
        writeBoss();
        applyIncomingEvents();
        updateWorld();
        updateMenu();
    }
}
