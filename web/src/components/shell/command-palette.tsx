import { useMemo, useState } from "react";
import { useNavigate } from "@tanstack/react-router";
import { defaultFilter } from "cmdk";
import {
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
  CommandSeparator,
  CommandShortcut,
} from "@/components/ui/command";
import { useSubjects } from "@/lib/queries";
import { typeLabel } from "@/lib/production-ui-types";
import { openTarget, searchSubjects } from "@/lib/browse";
import {
  ActivityIcon,
  CompassIcon,
  GiftIcon,
  HouseIcon,
  MedalIcon,
  InboxIcon,
  LayoutDashboardIcon,
  LibraryIcon,
  ListTodoIcon,
  MapIcon,
  MicIcon,
  MoonIcon,
  PlusIcon,
  RepeatIcon,
  UtensilsIcon,
  SparklesIcon,
  SunIcon,
  UsersIcon,
  EyeIcon,
  ClipboardCheckIcon,
} from "lucide-react";
import { useTheme } from "next-themes";

const SUBJECT_VALUE = "subject:";

/**
 * Subjects arrive already matched and ranked by `searchSubjects`, so they score alike and keep
 * that order; everything else (Navigate, Actions) keeps cmdk's fuzzy match. Their middling
 * score puts a page or action that closely matches ("task" → Tasks) above them, and a loose
 * match below.
 */
function paletteFilter(value: string, search: string, keywords?: string[]): number {
  return value.startsWith(SUBJECT_VALUE) ? 0.5 : defaultFilter(value, search, keywords);
}

export function CommandPalette({
  open,
  onOpenChange,
  onNew,
  onCapture,
  onVoice,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onNew: () => void;
  onCapture: () => void;
  onVoice: () => void;
}) {
  const navigate = useNavigate();
  const { setTheme, theme } = useTheme();
  // Every subject, archived ones too: the search box finds any item, not the first few.
  const { data: subjects } = useSubjects(undefined, true);
  const [query, setQuery] = useState("");
  const found = useMemo(() => searchSubjects(subjects ?? [], query), [subjects, query]);
  // Each opening starts with an empty search.
  const [wasOpen, setWasOpen] = useState(open);
  if (open !== wasOpen) {
    setWasOpen(open);
    if (!open) setQuery("");
  }

  function go(to: string) {
    onOpenChange(false);
    void navigate({ to });
  }

  return (
    <CommandDialog
      open={open}
      onOpenChange={onOpenChange}
      title="Command palette"
      filter={paletteFilter}
    >
      <CommandInput
        placeholder="Go somewhere, find a subject, or create…"
        value={query}
        onValueChange={setQuery}
      />
      <CommandList>
        <CommandEmpty>Nothing matches.</CommandEmpty>
        <CommandGroup heading="Navigate">
          <CommandItem onSelect={() => go("/")}>
            <LayoutDashboardIcon /> Focus <CommandShortcut>G F</CommandShortcut>
          </CommandItem>
          <CommandItem onSelect={() => go("/inbox")}>
            <InboxIcon /> Inbox
          </CommandItem>
          <CommandItem onSelect={() => go("/map")}>
            <MapIcon /> Map
          </CommandItem>
          <CommandItem onSelect={() => go("/browse")}>
            <LibraryIcon /> Browse
          </CommandItem>
          <CommandItem onSelect={() => go("/tasks")}>
            <ListTodoIcon /> Tasks
          </CommandItem>
          <CommandItem onSelect={() => go("/habits")}>
            <RepeatIcon /> Habits
          </CommandItem>
          <CommandItem onSelect={() => go("/food")}>
            <UtensilsIcon /> Food Log
          </CommandItem>
          <CommandItem onSelect={() => go("/activity")}>
            <ActivityIcon /> Activity
          </CommandItem>
          <CommandItem onSelect={() => go("/environment")}>
            <HouseIcon /> Environment
          </CommandItem>
          <CommandItem onSelect={() => go("/rewards")}>
            <GiftIcon /> Rewards
          </CommandItem>
          <CommandItem onSelect={() => go("/character")}>
            <MedalIcon /> Character
          </CommandItem>
          <CommandItem onSelect={() => go("/vision")}>
            <EyeIcon /> Vision
          </CommandItem>
          <CommandItem onSelect={() => go("/reviews")}>
            <ClipboardCheckIcon /> Reviews
          </CommandItem>
          <CommandItem onSelect={() => go("/people")}>
            <UsersIcon /> People
          </CommandItem>
          <CommandItem onSelect={() => go("/areas")}>
            <CompassIcon /> Areas
          </CommandItem>
          <CommandItem onSelect={() => go("/gallery")}>
            <SparklesIcon /> Component gallery
          </CommandItem>
        </CommandGroup>
        <CommandSeparator />
        <CommandGroup heading="Actions">
          <CommandItem
            onSelect={() => {
              onOpenChange(false);
              onNew();
            }}
          >
            <PlusIcon /> New subject <CommandShortcut>⌘N</CommandShortcut>
          </CommandItem>
          <CommandItem
            onSelect={() => {
              onOpenChange(false);
              onCapture();
            }}
          >
            Capture note / idea / problem
            <CommandShortcut>Alt+N</CommandShortcut>
          </CommandItem>
          <CommandItem
            onSelect={() => {
              onOpenChange(false);
              onVoice();
            }}
          >
            <MicIcon /> Voice capture
            <CommandShortcut>Alt+V</CommandShortcut>
          </CommandItem>
          <CommandItem
            onSelect={() => {
              setTheme(theme === "dark" ? "light" : "dark");
              onOpenChange(false);
            }}
          >
            {theme === "dark" ? <SunIcon /> : <MoonIcon />}
            Toggle theme
          </CommandItem>
        </CommandGroup>
        <CommandSeparator />
        {found.matches.length ? (
          <CommandGroup heading="Subjects">
            {found.matches.map((s) => (
              <CommandItem
                key={s.id}
                value={`${SUBJECT_VALUE}${s.id}`}
                onSelect={() => {
                  onOpenChange(false);
                  const target = openTarget(s);
                  if (target.to === "/map") void navigate({ to: "/map", search: target.search });
                  else void navigate({ to: "/browse", search: target.search });
                }}
              >
                {/* One flexing span, so the type sits at the right edge (the item ends in a hidden check). */}
                <span className="flex min-w-0 flex-1 items-center gap-2">
                  <span className="truncate">{s.title}</span>
                  {s.archived ? (
                    <span className="shrink-0 text-xs text-muted-foreground">Archived</span>
                  ) : null}
                  <span className="ml-auto shrink-0 text-xs text-muted-foreground">
                    {typeLabel(s.type)}
                  </span>
                </span>
              </CommandItem>
            ))}
            {found.total > found.matches.length ? (
              <p className="px-2 py-1.5 text-xs text-muted-foreground">
                The best {found.matches.length} of {found.total}. Keep typing to narrow them.
              </p>
            ) : null}
          </CommandGroup>
        ) : null}
      </CommandList>
    </CommandDialog>
  );
}
