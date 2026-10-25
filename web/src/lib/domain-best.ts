/**
 * The last best level and rank celebrated per life domain (xp-domains plan §7.4.7). A new best
 * reached by time alone, as days settle at 4 a.m., is celebrated once, the next time the
 * Character page opens: the page compares the domain's best with what's kept here. A write
 * that celebrates a new best keeps it here too, so the page doesn't celebrate it again.
 *
 * It lives in this browser only (localStorage). When nothing is kept, the page keeps the
 * current best without celebrating.
 */
const KEY = "lifeos.domains.best";

export interface KeptBest {
  level: number;
  rank: number;
}

function readAll(): Record<string, KeptBest> {
  try {
    const raw = localStorage.getItem(KEY);
    const parsed = raw ? (JSON.parse(raw) as unknown) : null;
    return parsed && typeof parsed === "object" ? (parsed as Record<string, KeptBest>) : {};
  } catch {
    return {};
  }
}

/** The best kept for `domain` (`health_xp`), or null when none is. */
export function keptBest(domain: string): KeptBest | null {
  const kept = readAll()[domain];
  return kept && Number.isFinite(kept.level) && Number.isFinite(kept.rank) ? kept : null;
}

/** Keeps `best` as the last celebrated for `domain`. Storage that's full or blocked keeps nothing. */
export function keepBest(domain: string, best: KeptBest) {
  try {
    localStorage.setItem(KEY, JSON.stringify({ ...readAll(), [domain]: { level: best.level, rank: best.rank } }));
  } catch {
    // Without storage the page may celebrate a time-reached best again; nothing else breaks.
  }
}

/**
 * What the page should celebrate for `domain`, whose best is now `best`: "rank" or "level"
 * for a best above the kept one, else null. It keeps `best` either way.
 */
export function newBest(domain: string, best: KeptBest): "rank" | "level" | null {
  const kept = keptBest(domain);
  keepBest(domain, best);
  if (!kept) return null;
  if (best.rank > kept.rank) return "rank";
  if (best.level > kept.level) return "level";
  return null;
}
