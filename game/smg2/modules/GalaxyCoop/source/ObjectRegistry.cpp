#include "GCoop/GCoop.h"
#include "GCoop/Addresses.h"
#include "Game/Scene/GameScene.h"

/*
 * Placement runs in the same order on every client for the same stage and
 * scenario, so "the n-th object with this placement hash" is a stable name.
 * The registry is cleared from GameScene::init, before any object of the new
 * scene is created, so it never holds pointers into a destroyed scene.
 */

#define GCOOP_REGISTRY_SIZE 2048

namespace {
    struct Entry {
        LiveActor *mActor;
        u32 mKey;
        u16 mType;
    };

    Entry sEntries[GCOOP_REGISTRY_SIZE];
    u32 sCount = 0;
    bool sOverflowLogged = false;

    bool isTaken(u16 type, u32 key) {
        for (u32 i = 0; i < sCount; i++) {
            if (sEntries[i].mType == type && sEntries[i].mKey == key) {
                return true;
            }
        }
        return false;
    }

    void hookGameSceneInit(GameScene *pThis) {
        GCoop::initMailbox();
        GCoop::lockSceneCharacter();
        GCoop::resetObjectRegistry();
        GCoop::resetIncomingEvents();
        GCoop::resetBosses();
        pThis->GameScene::init();
    }
}

kmCondWritePointer(GCOOP_VT_GAMESCENE + GCOOP_VT_SLOT_INIT, GCOOP_ORIG_GAMESCENE_INIT, hookGameSceneInit);

namespace GCoop {
    void resetObjectRegistry() {
        sCount = 0;
        sOverflowLogged = false;
    }

    u32 placementKey(const JMapInfoIter &rIter) {
        s32 linkId = -1;
        MR::getJMapInfoLinkID(rIter, &linkId);
        TVec3f pos(0.0f, 0.0f, 0.0f);
        MR::getJMapInfoTrans(rIter, &pos);
        u32 h = hashMix(0x811C9DC5, (u32)MR::getPlacedZoneId(rIter));
        h = hashMix(h, (u32)linkId);
        return hashMix(h, hashPosition(pos));
    }

    u32 registerObject(u16 type, LiveActor *pActor, u32 baseKey) {
        u32 existing = findKey(type, pActor);
        if (existing) {
            return existing;
        }
        if (sCount >= GCOOP_REGISTRY_SIZE) {
            if (!sOverflowLogged) {
                sOverflowLogged = true;
                GCOOP_INFO("object registry full; later objects will not sync\n");
            }
            return 0;
        }
        u32 key = baseKey ? baseKey : 1;
        for (u32 ordinal = 1; isTaken(type, key); ordinal++) {
            key = hashMix(baseKey, ordinal);
            if (!key) key = 1;
        }
        Entry &e = sEntries[sCount++];
        e.mActor = pActor;
        e.mKey = key;
        e.mType = type;
        return key;
    }

    u32 registerPlacedObject(u16 type, LiveActor *pActor, const JMapInfoIter &rIter) {
        if (!MR::isValidInfo(rIter)) {
            return 0;
        }
        return registerObject(type, pActor, placementKey(rIter));
    }

    u32 findKey(u16 type, const LiveActor *pActor) {
        for (u32 i = 0; i < sCount; i++) {
            if (sEntries[i].mActor == pActor && sEntries[i].mType == type) {
                return sEntries[i].mKey;
            }
        }
        return 0;
    }

    LiveActor *findObject(u16 type, u32 key) {
        for (u32 i = 0; i < sCount; i++) {
            if (sEntries[i].mKey == key && sEntries[i].mType == type) {
                return sEntries[i].mActor;
            }
        }
        return 0;
    }
}
