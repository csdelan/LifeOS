import {
  createRootRoute,
  createRoute,
  createRouter,
  lazyRouteComponent,
} from "@tanstack/react-router";
import { AppShell } from "@/components/shell/app-shell";
// Focus, the landing page, loads with the app; every other page is fetched when it is first
// opened, so the Map's graph layout and the pages' own code stay out of the first download
// (code review CR23).
import { FocusPage } from "@/pages/focus-page";
import type { MapLens } from "@/lib/derive";
import type { GraphLayoutKind, MapViewMode, OrphanFilter } from "@/lib/graph-model";
import { parseMapShow, type MapShow } from "@/lib/map-show";
import { parseBrowseType, type BrowseType } from "@/lib/browse";
import { ErrorView } from "@/components/shell/error-view";

export type MapSearch = {
  selected?: string;
  lens?: MapLens;
  archived?: boolean;
  view?: MapViewMode;
  layout?: GraphLayoutKind;
  orphans?: OrphanFilter;
  area?: string;
  colorByArea?: boolean;
  /** How much finished work shows; absent is Open. */
  show?: MapShow;
};

export type InboxSearch = {
  item?: string;
  /** "resolved" lists items that have left the Inbox; absent is the live queue. */
  view?: "resolved";
};

export type TasksSearch = {
  selected?: string;
};

/** Browse (BROWSE-2): the type looked at, absent for the last one used, and the open item. */
export type BrowseSearch = {
  type?: BrowseType;
  selected?: string;
};

export type HabitsSearch = {
  selected?: string;
};

export type RewardsSearch = {
  /** "history" opens the Rewards History tab (REWARD-5); absent is the wallet and catalog. */
  view?: "history";
  /** An item or Reward opened from the history, shown in the side panel. */
  selected?: string;
};

/** The Food Log (FOOD-4/5): Day by default, or the Week or Month around `date`. */
export type FoodSearch = {
  view?: "week" | "month";
  date?: string;
};

/** What Oura sends back to `/activity/connected`: a code and the state, or an error. */
export type ActivityConnectedSearch = {
  code?: string;
  state?: string;
  error?: string;
  error_description?: string;
};

export type VisionSearch = {
  /** "goals" opens the Goals tab; absent is the identity statements. */
  view?: "goals";
};

export type CharacterSearch = {
  /** "reports" opens the Reports tab (xp-domains plan §3.13); absent is the domain cards. */
  view?: "reports";
};

export type ReviewsSearch = {
  kind?: "daily" | "weekly";
  date?: string;
};

// The shell's own failures; a page's show inside the shell (defaultErrorComponent, code review CR93).
const rootRoute = createRootRoute({
  component: AppShell,
  errorComponent: ErrorView,
});

const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  component: FocusPage,
});

const inboxRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/inbox",
  validateSearch: (search: Record<string, unknown>): InboxSearch => ({
    item: typeof search.item === "string" ? search.item : undefined,
    view: search.view === "resolved" ? "resolved" : undefined,
  }),
  component: lazyRouteComponent(() => import("@/pages/inbox-page"), "InboxPage"),
});

const mapRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/map",
  validateSearch: (search: Record<string, unknown>): MapSearch => ({
    selected: typeof search.selected === "string" ? search.selected : undefined,
    lens:
      search.lens === "Goal" || search.lens === "Project" || search.lens === "Task"
        ? search.lens
        : "all",
    archived:
      search.archived === true || search.archived === "true" || search.archived === "1",
    view: search.view === "graph" || search.view === "outline" ? search.view : undefined,
    layout:
      search.layout === "force" || search.layout === "layered" ? search.layout : undefined,
    orphans:
      search.orphans === "only" || search.orphans === "hide" || search.orphans === "all"
        ? search.orphans
        : undefined,
    area: typeof search.area === "string" && search.area.length > 0 ? search.area : undefined,
    colorByArea:
      search.colorByArea === undefined
        ? undefined
        : search.colorByArea === true ||
          search.colorByArea === "true" ||
          search.colorByArea === "1",
    show: parseMapShow(search.show),
  }),
  component: lazyRouteComponent(() => import("@/pages/map-page"), "MapPage"),
});

/** Every subject, by type and filters, with its detail (BROWSE-2). */
const browseRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/browse",
  validateSearch: (search: Record<string, unknown>): BrowseSearch => ({
    type: parseBrowseType(search.type),
    selected: typeof search.selected === "string" ? search.selected : undefined,
  }),
  component: lazyRouteComponent(() => import("@/pages/browse-page"), "BrowsePage"),
});

const visionRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/vision",
  validateSearch: (search: Record<string, unknown>): VisionSearch => ({
    view: search.view === "goals" ? "goals" : undefined,
  }),
  component: lazyRouteComponent(() => import("@/pages/vision-page"), "VisionPage"),
});

const tasksRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/tasks",
  validateSearch: (search: Record<string, unknown>): TasksSearch => ({
    selected: typeof search.selected === "string" ? search.selected : undefined,
  }),
  component: lazyRouteComponent(() => import("@/pages/tasks-page"), "TasksPage"),
});

const habitsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/habits",
  validateSearch: (search: Record<string, unknown>): HabitsSearch => ({
    selected: typeof search.selected === "string" ? search.selected : undefined,
  }),
  component: lazyRouteComponent(() => import("@/pages/habits-page"), "HabitsPage"),
});

const rewardsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/rewards",
  validateSearch: (search: Record<string, unknown>): RewardsSearch => ({
    view: search.view === "history" ? "history" : undefined,
    selected: typeof search.selected === "string" ? search.selected : undefined,
  }),
  component: lazyRouteComponent(() => import("@/pages/rewards-page"), "RewardsPage"),
});

const foodRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/food",
  validateSearch: (search: Record<string, unknown>): FoodSearch => ({
    view: search.view === "week" || search.view === "month" ? search.view : undefined,
    date: typeof search.date === "string" && /^\d{4}-\d{2}-\d{2}$/.test(search.date) ? search.date : undefined,
  }),
  component: lazyRouteComponent(() => import("@/pages/food-page"), "FoodPage"),
});

const activityRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/activity",
  component: lazyRouteComponent(() => import("@/pages/activity-page"), "ActivityPage"),
});

/** Weight and, later, any metric Chris measures (MEASURE-1, MEASURE-2). */
const measurementsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/measurements",
  component: lazyRouteComponent(() => import("@/pages/measurements-page"), "MeasurementsPage"),
});

/** The home's spaces, ratings and Home Score (HOME-1..3). */
const environmentRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/environment",
  component: lazyRouteComponent(() => import("@/pages/environment-page"), "EnvironmentPage"),
});

const text = (value: unknown) => (typeof value === "string" && value !== "" ? value : undefined);

const activityConnectedRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/activity/connected",
  validateSearch: (search: Record<string, unknown>): ActivityConnectedSearch => ({
    code: text(search.code),
    state: text(search.state),
    error: text(search.error),
    error_description: text(search.error_description),
  }),
  component: lazyRouteComponent(() => import("@/pages/activity-page"), "ActivityConnectedPage"),
});

const characterRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/character",
  validateSearch: (search: Record<string, unknown>): CharacterSearch => ({
    view: search.view === "reports" ? "reports" : undefined,
  }),
  component: lazyRouteComponent(() => import("@/pages/character-page"), "CharacterPage"),
});

const reviewsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/reviews",
  validateSearch: (search: Record<string, unknown>): ReviewsSearch => ({
    kind: search.kind === "weekly" || search.kind === "daily" ? search.kind : undefined,
    date: typeof search.date === "string" ? search.date : undefined,
  }),
  component: lazyRouteComponent(() => import("@/pages/reviews-page"), "ReviewsPage"),
});

const peopleRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/people",
  component: lazyRouteComponent(() => import("@/pages/people-page"), "PeoplePage"),
});

const areasRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/areas",
  component: lazyRouteComponent(() => import("@/pages/areas-page"), "AreasPage"),
});

const galleryRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/gallery",
  component: lazyRouteComponent(() => import("@/pages/gallery-page"), "GalleryPage"),
});

const routeTree = rootRoute.addChildren([
  indexRoute,
  inboxRoute,
  mapRoute,
  browseRoute,
  tasksRoute,
  habitsRoute,
  foodRoute,
  activityRoute,
  activityConnectedRoute,
  measurementsRoute,
  environmentRoute,
  rewardsRoute,
  characterRoute,
  visionRoute,
  reviewsRoute,
  peopleRoute,
  areasRoute,
  galleryRoute,
]);

export const router = createRouter({
  routeTree,
  defaultPreload: "intent",
  scrollRestoration: true,
  // Each page fails on its own, inside the shell, so the navigation stays (code review CR93).
  defaultErrorComponent: ErrorView,
});

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
