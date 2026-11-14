/**
 * After a deploy, a page loaded from the old build asks for files the new build no longer has: a
 * page, a dialog, the Map's graph or the journal, each fetched the first time it's opened.
 * Reloading fetches the new build's page, which names files that exist (code review CR93).
 */

const RELOADED_AT = "lifeos:reloaded-for-new-build";
/** A reload this recent didn't help, so the error is shown rather than reloading again. */
const RELOAD_WINDOW_MS = 60_000;

/** The browsers' words for a lazily loaded file that couldn't be fetched, and Vite's for its CSS. */
const MISSING_CHUNK = [
  "Failed to fetch dynamically imported module", // Chrome, Edge
  "error loading dynamically imported module", // Firefox
  "Importing a module script failed", // Safari
  "Unable to preload CSS", // Vite, a lazily loaded piece's stylesheet
];

export function isMissingChunk(error: unknown): boolean {
  const message = error instanceof Error ? error.message : typeof error === "string" ? error : "";
  return MISSING_CHUNK.some((words) => message.includes(words));
}

export type ReloadEnv = {
  storage: Pick<Storage, "getItem" | "setItem"> | null;
  reload: () => void;
  now: () => number;
};

function browserEnv(): ReloadEnv {
  let storage: ReloadEnv["storage"] = null;
  try {
    storage = window.sessionStorage;
  } catch {
    // Blocked storage: no way to tell a first reload from a loop, so none is tried.
  }
  return { storage, reload: () => window.location.reload(), now: () => Date.now() };
}

/**
 * Reloads to pick up the new build, unless a reload a moment ago didn't help or there's no
 * storage to tell; then it's false, and whoever asked shows the error with a Reload button.
 */
export function reloadForNewBuild(env: ReloadEnv = browserEnv()): boolean {
  try {
    if (!env.storage) return false;
    const last = Number(env.storage.getItem(RELOADED_AT) ?? 0);
    if (env.now() - last < RELOAD_WINDOW_MS) return false;
    env.storage.setItem(RELOADED_AT, String(env.now()));
  } catch {
    return false;
  }
  env.reload();
  return true;
}

/**
 * Every lazily loaded file in a build goes through Vite's loader, which says `vite:preloadError`
 * when one can't be fetched. The error still reaches the error boundary, which shows it while
 * the page reloads, or for good when the reload is held back.
 */
export function listenForNewBuild(target: Window = window, env?: ReloadEnv): () => void {
  const onError = () => {
    reloadForNewBuild(env);
  };
  target.addEventListener("vite:preloadError", onError);
  return () => target.removeEventListener("vite:preloadError", onError);
}
