import {
  ActivityIcon,
  ClipboardCheckIcon,
  CompassIcon,
  EyeIcon,
  GiftIcon,
  HouseIcon,
  InboxIcon,
  LayoutDashboardIcon,
  LibraryIcon,
  ListTodoIcon,
  MapIcon,
  MedalIcon,
  RepeatIcon,
  SparklesIcon,
  UsersIcon,
  UtensilsIcon,
  WeightIcon,
  type LucideIcon,
} from "lucide-react";

type Page = {
  to: string;
  label: string;
  icon: LucideIcon;
  /** In the navigation as well as the palette. */
  nav: boolean;
  /** The palette's hint for a key that opens it. */
  shortcut?: string;
};

/**
 * Every page, in the order the navigation and the command palette list them (code review CR97).
 * A new page goes here and in the router; `pages.test.ts` checks the two agree and that an
 * accessibility spec opens each one.
 */
export const PAGES = [
  { to: "/", label: "Focus", icon: LayoutDashboardIcon, nav: true, shortcut: "G F" },
  { to: "/inbox", label: "Inbox", icon: InboxIcon, nav: true },
  { to: "/map", label: "Map", icon: MapIcon, nav: true },
  { to: "/browse", label: "Browse", icon: LibraryIcon, nav: true },
  { to: "/tasks", label: "Tasks", icon: ListTodoIcon, nav: true },
  { to: "/habits", label: "Habits", icon: RepeatIcon, nav: true },
  { to: "/food", label: "Food Log", icon: UtensilsIcon, nav: true },
  { to: "/activity", label: "Activity", icon: ActivityIcon, nav: true },
  { to: "/measurements", label: "Measurements", icon: WeightIcon, nav: true },
  { to: "/environment", label: "Environment", icon: HouseIcon, nav: true },
  { to: "/rewards", label: "Rewards", icon: GiftIcon, nav: true },
  { to: "/character", label: "Character", icon: MedalIcon, nav: true },
  { to: "/vision", label: "Vision", icon: EyeIcon, nav: true },
  { to: "/reviews", label: "Reviews", icon: ClipboardCheckIcon, nav: true },
  { to: "/people", label: "People", icon: UsersIcon, nav: true },
  { to: "/areas", label: "Areas", icon: CompassIcon, nav: true },
  { to: "/gallery", label: "Component gallery", icon: SparklesIcon, nav: false },
] as const satisfies readonly Page[];

/** The pages in the navigation. */
export const NAV_PAGES = PAGES.filter((p) => p.nav);
