import { Link, Outlet, useBlocker, useRouterState } from "@tanstack/react-router";
import { useTheme } from "next-themes";
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
} from "react";
import {
  ClipboardCheckIcon,
  CompassIcon,
  InboxIcon,
  LayoutDashboardIcon,
  MapIcon,
  MoonIcon,
  PlusIcon,
  SparklesIcon,
  SunIcon,
  UsersIcon,
  EyeIcon,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { CommandPalette } from "@/components/shell/command-palette";
import { NewSubjectDialog } from "@/components/create/new-subject-dialog";
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

const DirtyCtx = createContext<{
  dirty: boolean;
  setDirty: (v: boolean) => void;
}>({ dirty: false, setDirty: () => {} });

export function useDirty() {
  return useContext(DirtyCtx);
}

const NAV = [
  { to: "/", label: "Focus", icon: LayoutDashboardIcon },
  { to: "/inbox", label: "Inbox", icon: InboxIcon },
  { to: "/map", label: "Map", icon: MapIcon },
  { to: "/vision", label: "Vision", icon: EyeIcon },
  { to: "/reviews", label: "Reviews", icon: ClipboardCheckIcon },
  { to: "/people", label: "People", icon: UsersIcon },
  { to: "/areas", label: "Areas", icon: CompassIcon },
] as const;

export function AppShell() {
  const [palette, setPalette] = useState(false);
  const [creating, setCreating] = useState(false);
  const [dirty, setDirty] = useState(false);
  const { theme, setTheme } = useTheme();
  const { data: inbox } = useInbox();
  const pathname = useRouterState({ select: (s) => s.location.pathname });

  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      const meta = e.metaKey || e.ctrlKey;
      if (meta && e.key.toLowerCase() === "k") {
        e.preventDefault();
        setPalette(true);
      }
      if (meta && e.key.toLowerCase() === "n") {
        e.preventDefault();
        setCreating(true);
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

  const ctx = useMemo(() => ({ dirty, setDirty }), [dirty]);
  const setDirtyStable = useCallback((v: boolean) => setDirty(v), []);
  void setDirtyStable;

  return (
    <DirtyCtx.Provider value={ctx}>
      <div className="flex h-svh overflow-hidden bg-background">
        <aside className="flex w-[15.5rem] shrink-0 flex-col border-r bg-sidebar text-sidebar-foreground">
          <div className="flex items-center gap-2 px-4 py-4">
            <span className="flex size-8 items-center justify-center rounded-lg bg-primary text-primary-foreground font-heading text-lg">
              ⌘
            </span>
            <div>
              <p className="font-heading text-lg leading-none">LifeOS</p>
              <p className="text-[0.6875rem] text-muted-foreground">Command center</p>
            </div>
          </div>
          <nav className="flex flex-1 flex-col gap-0.5 px-2">
            {NAV.map((item) => {
              const active =
                item.to === "/"
                  ? pathname === "/"
                  : pathname === item.to || pathname.startsWith(item.to + "/");
              const Icon = item.icon;
              const count = item.to === "/inbox" ? inbox?.length : undefined;
              return (
                <Link
                  key={item.to}
                  to={item.to}
                  className={cn(
                    "flex items-center gap-2 rounded-lg px-2.5 py-1.5 text-sm transition-colors ease-hearth",
                    active
                      ? "bg-sidebar-accent font-medium text-sidebar-accent-foreground"
                      : "text-sidebar-foreground opacity-70 hover:bg-sidebar-accent hover:opacity-100",
                  )}
                >
                  <Icon className="size-4" />
                  {item.label}
                  {typeof count === "number" && count > 0 ? (
                    <span className="ml-auto rounded-full bg-attention/15 px-1.5 text-[0.6875rem] font-medium text-attention">
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
              className={cn(
                "flex items-center gap-2 rounded-lg px-2.5 py-1.5 text-sm text-muted-foreground hover:bg-sidebar-accent",
                pathname === "/gallery" && "bg-sidebar-accent text-foreground",
              )}
            >
              <SparklesIcon className="size-4" /> Gallery
            </Link>
            <div className="flex gap-1">
              <Button className="flex-1" size="sm" onClick={() => setCreating(true)}>
                <PlusIcon /> New
              </Button>
              <Button
                variant="outline"
                size="icon-sm"
                aria-label="Toggle theme"
                onClick={() => setTheme(theme === "dark" ? "light" : "dark")}
              >
                {theme === "dark" ? <SunIcon /> : <MoonIcon />}
              </Button>
            </div>
            <button
              type="button"
              onClick={() => setPalette(true)}
              className="flex w-full items-center justify-between rounded-lg px-2.5 py-1.5 text-xs text-muted-foreground hover:bg-sidebar-accent"
            >
              Command palette
              <kbd className="rounded border bg-background px-1.5 py-0.5 font-sans text-[0.625rem]">
                ⌘K
              </kbd>
            </button>
          </div>
        </aside>
        <main className="min-w-0 flex-1 overflow-hidden">
          <Outlet />
        </main>
      </div>
      <CommandPalette open={palette} onOpenChange={setPalette} onNew={() => setCreating(true)} />
      <NewSubjectDialog open={creating} onOpenChange={setCreating} />
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
                if (blocker.status === "blocked") blocker.reset?.();
              }}
            >
              Stay
            </AlertDialogCancel>
            <AlertDialogAction
              variant="outline"
              onClick={() => {
                setDirty(false);
                setStayOpen(false);
                if (blocker.status === "blocked") blocker.proceed?.();
              }}
            >
              Discard
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </DirtyCtx.Provider>
  );
}
