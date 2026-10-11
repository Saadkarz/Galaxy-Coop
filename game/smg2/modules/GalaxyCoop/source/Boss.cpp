#include "GCoop/GCoop.h"
#include "GCoop/Addresses.h"
#include "Game/NameObj/NameObjHolder.h"

/*
 * Bosses. Every placed actor whose name is a boss body is remembered, and the
 * player standing near one publishes its position. Everyone else copies that
 * pose onto their own boss, and a player who is still elsewhere in the galaxy
 * is brought to it once. The host's pose wins when both sides are in the fight.
 */

#define GCOOP_BOSS_MAX      48
#define GCOOP_BOSS_RANGE    6000.0f
#define GCOOP_BOSS_SUMMON   1800.0f

extern "C" {
    void add__13NameObjHolderFP7NameObj(NameObjHolder *pHolder, NameObj *pObj);
}

namespace {
    LiveActor *sBosses[GCOOP_BOSS_MAX];
    u32 sBossCount = 0;
    u32 sSummonedKey = 0;

    bool excluded(const char *pName) {
        static const char *const sSkip[] = {
            "Gate", "Planet", "Cloud", "Bomb", "Shot", "Fire", "Baby", "Ball",
            "Shell", "Tail", "Step", "Wind", "Npc", "Statue", "Parts", "Demo", "Pos"
        };
        for (u32 i = 0; i < sizeof(sSkip) / sizeof(sSkip[0]); i++) {
            if (strstr(pName, sSkip[i])) {
                return true;
            }
        }
        return false;
    }

    bool isBossBody(const char *pName) {
        if (!pName || excluded(pName)) {
            return false;
        }
        return strncmp(pName, "Boss", 4) == 0
            || strncmp(pName, "DinoPackun", 10) == 0
            || strncmp(pName, "KoopaJr", 7) == 0
            || strcmp(pName, "KoopaBattle") == 0;
    }

    void note(NameObj *pObj) {
        if (!pObj || sBossCount >= GCOOP_BOSS_MAX || !isBossBody(pObj->mName)) {
            return;
        }
        LiveActor *pActor = static_cast<LiveActor *>(pObj);
        for (u32 i = 0; i < sBossCount; i++) {
            if (sBosses[i] == pActor) {
                return;
            }
        }
        sBosses[sBossCount++] = pActor;
    }

    f32 dist2(const TVec3f &rPos, f32 x, f32 y, f32 z) {
        f32 dx = rPos.x - x;
        f32 dy = rPos.y - y;
        f32 dz = rPos.z - z;
        return dx * dx + dy * dy + dz * dz;
    }

    /* The living boss closest to pPos, as long as it is within maxDist. */
    LiveActor *nearest(const TVec3f &rPos, f32 maxDist) {
        LiveActor *pBest = 0;
        f32 best = maxDist * maxDist;
        for (u32 i = 0; i < sBossCount; i++) {
            LiveActor *pBoss = sBosses[i];
            if (!pBoss || pBoss->mFlags.mIsDead) {
                continue;
            }
            f32 d = dist2(pBoss->mTranslation, rPos.x, rPos.y, rPos.z);
            if (d < best) {
                best = d;
                pBest = pBoss;
            }
        }
        return pBest;
    }

    void hookNameObjAdd(NameObjHolder *pHolder, NameObj *pObj) {
        add__13NameObjHolderFP7NameObj(pHolder, pObj);
        note(pObj);
    }
}

kmBranch(GCOOP_BR_NAMEOBJ_ADD, hookNameObjAdd);

namespace GCoop {
    void resetBosses() {
        sBossCount = 0;
        sSummonedKey = 0;
    }

    void writeBoss() {
        const TVec3f *pPlayer = MR::getPlayerPos();
        LiveActor *pBoss = pPlayer ? nearest(*pPlayer, GCOOP_BOSS_RANGE) : 0;
        GCoopBoss &b = gMailbox.bossOut;
        b.seq++;
        __sync();
        if (pBoss) {
            b.key = hashString(pBoss->mName, 0xB055);
            b.pos[0] = pBoss->mTranslation.x;
            b.pos[1] = pBoss->mTranslation.y;
            b.pos[2] = pBoss->mTranslation.z;
            b.rot[0] = pBoss->mRotation.x;
            b.rot[1] = pBoss->mRotation.y;
            b.rot[2] = pBoss->mRotation.z;
            b.active = 1;
        }
        else {
            b.active = 0;
        }
        __sync();
        b.seq++;
    }

    void applyBossPose() {
        const GCoopBoss &in = gMailbox.bossIn;
        u32 seq = in.seq;
        if ((seq & 1) || seq == 0) {
            return;
        }
        __sync();
        GCoopBoss copy = in;
        __sync();
        if (in.seq != seq) {
            return;
        }
        if (!copy.active) {
            sSummonedKey = 0;
            return;
        }

        TVec3f pos;
        pos.set(copy.pos[0], copy.pos[1], copy.pos[2]);
        LiveActor *pBoss = nearest(pos, GCOOP_BOSS_RANGE);
        if (pBoss) {
            pBoss->mTranslation.set(copy.pos[0], copy.pos[1], copy.pos[2]);
            pBoss->mRotation.set(copy.rot[0], copy.rot[1], copy.rot[2]);
            pBoss->mVelocity.set(0.0f, 0.0f, 0.0f);
        }

        if (sSummonedKey == copy.key || MR::isPlayerDead() || MR::isDemoActive()) {
            return;
        }
        const TVec3f *pPlayer = MR::getPlayerPos();
        if (!pPlayer) {
            return;
        }
        if (dist2(*pPlayer, copy.pos[0], copy.pos[1], copy.pos[2]) < GCOOP_BOSS_SUMMON * GCOOP_BOSS_SUMMON) {
            sSummonedKey = copy.key;
            return;
        }
        TVec3f up;
        MR::getPlayerUpVec(&up);
        TVec3f dest;
        dest.set(copy.pos[0] + up.x * 220.0f, copy.pos[1] + up.y * 220.0f, copy.pos[2] + up.z * 220.0f);
        MR::setPlayerPos(dest);
        sSummonedKey = copy.key;
        GCOOP_INFO("brought to the boss\n");
    }
}
