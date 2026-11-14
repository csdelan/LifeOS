import { Link, Outlet, useBlocker, useRouterState } from "@tanstack/react-router";
import { useTheme } from "next-themes";
import { lazy, useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  LogOutIcon,
  MenuIcon,
  MicIcon,
  MoonIcon,
  PenLineIcon,
  PlusIcon,
  SearchIcon,
  SparklesIcon,
  SunIcon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { OnceOpened } from "@/components/shell/once-opened";
import { UnsavedEditsCtx, type UnsavedEdits } from "@/components/shell/unsaved-edits";
import { ResizeHandle } from "@/components/shell/panels";
import { usePersistedWidth } from "@/components/shell/use-persisted-width";
import { NAV_PAGES } from "@/components/shell/pages";
import { CreateActions, type CreateOpts } from "@/components/create/create-context";
import { RunningTimer } from "@/components/timer/running-timer";
import { useAuth } from "@/components/auth/auth-context";
import { isLiveApi } from "@/lib/api-base";
import { useIdleAfterFirstPage } from "@/lib/idle";
import { useInbox, useMe } from "@/lib/queries";
import { cn } from "@/lib/utils";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet";

// The palette and the create dialogs are closed until asked for, so each loads after the first
// page, when the browser is idle or on its first open, rather than with every page (code review CR84).
const CommandPalette = lazy(() => import("@/components/shell/command-palette").then((m) => ({ default: m.CommandPalette })));
const NewSubjectDialog = lazy(() =>
  import("@/components/create/new-subject-dialog").then((m) => ({ default: m.NewSubjectDialog })),
);
const QuickCaptureDialog = lazy(() =>
  import("@/components/create/quick-capture-dialog").then((m) => ({ default: m.QuickCaptureDialog })),
);
const VoiceCaptureDialog = lazy(() =>
  import("@/components/create/voice-capture-dialog").then((m) => ({ default: m.VoiceCaptureDialog })),
);

export function AppShell() {
  const [palette, setPalette] = useState(false);
  const [createOpts, setCreateOpts] = useState<CreateOpts | null>(null);
  const [capturing, setCapturing] = useState(false);
  const [voice, setVoice] = useState(false);
  const [navOpen, setNavOpen] = useState(false);
  const [dirty, setDirtyState] = useState(false);
  // Read where the answer can't wait for a render: Discard clears it and moves in the same click,
  // and a blocker reading the rendered value blocked that move and asked again (code review CR92).
  const dirtyRef = useRef(false);
  const setDirty = useCallback((next: boolean) => {
    dirtyRef.current = next;
    setDirtyState(next);
  }, []);
  const [discards, setDiscards] = useState(0);
  const idle = useIdleAfterFirstPage();
  const pendingNav = useRef<(() => void) | null>(null);
  const { resolvedTheme, setTheme } = useTheme();
  const { data: inbox } = useInbox();
  const { data: me } = useMe();
  const { signOut } = useAuth();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const navPanel = usePersistedWidth("layout.navWidth", 248, { min: 176, max: 400 });

  // A move to another page closes the phone's navigation, as the render that sees it.
  const [navPath, setNavPath] = useState(pathname);
  if (navPath !== pathname) {
    setNavPath(pathname);
    setNavOpen(false);
  }

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      const meta = e.metaKey || e.ctrlKey;
      if (meta && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setPalette(true);
      }
      if (meta && e.key.toLowerCase() === "n") {
        e.preventDefault();
        setCreateOpts({});
      }
      if (e.altKey && e.key.toLowerCase() === "n") {
        e.preventDefault();
        setCapturing(true);
      }
      if (e.altKey && e.key.toLowerCase() === "v") {
        e.preventDefault();
        setVoice(true);
      }
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const blocker = useBlocker({
    shouldBlockFn: () => dirtyRef.current,
    withResolver: true,
    enableBeforeUnload: dirty,
  });

  // Asked by a move the shell makes itself (requestNavigation); the router's own moves ask by
  // being blocked.
  const [asked, setAsked] = useState(false);
  const stayOpen = asked || blocker.status === "blocked";

  const requestNavigation = useCallback((proceed: () => void) => {
    if (!dirtyRef.current) {
      proceed();
      return;
    }
    pendingNav.current = proceed;
    setAsked(true);
  }, []);

  /**
   * Stay, by the button or by Escape: the move is dropped and the router let go of it. Left
   * blocked, the router waited on it, and every later link did nothing (code review CR92).
   * After Discard it finds nothing left to drop.
   */
  function stay() {
    setAsked(false);
    pendingNav.current = null;
    if (blocker.status === "blocked") blocker.reset?.();
  }

  function discard() {
    const next = pendingNav.current;
    pendingNav.current = null;
    setDirty(false);
    setDiscards((n) => n + 1);
    setAsked(false);
    if (blocker.status === "blocked") blocker.proceed?.();
    else next?.();
  }

  const ctx = useMemo<UnsavedEdits>(
    () => ({ dirty, setDirty, requestNavigation, discards }),
    [dirty, setDirty, requestNavigation, discards],
  );

  const createActions = useMemo(
    () => ({
      openNew: (opts?: CreateOpts) => setCreateOpts(opts ?? {}),
      openCapture: () => setCapturing(true),
      openVoice: () => setVoice(true),
    }),
    [],
  );

  const nav = (
    <NavBody
      pathname={pathname}
      inboxCount={inbox?.length}
      onNew={() => setCreateOpts({})}
      onCapture={() => setCapturing(true)}
      onVoice={() => setVoice(true)}
      onPalette={() => setPalette(true)}
      theme={resolvedTheme}
      onToggleTheme={() => setTheme(resolvedTheme === "dark" ? "light" : "dark")}
      accountEmail={isLiveApi() ? me?.email : undefined}
      onSignOut={() => void signOut()}
    />
  );

  return (
    <UnsavedEditsCtx.Provider value={ctx}>
      <CreateActions.Provider value={createActions}>
      <div className="flex h-svh flex-col overflow-hidden bg-background md:flex-row">
      <a href="#main" className="skip-link">
        Skip to main content
      </a>
      <header className="flex shrink-0 items-center gap-2 border-b bg-sidebar px-2 py-1.5 md:hidden">
        <Button
          variant="ghost"
          size="icon"
          aria-label="Open navigation"
          aria-expanded={navOpen}
          aria-controls="mobile-nav"
          onClick={() => setNavOpen(true)}
        >
          <MenuIcon />
        </Button>
        <p className="font-heading text-lg leading-none">LifeOS</p>
        <RunningTimer compact className="ml-1" />
        <div className="ml-auto flex items-center gap-1">
          <Button
            variant="ghost"
            size="icon"
            aria-label="Command palette"
            onClick={() => setPalette(true)}
          >
            <SearchIcon />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            aria-label="Quick capture"
            onClick={() => setCapturing(true)}
          >
            <PenLineIcon />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            aria-label="Voice capture"
            onClick={() => setVoice(true)}
          >
            <MicIcon />
          </Button>
          <Button size="icon" aria-label="New subject" onClick={() => setCreateOpts({})}>
            <PlusIcon />
          </Button>
        </div>
      </header>
      <aside
        className="relative hidden shrink-0 flex-col border-r bg-sidebar text-sidebar-foreground md:flex"
        style={{ width: navPanel.width }}
      >
        <ResizeHandle panel={navPanel} label="Resize the navigation" edge="end" />
        {nav}
      </aside>
      <Sheet open={navOpen} onOpenChange={setNavOpen}>
        <SheetContent
          id="mobile-nav"
          side="left"
          className="w-[min(20rem,92vw)] gap-0 p-0 sm:max-w-none data-[side=left]:w-[min(20rem,92vw)] data-[side=left]:sm:max-w-none"
        >
          <SheetHeader className="sr-only">
            <SheetTitle>Navigation</SheetTitle>
            <SheetDescription>Primary destinations in LifeOS</SheetDescription>
          </SheetHeader>
          <div className="flex h-full flex-col bg-sidebar text-sidebar-foreground">{nav}</div>
        </SheetContent>
      </Sheet>
      <main id="main" className="min-w-0 flex-1 overflow-hidden" tabIndex={-1}>
        <Outlet />
      </main>
      </div>
      <OnceOpened open={palette || idle}>
        <CommandPalette
          open={palette}
          onOpenChange={setPalette}
          onNew={() => setCreateOpts({})}
          onCapture={() => setCapturing(true)}
          onVoice={() => setVoice(true)}
        />
      </OnceOpened>
      <OnceOpened open={createOpts !== null || idle}>
        <NewSubjectDialog
          open={createOpts !== null}
          onOpenChange={(o) => {
            if (!o) setCreateOpts(null);
          }}
          initialType={createOpts?.type}
          initialParent={createOpts?.parent}
          initialTitle={createOpts?.title}
          initialLink={createOpts?.link}
          onCreated={createOpts?.onCreated}
        />
      </OnceOpened>
      <OnceOpened open={capturing || idle}>
        <QuickCaptureDialog open={capturing} onOpenChange={setCapturing} />
      </OnceOpened>
      <OnceOpened open={voice || idle}>
        <VoiceCaptureDialog open={voice} onOpenChange={setVoice} />
      </OnceOpened>
      {/* Closing it any way but Discard (Stay, Escape) is staying. Discard's click runs before
          the dialog closes, so the stay that follows finds nothing left to drop. */}
      <AlertDialog open={stayOpen} onOpenChange={(open) => (open ? setAsked(true) : stay())}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Unsaved edits</AlertDialogTitle>
            <AlertDialogDescription>
              Save, discard, or stay. Status and attributes are not autosaved.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Stay</AlertDialogCancel>
            <AlertDialogAction variant="outline" onClick={discard}>
              Discard
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
      </CreateActions.Provider>
    </UnsavedEditsCtx.Provider>
  );
}

function NavBody({
  pathname,
  inboxCount,
  onNew,
  onCapture,
  onVoice,
  onPalette,
  theme,
  onToggleTheme,
  accountEmail,
  onSignOut,
}: {
  pathname: string;
  inboxCount?: number;
  onNew: () => void;
  onCapture: () => void;
  onVoice: () => void;
  onPalette: () => void;
  theme?: string;
  onToggleTheme: () => void;
  accountEmail?: string;
  onSignOut: () => void;
}) {
  return (
    <>
      <div className="flex items-center gap-2 px-4 py-4">
        <span className="flex size-8 items-center justify-center rounded-lg bg-primary font-heading text-lg text-primary-foreground">
          ⌘
        </span>
        <div>
          <p className="font-heading text-lg leading-none">LifeOS</p>
          <p className="text-[0.6875rem] text-muted-foreground">Command center</p>
        </div>
      </div>
      <nav className="flex flex-1 flex-col gap-0.5 px-2" aria-label="Primary">
        {NAV_PAGES.map((item) => {
          const active =
            item.to === "/"
              ? pathname === "/"
              : pathname === item.to || pathname.startsWith(item.to + "/");
          const Icon = item.icon;
          const count = item.to === "/inbox" ? inboxCount : undefined;
          return (
            <Link
              key={item.to}
              to={item.to}
              aria-current={active ? "page" : undefined}
              className={cn(
                "flex min-h-11 items-center gap-2 rounded-lg px-2.5 py-1.5 text-sm transition-colors ease-hearth md:min-h-0",
                active
                  ? "bg-sidebar-accent font-medium text-sidebar-accent-foreground"
                  : // Fade the label and icon, not the whole link, so the count badge keeps its contrast.
                    "text-sidebar-foreground/70 hover:bg-sidebar-accent hover:text-sidebar-foreground",
              )}
            >
              <Icon className="size-4" aria-hidden />
              {item.label}
              {typeof count === "number" && count > 0 ? (
                <span
                  className="ml-auto rounded-full bg-attention/15 px-1.5 text-[0.6875rem] font-medium text-attention"
                  aria-label={`${count} inbox items`}
                >
                  {count}
                </span>
              ) : null}
            </Link>
          );
        })}
      </nav>
      <RunningTimer className="mx-2 mb-2" />
      <div className="space-y-1 border-t p-2">
        <Link
          to="/gallery"
          aria-current={pathname === "/gallery" ? "page" : undefined}
          className={cn(
            "flex min-h-11 items-center gap-2 rounded-lg px-2.5 py-1.5 text-sm text-muted-foreground hover:bg-sidebar-accent md:min-h-0",
            pathname === "/gallery" && "bg-sidebar-accent text-foreground",
          )}
        >
          <SparklesIcon className="size-4" aria-hidden /> Gallery
        </Link>
        <div className="hidden gap-1 md:flex">
          <Button className="flex-1" size="sm" onClick={onNew}>
            <PlusIcon /> New
          </Button>
          <Button variant="outline" size="sm" onClick={onCapture}>
            Capture
          </Button>
          <Button
            variant="outline"
            size="icon-sm"
            aria-label="Voice capture"
            onClick={onVoice}
          >
            <MicIcon />
          </Button>
          <Button
            variant="outline"
            size="icon-sm"
            aria-label={theme === "dark" ? "Switch to light theme" : "Switch to dark theme"}
            onClick={onToggleTheme}
          >
            {theme === "dark" ? <SunIcon /> : <MoonIcon />}
          </Button>
        </div>
        <div className="flex gap-1 md:hidden">
          <Button
            variant="outline"
            size="sm"
            className="flex-1"
            aria-label={theme === "dark" ? "Switch to light theme" : "Switch to dark theme"}
            onClick={onToggleTheme}
          >
            {theme === "dark" ? <SunIcon /> : <MoonIcon />}
            Theme
          </Button>
        </div>
        <button
          type="button"
          onClick={onPalette}
          className="flex min-h-11 w-full items-center justify-between rounded-lg px-2.5 py-1.5 text-xs text-muted-foreground hover:bg-sidebar-accent focus-visible:ring-2 focus-visible:ring-ring/50 md:min-h-0"
        >
          Command palette
          <kbd className="hidden rounded border bg-background px-1.5 py-0.5 font-sans text-[0.625rem] md:inline">
            ⌘K
          </kbd>
        </button>
        {accountEmail ? (
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button
                type="button"
                className="flex min-h-11 w-full items-center gap-2 rounded-lg px-2.5 py-1.5 text-left text-xs text-muted-foreground hover:bg-sidebar-accent focus-visible:ring-2 focus-visible:ring-ring/50 md:min-h-0"
                aria-label={`Signed in as ${accountEmail}`}
              >
                <span className="min-w-0 flex-1 truncate">{accountEmail}</span>
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" className="w-56">
              <DropdownMenuLabel className="truncate font-normal">
                {accountEmail}
              </DropdownMenuLabel>
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={onSignOut}>
                <LogOutIcon />
                Sign out
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        ) : null}
      </div>
    </>
  );
}
