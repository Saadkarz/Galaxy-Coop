#include "GCoop/GCoop.h"
#include "GCoop/Addresses.h"
#include "Game/MapObj/Coin.h"
#include "Game/MapObj/StarPiece.h"
#include "Game/MapObj/PowerStar.h"

/*
 * Coins, star bits and Power Stars.
 *
 * Local pickups are reported from the exact call that grants them, so the
 * event fires once per real collection. Remote pickups are replayed through
 * the same game function on our copy of the object (requestGetCoin,
 * touchPlayer), which also adds it to our counters: collectibles are shared,
 * as in sm64coop. Objects created at runtime (enemy drops, launched star bits)
 * have no placement identity and stay local.
 */

extern "C" {
    void touchPlayer__9StarPieceFv(StarPiece *pThis);
    void initAndSetRailMoveFromGroup__9StarPieceFRC12JMapInfoIter(StarPiece *pThis, const JMapInfoIter &rIter);
    void initAndSetFloatingFromGroup__9StarPieceFRC12JMapInfoIter(StarPiece *pThis, const JMapInfoIter &rIter);
}

namespace {
    void report(u16 type, LiveActor *pActor) {
        if (GCoop::gApplyingRemote) {
            return;
        }
        u32 key = GCoop::findKey(type, pActor);
        if (key) {
            GCoop::pushEvent(type, key, 0, 0, &pActor->mTranslation, 0);
        }
    }

    /* ---- Coins ---- */

    void hookCoinInit(Coin *pThis, const JMapInfoIter &rIter) {
        pThis->Coin::init(rIter);
        GCoop::registerPlacedObject(GCOOP_EV_COIN, pThis, rIter);
    }

    bool hookCoinRequestGet(Coin *pThis) {
        bool got = pThis->requestGetCoin();
        if (got) {
            report(GCOOP_EV_COIN, pThis);
        }
        return got;
    }

    /* ---- Star bits ---- */

    void hookStarPieceInit(StarPiece *pThis, const JMapInfoIter &rIter) {
        pThis->StarPiece::init(rIter);
        GCoop::registerPlacedObject(GCOOP_EV_STAR_PIECE, pThis, rIter);
    }

    /* Children of a StarPieceGroup: keyed by the group's placement plus creation order. */
    void hookStarPieceRailFromGroup(StarPiece *pThis, const JMapInfoIter &rGroupIter) {
        initAndSetRailMoveFromGroup__9StarPieceFRC12JMapInfoIter(pThis, rGroupIter);
        if (MR::isValidInfo(rGroupIter)) {
            GCoop::registerObject(GCOOP_EV_STAR_PIECE, pThis, GCoop::hashMix(GCoop::placementKey(rGroupIter), 'SPGR'));
        }
    }

    void hookStarPieceFloatingFromGroup(StarPiece *pThis, const JMapInfoIter &rGroupIter) {
        initAndSetFloatingFromGroup__9StarPieceFRC12JMapInfoIter(pThis, rGroupIter);
        if (MR::isValidInfo(rGroupIter)) {
            GCoop::registerObject(GCOOP_EV_STAR_PIECE, pThis, GCoop::hashMix(GCoop::placementKey(rGroupIter), 'SPGF'));
        }
    }

    void hookStarPieceTouchPlayer(StarPiece *pThis) {
        bool wasAlive = !MR::isDead(pThis);
        touchPlayer__9StarPieceFv(pThis);
        if (wasAlive) {
            report(GCOOP_EV_STAR_PIECE, pThis);
        }
    }

    /* ---- Power Stars ---- */

    PowerStar *sStarReceivingMsg = 0;

    void hookPowerStarInit(PowerStar *pThis, const JMapInfoIter &rIter) {
        pThis->PowerStar::init(rIter);
        if (pThis->mPowerStarId > 0) {
            GCoop::registerObject(GCOOP_EV_POWER_STAR, pThis, (u32)pThis->mPowerStarId);
        }
    }

