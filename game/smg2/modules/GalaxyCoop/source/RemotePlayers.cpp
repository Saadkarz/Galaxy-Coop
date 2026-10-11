#include "GCoop/GCoop.h"
#include "GCoop/Addresses.h"
#include "Game/Animation/XanimePlayer.h"

/*
 * Ghosts of the other players. Actors can only be created while a scene is
 * being built, so every ghost the scene may need is created up front from the
 * MarioActor::init hook and then toggled with appear()/kill() as remote slots
 * become active. Each slot gets both a Mario and a Luigi body so a player can
 * switch characters between galaxies without respawning anything.
 *
 * Ghosts use the real player model and MarioAnime, initialised exactly like
 * MarioActor::initDrawAndModel does. Their animation players are rebuilt on
 * the local Mario's animation table, which is defined in code rather than in
 * MarioAnime.arc: only that table knows the blended groups (walk/run mixes and
 * so on) that remote players report, so with it the ghost plays the same
 * group with the same speed, frame and track weights. Like MarioAnimator, a
 * second player layers the upper-body group (carrying, spinning, waving...)
 * from Spine1 up. Build with -DGCOOP_GHOST_NPC to use LuigiNPC instead if the
 * player model misbehaves outside MarioActor.
 */

#define GCOOP_GHOST_SLOTS 3
/* Frames the ghost may drift from the remote animation before it is snapped back. */
#define GCOOP_ANIM_MAX_DRIFT 3.0f
/* Mario sits about this far above Yoshi's origin while riding. */
#define GCOOP_YOSHI_DROP 110.0f

extern "C" {
    XanimePlayer *__ct__12XanimePlayerFP8J3DModelP19XanimeResourceTable(void *pThis, J3DModel *pModel, XanimeResourceTable *pTable);
    XanimePlayer *__ct__12XanimePlayerFP8J3DModelP19XanimeResourceTableP12XanimePlayer(void *pThis, J3DModel *pModel, XanimeResourceTable *pTable, XanimePlayer *pBody);
    void enableJointTransform__10XanimeCoreFP12J3DModelData(void *pCore, J3DModelData *pModelData);
    void shareJointTransform__10XanimeCoreFPC10XanimeCore(void *pCore, const void *pOther);
}

namespace {
#ifdef GCOOP_GHOST_NPC
    const char *const sModelNames[2] = { "LuigiNPC", "LuigiNPC" };
    const char *const sAnimArchive = 0;
    const bool sModelFlag = false;
#else
    const char *const sModelNames[2] = { "Mario", "Luigi" };
    const char *const sAnimArchive = "MarioAnime";
    const bool sModelFlag = true;
#endif

    bool isUsable(const f32 *pVec) {
        return pVec[0] * pVec[0] + pVec[1] * pVec[1] + pVec[2] * pVec[2] > 0.01f;
    }

    void *xanimeCore(XanimePlayer *pPlayer) {
        return *(void **)((u8 *)pPlayer + GCOOP_XANIME_CORE);
    }

    const void *xanimeGroup(XanimePlayer *pPlayer) {
        return *(const void **)((u8 *)pPlayer + GCOOP_XANIME_GROUP);
    }

    /*
     * Plays group pName on pPlayer unless it already is. The group pointer is
     * compared too, so a one-shot the player replaced on its own (default
     * animation) is restarted even though the remote name didn't change.
     */
    void followGroup(XanimePlayer *pPlayer, const char *pName, char *pCurrent, const void *&rGroup) {
        if (strcmp(pName, pCurrent) == 0 && xanimeGroup(pPlayer) == rGroup) {
            return;
        }
        GCoop::copyName(pCurrent, pName, GCOOP_BCK_NAME_LEN);
        pPlayer->changeAnimationBck(pName);
        rGroup = xanimeGroup(pPlayer);
    }

