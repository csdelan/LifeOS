import { Link, Outlet, useBlocker, useRouterState } from "@tanstack/react-router";
import { useTheme } from "next-themes";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import {
  ClipboardCheckIcon,
  CompassIcon,
  InboxIcon,
  LayoutDashboardIcon,
  ListTodoIcon,
  MapIcon,
  MenuIcon,
  MoonIcon,
  PenLineIcon,
  PlusIcon,
  RepeatIcon,
  SearchIcon,
  SparklesIcon,
  SunIcon,
  UsersIcon,
  EyeIcon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { CommandPalette } from "@/components/shell/command-palette";
import { ResizeHandle, usePersistedWidth } from "@/components/shell/panels";
import { NewSubjectDialog } from "@/components/create/new-subject-dialog";
import { QuickCaptureDialog } from "@/components/create/quick-capture-dialog";
import { CreateActions, type CreateOpts } from "@/components/create/create-context";
import { useInbox } from "@/lib/queries";
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

const DirtyCtx = createContext<{
  dirty: boolean;
  setDirty: (v: boolean) => void;
  requestNavigation: (proceed: () => void) => void;
}>({ dirty: false, setDirty: () => {}, requestNavigation: (proceed) => proceed() });

export function useDirty() {
  return useContext(DirtyCtx);
}

const NAV = [
  { to: "/", label: "Focus", icon: LayoutDashboardIcon },
  { to: "/inbox", label: "Inbox", icon: InboxIcon },
  { to: "/map", label: "Map", icon: MapIcon },
  { to: "/tasks", label: "Tasks", icon: ListTodoIcon },
  { to: "/habits", label: "Habits", icon: RepeatIcon },
  { to: "/vision", label: "Vision", icon: EyeIcon },
  { to: "/reviews", label: "Reviews", icon: ClipboardCheckIcon },
  { to: "/people", label: "People", icon: UsersIcon },
  { to: "/areas", label: "Areas", icon: CompassIcon },
] as const;

export function AppShell() {
  const [palette, setPalette] = useState(false);
  const [createOpts, setCreateOpts] = useState<CreateOpts | null>(null);
  const [capturing, setCapturing] = useState(false);
  const [navOpen, setNavOpen] = useState(false);
  const [dirty, setDirty] = useState(false);
  const pendingNav = useRef<(() => void) | null>(null);
  const { resolvedTheme, setTheme } = useTheme();
  const { data: inbox } = useInbox();
  const pathname = useRouterState({ select: (s) => s.location.pathname });
  const { width: navWidth, onPointerDown: onNavPointerDown } = usePersistedWidth(
    "layout.navWidth",
    248,
    { min: 176, max: 400 },
  );

  useEffect(() => {
    setNavOpen(false);
  }, [pathname]);

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
    }
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, []);

  const blocker = useBlocker({
    shouldBlockFn: () => dirty,
    withResolver: true,
    enableBeforeUnload: dirty,
  });

  const [stayOpen, setStayOpen] = useState(false);
  useEffect(() => {
    if (blocker.status === "blocked") setStayOpen(true);
  }, [blocker.status]);

  const requestNavigation = useCallback(
    (proceed: () => void) => {
      if (!dirty) {
        proceed();
        return;
      }
      pendingNav.current = proceed;
      setStayOpen(true);
    },
    [dirty],
  );

  const ctx = useMemo(
    () => ({ dirty, setDirty, requestNavigation }),
    [dirty, requestNavigation],
  );

  const createActions = useMemo(
    () => ({
      openNew: (opts?: CreateOpts) => setCreateOpts(opts ?? {}),
      openCapture: () => setCapturing(true),
    }),
    [],
  );

  const nav = (
    <NavBody
      pathname={pathname}
      inboxCount={inbox?.length}
      onNew={() => setCreateOpts({})}
      onCapture={() => setCapturing(true)}
      onPalette={() => setPalette(true)}
      theme={resolvedTheme}
      onToggleTheme={() => setTheme(resolvedTheme === "dark" ? "light" : "dark")}
    />
  );

  return (
    <DirtyCtx.Provider value={ctx}>
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
          <Button size="icon" aria-label="New subject" onClick={() => setCreateOpts({})}>
            <PlusIcon />
          </Button>
        </div>
      </header>
      <aside
        className="relative hidden shrink-0 flex-col border-r bg-sidebar text-sidebar-foreground md:flex"
        style={{ width: navWidth }}
      >
        <ResizeHandle edge="end" onPointerDown={onNavPointerDown} />
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
      <CommandPalette
        open={palette}
        onOpenChange={setPalette}
        onNew={() => setCreateOpts({})}
        onCapture={() => setCapturing(true)}
      />
      <NewSubjectDialog
        open={createOpts !== null}
        onOpenChange={(o) => {
          if (!o) setCreateOpts(null);
        }}
        initialType={createOpts?.type}
        initialParent={createOpts?.parent}
        initialTitle={createOpts?.title}
      />
      <QuickCaptureDialog open={capturing} onOpenChange={setCapturing} />
      <AlertDialog open={stayOpen} onOpenChange={setStayOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Unsaved edits</AlertDialogTitle>
            <AlertDialogDescription>
              Save, discard, or stay. Status and attributes are not autosaved.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel
              onClick={() => {
                setStayOpen(false);
                pendingNav.current = null;
                if (blocker.status === "blocked") blocker.reset?.();
              }}
            >
              Stay
            </AlertDialogCancel>
            <AlertDialogAction
              variant="outline"
              onClick={() => {
                const next = pendingNav.current;
                pendingNav.current = null;
                setDirty(false);
                setStayOpen(false);
                if (blocker.status === "blocked") blocker.proceed?.();
                else next?.();
              }}
            >
              Discard
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
      </CreateActions.Provider>
    </DirtyCtx.Provider>
  );
}

function NavBody({
  pathname,
  inboxCount,
  onNew,
  onCapture,
  onPalette,
  theme,
  onToggleTheme,
}: {
  pathname: string;
  inboxCount?: number;
  onNew: () => void;
  onCapture: () => void;
  onPalette: () => void;
  theme?: string;
  onToggleTheme: () => void;
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
        {NAV.map((item) => {
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
                  : "text-sidebar-foreground opacity-70 hover:bg-sidebar-accent hover:opacity-100",
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
      </div>
    </>
  );
}
