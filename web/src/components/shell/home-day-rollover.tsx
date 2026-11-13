import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";

import { useHomeToday } from "@/lib/home-day";

/**
 * When the home day turns while the app is open, drops every cached read and reads again
 * (code review CR58). Reads like Focus's habits and today's Food Log are dated the day they were
 * fetched, so a row kept from yesterday would send yesterday's date with its next write. A reset,
 * unlike a refresh, takes the old rows off the page until the new ones arrive, so there's nothing
 * left from yesterday to tap.
 */
export function HomeDayRollover() {
  const today = useHomeToday();
  const queryClient = useQueryClient();
  const seen = useRef(today);

  useEffect(() => {
    if (seen.current === today) return;
    seen.current = today;
    void queryClient.resetQueries();
  }, [today, queryClient]);

  return null;
}