    void followFrame(XanimePlayer *pPlayer, f32 frame, f32 rate) {
        u8 *pCtrl = GCoop::xanimeFrameCtrl(pPlayer);
        if (!pCtrl) {
            return;
        }
        *(f32 *)(pCtrl + GCOOP_FRAMECTRL_RATE) = rate;

        f32 &rFrame = *(f32 *)(pCtrl + GCOOP_FRAMECTRL_FRAME);
        f32 end = (f32)*(const s16 *)(pCtrl + GCOOP_FRAMECTRL_END);
        f32 target = frame;
        if (end >= 1.0f) {
            /* The companion advances the frame past the end; loops wrap, one-shots hold. */
            while (target >= end) {
                target -= end;
            }
            if (target < 0.0f) {
                target = 0.0f;
            }
        }
        f32 drift = target - rFrame;
        if (drift < 0.0f) {
            drift = -drift;
        }
        bool wrapped = end >= 1.0f && drift > end - GCOOP_ANIM_MAX_DRIFT;
        if (drift > GCOOP_ANIM_MAX_DRIFT && !wrapped) {
            rFrame = target;
        }
    }

    /* Yoshi has no separate archive: YoshiWait and YoshiRun live in Yoshi.arc. */
    class YoshiMount : public LiveActor {
    public:
        YoshiMount() : LiveActor("GCoopYoshi"), mUp(0.0f, 1.0f, 0.0f), mFront(0.0f, 0.0f, 1.0f) {
            mBck[0] = 0;
        }

        virtual void init(const JMapInfoIter &rIter) {
            initModelManagerWithAnm("Yoshi", 0, 0, false);
            MR::connectToSceneNpc(this);
            MR::invalidateClipping(this);
            makeActorDead();
        }

        virtual void calcAndSetBaseMtx() {
            TPos3f mtx;
            MR::makeMtxUpFrontPos(&mtx, mUp, mFront, mTranslation);
            MR::setBaseTRMtx(this, mtx);
        }

        void place(const TVec3f &rPos, const TVec3f &rUp, const TVec3f &rFront, bool running) {
            mUp = rUp;
            mFront = rFront;
            mTranslation.set(rPos.x - rUp.x * GCOOP_YOSHI_DROP, rPos.y - rUp.y * GCOOP_YOSHI_DROP, rPos.z - rUp.z * GCOOP_YOSHI_DROP);
            const char *pBck = running ? "YoshiRun" : "YoshiWait";
            if (strcmp(pBck, mBck) != 0) {
                GCoop::copyName(mBck, pBck, sizeof(mBck));
                MR::startBckIfExist(this, pBck);
            }
        }

    private:
        TVec3f mUp;
        TVec3f mFront;
        char mBck[16];
    };

    class RemotePlayerActor : public LiveActor {
    public:
        RemotePlayerActor(const char *pModelName)
            : LiveActor("GCoopRemotePlayer"), mModelName(pModelName), mUp(0.0f, 1.0f, 0.0f), mFront(0.0f, 0.0f, 1.0f),
              mBody(0), mUpper(0), mBodyGroup(0), mUpperGroup(0), mSpineJoint(0), mPartsJoint(0), mUpperOn(false), mYoshi(0) {
            mBckName[0] = 0;
            mUpperName[0] = 0;
        }

        virtual void init(const JMapInfoIter &rIter) {
            initModelManagerWithAnm(mModelName, sAnimArchive, 0, sModelFlag);
            MR::connectToSceneNpc(this);
            MR::invalidateClipping(this);
            mYoshi = new YoshiMount();
            mYoshi->initWithoutIter();
            makeActorDead();
        }

        virtual void calcAndSetBaseMtx() {
            TPos3f mtx;
            MR::makeMtxUpFrontPos(&mtx, mUp, mFront, mTranslation);
            MR::setBaseTRMtx(this, mtx);
        }

