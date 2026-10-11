#include "GCoop/GCoop.h"

namespace GCoop {
    GCoopMailbox gMailbox;
    bool gApplyingRemote = false;

    void initMailbox() {
        if (gMailbox.magic0 == GCOOP_MAGIC0) {
            return;
        }
        gMailbox.version = GCOOP_MAILBOX_VERSION;
        gMailbox.game = GCOOP_GAME_SB4E;
        gMailbox.size = sizeof(GCoopMailbox);
        gMailbox.magic1 = GCOOP_MAGIC1;
        /* magic0 last: the companion only trusts the block once both words are present */
        gMailbox.magic0 = GCOOP_MAGIC0;
        GCOOP_INFO("mailbox at %p (%d bytes)\n", &gMailbox, sizeof(GCoopMailbox));
    }

    bool pushEvent(u16 type, u32 key, s32 arg0, s32 arg1, const TVec3f *pPos, const char *pPayload) {
        GCoopEventRing &ring = gMailbox.eventsOut;
        u32 head = ring.head;
        if (head - ring.tail >= GCOOP_EVENT_RING_SIZE) {
            gMailbox.debug[GCOOP_DBG_EVENTS_DROPPED]++;
            return false;
        }
        GCoopEvent &ev = ring.entries[head % GCOOP_EVENT_RING_SIZE];
        ev.id = head + 1;
        ev.type = type;
        ev.source = 0;
        ev.key = key;
        ev.arg0 = arg0;
        ev.arg1 = arg1;
        if (pPos) {
            ev.pos[0] = pPos->x;
            ev.pos[1] = pPos->y;
            ev.pos[2] = pPos->z;
        } else {
            ev.pos[0] = ev.pos[1] = ev.pos[2] = 0.0f;
        }
        copyName(ev.payload, pPayload, GCOOP_EV_PAYLOAD_LEN);
        ring.head = head + 1;
        gMailbox.debug[GCOOP_DBG_EVENTS_OUT]++;
        return true;
    }

    bool popEvent(GCoopEvent *pOut) {
        GCoopEventRing &ring = gMailbox.eventsIn;
        u32 tail = ring.tail;
        if (tail == ring.head) {
            return false;
        }
        *pOut = ring.entries[tail % GCOOP_EVENT_RING_SIZE];
        ring.tail = tail + 1;
        gMailbox.debug[GCOOP_DBG_EVENTS_IN]++;
        return true;
    }

    u32 hashString(const char *pStr, u32 seed) {
        u32 h = seed ^ 0x811C9DC5;
        if (pStr) {
            while (*pStr) {
                h = (h ^ (u8)*pStr++) * 0x01000193;
            }
        }
        return h;
    }

    u32 hashMix(u32 h, u32 value) {
        for (int i = 0; i < 4; i++) {
            h = (h ^ ((value >> (i * 8)) & 0xFF)) * 0x01000193;
        }
        return h;
    }

    u32 hashPosition(const TVec3f &rPos) {
        /* 10-unit grid, so float noise from different load orders does not change the key */
        s32 v[3];
        v[0] = (s32)(rPos.x * 0.1f);
        v[1] = (s32)(rPos.y * 0.1f);
        v[2] = (s32)(rPos.z * 0.1f);
        u32 h = 0x811C9DC5;
        const u8 *p = (const u8 *)v;
        for (int i = 0; i < (int)sizeof(v); i++) {
            h = (h ^ p[i]) * 0x01000193;
        }
        return h;
    }

    void copyName(char *pDst, const char *pSrc, u32 size) {
        u32 i = 0;
        if (pSrc) {
            for (; i + 1 < size && pSrc[i]; i++) {
                pDst[i] = pSrc[i];
            }
        }
        for (; i < size; i++) {
            pDst[i] = 0;
        }
    }
}
