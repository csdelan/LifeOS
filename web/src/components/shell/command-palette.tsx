import { useNavigate } from "@tanstack/react-router";
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
import {
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
  ClipboardCheckIcon,
} from "lucide-react";
import { useTheme } from "next-themes";

export function CommandPalette({
  open,
  onOpenChange,
  onNew,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onNew: () => void;
}) {
  const navigate = useNavigate();
  const { setTheme, theme } = useTheme();
  const { data: subjects } = useSubjects();

  function go(to: string) {
    onOpenChange(false);
    void navigate({ to });
  }

  return (
    <CommandDialog open={open} onOpenChange={onOpenChange} title="Command palette">
      <CommandInput placeholder="Go somewhere, find a subject, or create…" />
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
              setTheme(theme === "dark" ? "light" : "dark");
              onOpenChange(false);
            }}
          >
            {theme === "dark" ? <SunIcon /> : <MoonIcon />}
            Toggle theme
          </CommandItem>
        </CommandGroup>
        <CommandSeparator />
        <CommandGroup heading="Subjects">
          {(subjects ?? []).slice(0, 20).map((s) => (
            <CommandItem
              key={s.id}
              value={`${s.title} ${s.type}`}
              onSelect={() => {
                onOpenChange(false);
                void navigate({ to: "/map", search: { selected: s.id } });
              }}
            >
              {s.title}
              <span className="ml-auto text-xs text-muted-foreground">
                {typeLabel(s.type)}
              </span>
            </CommandItem>
          ))}
        </CommandGroup>
      </CommandList>
    </CommandDialog>
  );
}