        /*
         * Swaps in animation players that read the player's own animation
         * table, set up the way MarioAnimator::init sets up Mario's.
         */
        void usePlayerAnimTable(XanimeResourceTable *pTable, const char *pInitial) {
            J3DModel *pModel = MR::getJ3DModel(this);
            J3DModelData *pModelData = MR::getJ3DModelData(this);
            if (!pTable || !pModel || !pModelData) {
                return;
            }
            void *pMem = operator new(GCOOP_XANIME_SIZE);
            if (!pMem) {
                return;
            }
            XanimePlayer *pBody = __ct__12XanimePlayerFP8J3DModelP19XanimeResourceTable(pMem, pModel, pTable);
            followGroup(pBody, pInitial, mBckName, mBodyGroup);
            enableJointTransform__10XanimeCoreFP12J3DModelData(xanimeCore(pBody), pModelData);
            mModelManager->mXanimePlayer = pBody;
            mBody = pBody;

            s16 spine = MR::getJointIndex(this, "Spine1");
            s16 parts = MR::getJointIndex(this, "PartsControl");
            if (spine < 0 || parts < 0) {
                return;
            }
            pMem = operator new(GCOOP_XANIME_SIZE);
            if (!pMem) {
                return;
            }
            XanimePlayer *pUpper = __ct__12XanimePlayerFP8J3DModelP19XanimeResourceTableP12XanimePlayer(pMem, pModel, pTable, pBody);
            pUpper->changeAnimationBck(pInitial);
            shareJointTransform__10XanimeCoreFPC10XanimeCore(xanimeCore(pUpper), xanimeCore(pBody));
            mUpper = pUpper;
            mSpineJoint = spine;
            mPartsJoint = parts;
        }

        void applyState(const GCoopPlayerState &rState) {
            mTranslation.set(rState.pos[0], rState.pos[1], rState.pos[2]);
            if (isUsable(rState.up)) {
                mUp.set(rState.up[0], rState.up[1], rState.up[2]);
            }
            if (isUsable(rState.front)) {
                mFront.set(rState.front[0], rState.front[1], rState.front[2]);
            }
            if (mBody) {
                followAnimation(rState);
            }
            else {
                playBck(rState.bckName);
            }
            rideYoshi(rState);
        }

        void rideYoshi(const GCoopPlayerState &rState) {
            if (!mYoshi) {
                return;
            }
            bool on = (rState.flags & GCOOP_PF_YOSHI) != 0;
            if (!on) {
                if (!MR::isDead(mYoshi)) {
                    mYoshi->kill();
                }
                return;
            }
            if (MR::isDead(mYoshi)) {
                mYoshi->appear();
            }
            TVec3f pos;
            pos.set(rState.pos[0], rState.pos[1], rState.pos[2]);
            f32 vx = rState.vel[0], vy = rState.vel[1], vz = rState.vel[2];
            mYoshi->place(pos, mUp, mFront, vx * vx + vy * vy + vz * vz > 25.0f);
        }

        void followAnimation(const GCoopPlayerState &rState) {
            if (rState.bckName[0]) {
                followGroup(mBody, rState.bckName, mBckName, mBodyGroup);
            }
            f32 *pWeights = GCoop::xanimeTrackWeights(mBody);
            for (s32 i = 0; i < GCOOP_ANIM_TRACKS; i++) {
                pWeights[i] = rState.trackWeight[i];
            }
            followFrame(mBody, rState.bckFrame, rState.bckRate);

            mUpperOn = mUpper && rState.upperName[0];
            if (mUpperOn) {
                followGroup(mUpper, rState.upperName, mUpperName, mUpperGroup);
                followFrame(mUpper, rState.upperFrame, rState.upperRate);
            }
        }

        /* The model manager only updates the body; the upper layer is driven here, as MarioAnimator does. */
        virtual void control() {
            if (mUpperOn) {
                mUpper->updateBeforeMovement();
                mUpper->updateAfterMovement();
            }
        }

        /* Mario's calcAnim: the upper player owns Spine1 and PartsControl for this model's calc only. */
        virtual void calcAnim() {
            bool upper = mUpperOn;
            if (upper) {
                mUpper->calcAnm(mSpineJoint);
                mUpper->overWriteMtxCalc(mPartsJoint);
            }
            LiveActor::calcAnim();
            if (upper) {
                /* Joints live in the J3DModelData, which the local player's model shares. */
                mUpper->clearMtxCalc(mSpineJoint);
                mUpper->clearMtxCalc(mPartsJoint);
                /* ModelManager::calcAnim does this for the body; updateAfterMovement only advances while both are clear. */
                ((u8 *)mUpper)[GCOOP_XANIME_CALCULATED] = 0;
                ((u8 *)mUpper)[GCOOP_XANIME_CALCULATED + 1] = 0;
            }
        }

