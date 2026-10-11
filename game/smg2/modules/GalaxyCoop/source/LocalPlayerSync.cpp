#include "GCoop/GCoop.h"
#include "GCoop/Addresses.h"
#include "Game/Player/MarioActor.h"
#include "Game/Animation/XanimePlayer.h"

namespace {
    void storeVec(f32 *pDst, const TVec3f &rSrc) {
        pDst[0] = rSrc.x;
        pDst[1] = rSrc.y;
        pDst[2] = rSrc.z;
    }

    void *animatorOf(MarioActor *pMario) {
        return *(void **)((u8 *)pMario + GCOOP_MARIOACTOR_ANIMATOR);
    }
}

namespace GCoop {
    XanimePlayer *getPlayerXanime(MarioActor *pMario) {
        void *pAnimator = animatorOf(pMario);
        return pAnimator ? *(XanimePlayer **)((u8 *)pAnimator + GCOOP_ANIMATOR_XANIME) : 0;
    }

    XanimePlayer *getPlayerUpperXanime(MarioActor *pMario) {
        u8 *pAnimator = (u8 *)animatorOf(pMario);
        if (!pAnimator || !pAnimator[GCOOP_ANIMATOR_UPPER_ACTIVE]) {
            return 0;
        }
        return *(XanimePlayer **)(pAnimator + GCOOP_ANIMATOR_UPPER);
    }

    XanimeResourceTable *getPlayerAnimTable(MarioActor *pMario) {
        void *pAnimator = animatorOf(pMario);
        return pAnimator ? *(XanimeResourceTable **)((u8 *)pAnimator + GCOOP_ANIMATOR_TABLE) : 0;
    }

    f32 *xanimeTrackWeights(XanimePlayer *pPlayer) {
        return (f32 *)((u8 *)pPlayer + GCOOP_XANIME_TRACK_WEIGHT);
    }

    u8 *xanimeFrameCtrl(XanimePlayer *pPlayer) {
        return *(u8 **)((u8 *)pPlayer + GCOOP_XANIME_FRAME_CTRL);
    }

    const char *xanimeGroupName(const XanimePlayer *pPlayer) {
        const char *const *pGroup = *(const char *const *const *)((const u8 *)pPlayer + GCOOP_XANIME_GROUP);
        return pGroup ? *pGroup : 0;
    }

    void writeLocalPlayer(MarioActor *pMario) {
        GCoopPlayerState &s = gMailbox.local;

        /* seqlock: odd while the body is being written */
        s.seq++;
        __sync();

        s.frame = gMailbox.gameFrame;
        storeVec(s.pos, *MR::getPlayerPos());
        storeVec(s.vel, *MR::getPlayerVelocity());
        storeVec(s.gravity, *MR::getPlayerGravity());

        TVec3f up, front;
        MR::getPlayerUpVec(&up);
        MR::getPlayerFrontVec(&front);
        storeVec(s.up, up);
        storeVec(s.front, front);

        /* The group name (not the resolved bck) so the ghost can blend the same tracks. */
        const char *pAnim = 0;
        XanimePlayer *pXanime = getPlayerXanime(pMario);
        if (pXanime) {
            pAnim = xanimeGroupName(pXanime);
            const u8 *pCtrl = xanimeFrameCtrl(pXanime);
            s.bckFrame = *(const f32 *)(pCtrl + GCOOP_FRAMECTRL_FRAME);
            s.bckRate = *(const f32 *)(pCtrl + GCOOP_FRAMECTRL_RATE);
            const f32 *pWeights = xanimeTrackWeights(pXanime);
            for (s32 i = 0; i < GCOOP_ANIM_TRACKS; i++) {
                s.trackWeight[i] = pWeights[i];
            }
        }
        else {
            s.bckFrame = 0.0f;
            s.bckRate = 1.0f;
            for (s32 i = 0; i < GCOOP_ANIM_TRACKS; i++) {
                s.trackWeight[i] = i == 0 ? 1.0f : 0.0f;
            }
        }
        if (!pAnim || pAnim[0] == 0) {
            pAnim = MR::getPlayerCurrentBckName();
        }

        const char *pUpper = 0;
        XanimePlayer *pUpperXanime = getPlayerUpperXanime(pMario);
        if (pUpperXanime) {
            pUpper = xanimeGroupName(pUpperXanime);
            const u8 *pCtrl = xanimeFrameCtrl(pUpperXanime);
            s.upperFrame = *(const f32 *)(pCtrl + GCOOP_FRAMECTRL_FRAME);
            s.upperRate = *(const f32 *)(pCtrl + GCOOP_FRAMECTRL_RATE);
        }
        else {
            s.upperFrame = 0.0f;
            s.upperRate = 1.0f;
        }
        copyName(s.upperName, pUpper ? pUpper : "", GCOOP_BCK_NAME_LEN);

        u32 flags = GCOOP_PF_IN_SCENE;
        if (MR::isOnGroundPlayer()) flags |= GCOOP_PF_ON_GROUND;
        if (MR::isPlayerSwimming()) flags |= GCOOP_PF_SWIMMING;
        if (MR::isPlayerDead()) flags |= GCOOP_PF_DEAD;
        if (MR::isPlayerHidden()) flags |= GCOOP_PF_HIDDEN;
        if (MR::isPlayerLuigi()) flags |= GCOOP_PF_LUIGI;
        if (MR::isDemoActive()) flags |= GCOOP_PF_DEMO;
        if (MR::isPlayerElementModeYoshi() || MR::isPlayerElementModeYoshiDash() || MR::isPlayerElementModeYoshiLight()) flags |= GCOOP_PF_YOSHI;
        s.flags = flags;

        s.life = MR::getPlayerLife();
        s.scenario = MR::getCurrentScenarioNo();
        s.character = (flags & GCOOP_PF_LUIGI) ? 1 : 0;
        s.active = 1;
        s.playerId = 0;
        copyName(s.bckName, pAnim, GCOOP_BCK_NAME_LEN);
        copyName(s.stage, MR::getCurrentStageName(), GCOOP_STAGE_NAME_LEN);

        __sync();
        s.seq++;
    }
}
