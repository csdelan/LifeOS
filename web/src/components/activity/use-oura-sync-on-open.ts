import { useEffect, useRef } from "react";
import { useOuraStatus, useWrites } from "@/lib/queries";

/**
 * Asks for the Activity page's throttled sync once, when Oura is connected (activity plan
 * §3.8, question 9). The Food Log and Character pages use it, so activity XP lands when
 * Chris opens either page: the API skips the sync when LifeOS synced under 5 minutes ago,
 * settles activity XP either way, and the sync's toasts say what it paid. A failed sync
 * changes nothing here; the Activity page shows Oura's errors.
 */
export function useOuraSyncOnOpen() {
  const oura = useOuraStatus();
  const writes = useWrites();
  const asked = useRef(false);
  const connected = oura.data?.state === "connected";

  useEffect(() => {
    if (asked.current || !connected) return;
    asked.current = true;
    void writes.syncActivity(false).catch(() => undefined);
  }, [connected, writes]);
}