        void playBck(const char *pName) {
            if (pName[0] == 0 || strcmp(pName, mBckName) == 0) {
                return;
            }
            GCoop::copyName(mBckName, pName, sizeof(mBckName));
            if (!MR::startBckIfExist(this, pName) && !MR::isBckPlaying(this, "Wait")) {
                MR::startBckIfExist(this, "Wait");
            }
        }

        virtual void appear() {
            LiveActor::appear();
            mBckName[0] = 0;
            mUpperName[0] = 0;
            mUpperOn = false;
        }

        virtual void kill() {
            if (mYoshi && !MR::isDead(mYoshi)) {
                mYoshi->kill();
            }
            LiveActor::kill();
        }

    private:
        const char *mModelName;
        TVec3f mUp;
        TVec3f mFront;
        XanimePlayer *mBody;
        XanimePlayer *mUpper;
        const void *mBodyGroup;
        const void *mUpperGroup;
        u16 mSpineJoint;
        u16 mPartsJoint;
        bool mUpperOn;
        char mBckName[GCOOP_BCK_NAME_LEN];
        char mUpperName[GCOOP_BCK_NAME_LEN];
        YoshiMount *mYoshi;
    };

    RemotePlayerActor *sGhosts[GCOOP_GHOST_SLOTS][2];

    bool readRemote(s32 slot, GCoopPlayerState *pOut) {
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
}

namespace GCoop {
    void initRemotePlayers(MarioActor *pMario) {
        XanimeResourceTable *pTable = 0;
        const char *pInitial = 0;
#ifndef GCOOP_GHOST_NPC
        pTable = getPlayerAnimTable(pMario);
        XanimePlayer *pMarioBody = getPlayerXanime(pMario);
        if (pMarioBody) {
            pInitial = xanimeGroupName(pMarioBody);
        }
#endif
        if (!pInitial || !pInitial[0]) {
            pInitial = "Wait";
        }
        u32 spawned = 0;
        for (s32 i = 0; i < GCOOP_GHOST_SLOTS; i++) {
            for (s32 c = 0; c < 2; c++) {
                RemotePlayerActor *pGhost = new RemotePlayerActor(sModelNames[c]);
                pGhost->initWithoutIter();
                pGhost->usePlayerAnimTable(pTable, pInitial);
                sGhosts[i][c] = pGhost;
                spawned++;
            }
        }
        gMailbox.debug[GCOOP_DBG_REMOTE_SPAWNED] = spawned;
        GCOOP_LOG("created %d ghost actors\n", spawned);
    }

    void updateRemotePlayers() {
        const char *pStage = MR::getCurrentStageName();
        s32 scenario = MR::getCurrentScenarioNo();
        GCoopPlayerState state;

        for (s32 i = 0; i < GCOOP_GHOST_SLOTS; i++) {
            if (!readRemote(i, &state)) {
                continue;
            }
            state.bckName[GCOOP_BCK_NAME_LEN - 1] = 0;
            state.upperName[GCOOP_BCK_NAME_LEN - 1] = 0;
            state.stage[GCOOP_STAGE_NAME_LEN - 1] = 0;

            bool visible = state.active
                && (state.flags & GCOOP_PF_IN_SCENE)
                && !(state.flags & GCOOP_PF_HIDDEN)
                && state.scenario == scenario
                && strcmp(state.stage, pStage) == 0;
            s32 body = state.character ? 1 : 0;

            for (s32 c = 0; c < 2; c++) {
                RemotePlayerActor *pGhost = sGhosts[i][c];
                if (!pGhost) {
                    continue;
                }
                if (visible && c == body) {
                    pGhost->applyState(state);
                    if (MR::isDead(pGhost)) {
                        pGhost->appear();
                        pGhost->applyState(state);
                    }
                }
                else if (!MR::isDead(pGhost)) {
                    pGhost->kill();
                }
            }
        }
    }
}