    bool hookPowerStarReceiveOtherMsg(PowerStar *pThis, u32 msg, HitSensor *pSender, HitSensor *pReceiver) {
        sStarReceivingMsg = pThis;
        bool handled = pThis->PowerStar::receiveOtherMsg(msg, pSender, pReceiver);
        sStarReceivingMsg = 0;
        return handled;
    }

    void reportStar(bool grand) {
        PowerStar *pStar = sStarReceivingMsg;
        if (!pStar || GCoop::gApplyingRemote) {
            return;
        }
        GCOOP_INFO("got %s star %d\n", grand ? "grand" : "power", pStar->mPowerStarId);
        GCoop::pushEvent(GCOOP_EV_POWER_STAR, (u32)pStar->mPowerStarId, pStar->mPowerStarId, grand ? 1 : 0, &pStar->mTranslation, 0);
    }

    void hookPowerStarGetDemo() {
        MR::requestPowerStarGetDemo();
        reportStar(false);
    }

    void hookGrandStarGetDemo() {
        MR::requestGrandStarGetDemo();
        reportStar(true);
    }
}

kmCondWritePointer(GCOOP_VT_COIN + GCOOP_VT_SLOT_INIT, GCOOP_ORIG_COIN_INIT, hookCoinInit);
kmCall(GCOOP_CALL_COIN_REQUEST_GET, hookCoinRequestGet);

kmCondWritePointer(GCOOP_VT_STARPIECE + GCOOP_VT_SLOT_INIT, GCOOP_ORIG_STARPIECE_INIT, hookStarPieceInit);
kmCall(GCOOP_CALL_STARPIECE_GROUP_RAIL, hookStarPieceRailFromGroup);
kmCall(GCOOP_CALL_STARPIECE_GROUP_FLOAT, hookStarPieceFloatingFromGroup);
kmCall(GCOOP_CALL_STARPIECE_TOUCH_MSG, hookStarPieceTouchPlayer);
kmCall(GCOOP_CALL_STARPIECE_TOUCH_PTR, hookStarPieceTouchPlayer);

kmCondWritePointer(GCOOP_VT_POWERSTAR + GCOOP_VT_SLOT_INIT, GCOOP_ORIG_POWERSTAR_INIT, hookPowerStarInit);
kmCondWritePointer(GCOOP_VT_POWERSTAR + GCOOP_VT_SLOT_OTHERMSG, GCOOP_ORIG_POWERSTAR_OTHERMSG, hookPowerStarReceiveOtherMsg);
kmCall(GCOOP_CALL_POWERSTAR_GET_DEMO, hookPowerStarGetDemo);
kmCall(GCOOP_CALL_GRANDSTAR_GET_DEMO, hookGrandStarGetDemo);

namespace GCoop {
    ApplyResult applyCollectibleEvent(const GCoopEvent &rEvent) {
        LiveActor *pActor = findObject(rEvent.type, rEvent.key);
        if (!pActor) {
            return APPLY_UNKNOWN;
        }

        switch (rEvent.type) {
        case GCOOP_EV_COIN:
            if (MR::isDead(pActor)) {
                return APPLY_RETRY;
            }
            static_cast<Coin *>(pActor)->requestGetCoin();
            return APPLY_DONE;

        case GCOOP_EV_STAR_PIECE:
            if (MR::isDead(pActor)) {
                return APPLY_RETRY;
            }
            touchPlayer__9StarPieceFv(static_cast<StarPiece *>(pActor));
            return APPLY_DONE;

        case GCOOP_EV_POWER_STAR:
            if (!(gMailbox.companionFlags & GCOOP_CF_SHARE_STARS)) {
                return APPLY_DONE;
            }
            if (MR::isDead(pActor) || MR::isPlayerDead() || MR::isDemoActive()) {
                return APPLY_RETRY;
            }
            /* Same message the player sends when it touches the star. */
            if (!pActor->receiveOtherMsg(GCOOP_MSG_AUTO_RUSH_BEGIN, MR::getPlayerBodySensor(), MR::getBodySensor(pActor))) {
                return APPLY_RETRY;
            }
            return APPLY_DONE;
        }
        return APPLY_UNKNOWN;
    }
}
