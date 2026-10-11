#include "GCoop/GCoop.h"

/*
 * Applies events the companion queued for us. An event whose object is not
 * ready yet (a coin that only appears after a switch, a star still in its
 * appear demo) is parked and retried every frame until the scene ends.
 */

#define GCOOP_RETRY_SLOTS 64

namespace {
    GCoopEvent sRetry[GCOOP_RETRY_SLOTS];
    u32 sRetryCount = 0;

    GCoop::ApplyResult apply(const GCoopEvent &rEvent) {
        switch (rEvent.type) {
        case GCOOP_EV_COIN:
        case GCOOP_EV_STAR_PIECE:
        case GCOOP_EV_POWER_STAR:
            return GCoop::applyCollectibleEvent(rEvent);
        default:
            return GCoop::applyWorldEvent(rEvent);
        }
    }

    void park(const GCoopEvent &rEvent) {
        if (sRetryCount == GCOOP_RETRY_SLOTS) {
            /* full: drop the oldest */
            for (u32 i = 1; i < GCOOP_RETRY_SLOTS; i++) {
                sRetry[i - 1] = sRetry[i];
            }
            sRetryCount--;
        }
        sRetry[sRetryCount++] = rEvent;
    }

    GCoop::ApplyResult applyGuarded(const GCoopEvent &rEvent) {
        GCoop::gApplyingRemote = true;
        GCoop::ApplyResult result = apply(rEvent);
        GCoop::gApplyingRemote = false;
        return result;
    }
}

namespace GCoop {
    void resetIncomingEvents() {
        sRetryCount = 0;
    }

    void applyIncomingEvents() {
        u32 kept = 0;
        for (u32 i = 0; i < sRetryCount; i++) {
            if (applyGuarded(sRetry[i]) == APPLY_RETRY) {
                sRetry[kept++] = sRetry[i];
            }
        }
        sRetryCount = kept;

        GCoopEvent ev;
        while (popEvent(&ev)) {
            switch (applyGuarded(ev)) {
            case APPLY_RETRY:
                park(ev);
                break;
            case APPLY_UNKNOWN:
                gMailbox.debug[GCOOP_DBG_KEY_MISMATCH]++;
                GCOOP_LOG("no object for event type %d key %08x\n", ev.type, ev.key);
                break;
            default:
                break;
            }
        }
    }
}
