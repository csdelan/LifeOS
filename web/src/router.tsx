import {
  createRootRoute,
  createRoute,
  createRouter,
  type ErrorComponentProps,
} from "@tanstack/react-router";
import { AppShell } from "@/components/shell/app-shell";
import { FocusPage } from "@/pages/focus-page";
import { InboxPage } from "@/pages/inbox-page";
import { MapPage } from "@/pages/map-page";
import { TasksPage } from "@/pages/tasks-page";
import { HabitsPage } from "@/pages/habits-page";
import { RewardsPage } from "@/pages/rewards-page";
import { CharacterPage } from "@/pages/character-page";
import { FoodPage } from "@/pages/food-page";
import { ActivityConnectedPage, ActivityPage } from "@/pages/activity-page";
import { VisionPage } from "@/pages/vision-page";
import { ReviewsPage } from "@/pages/reviews-page";
import { PeoplePage } from "@/pages/people-page";
import { AreasPage } from "@/pages/areas-page";
import { GalleryPage } from "@/pages/gallery-page";
import type { MapLens } from "@/lib/derive";
import type { GraphLayoutKind, MapViewMode, OrphanFilter } from "@/lib/graph-model";
import { Button } from "@/components/ui/button";

export type MapSearch = {
  selected?: string;
  lens?: MapLens;
  archived?: boolean;
  view?: MapViewMode;
  layout?: GraphLayoutKind;
  orphans?: OrphanFilter;
  area?: string;
  colorByArea?: boolean;
};

export type InboxSearch = {
  item?: string;
  /** "resolved" lists items that have left the Inbox; absent is the live queue. */
  view?: "resolved";
};

export type TasksSearch = {
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

export type CharacterSearch = {
  /** "reports" opens the Reports tab (xp-domains plan §3.13); absent is the domain cards. */
  view?: "reports";
};

export type ReviewsSearch = {
  kind?: "daily" | "weekly";
  date?: string;
};

function ErrorView({ error }: ErrorComponentProps) {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-3 p-8 text-center">
      <p className="font-heading text-2xl">Something broke</p>
      <p className="max-w-md text-sm text-muted-foreground">
        {error instanceof Error ? error.message : "Unknown error"}
      </p>
      <Button onClick={() => window.location.assign("/")}>Back to Focus</Button>
    </div>
  );
}

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
  component: InboxPage,
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
  }),
  component: MapPage,
});

const visionRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/vision",
  component: VisionPage,
});

const tasksRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/tasks",
  validateSearch: (search: Record<string, unknown>): TasksSearch => ({
    selected: typeof search.selected === "string" ? search.selected : undefined,
  }),
  component: TasksPage,
});

const habitsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/habits",
  validateSearch: (search: Record<string, unknown>): HabitsSearch => ({
    selected: typeof search.selected === "string" ? search.selected : undefined,
  }),
  component: HabitsPage,
});

const rewardsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/rewards",
  validateSearch: (search: Record<string, unknown>): RewardsSearch => ({
    view: search.view === "history" ? "history" : undefined,
    selected: typeof search.selected === "string" ? search.selected : undefined,
  }),
  component: RewardsPage,
});

const foodRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/food",
  validateSearch: (search: Record<string, unknown>): FoodSearch => ({
    view: search.view === "week" || search.view === "month" ? search.view : undefined,
    date: typeof search.date === "string" && /^\d{4}-\d{2}-\d{2}$/.test(search.date) ? search.date : undefined,
  }),
  component: FoodPage,
});

const activityRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/activity",
  component: ActivityPage,
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
  component: ActivityConnectedPage,
});

const characterRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/character",
  validateSearch: (search: Record<string, unknown>): CharacterSearch => ({
    view: search.view === "reports" ? "reports" : undefined,
  }),
  component: CharacterPage,
});

const reviewsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/reviews",
  validateSearch: (search: Record<string, unknown>): ReviewsSearch => ({
    kind: search.kind === "weekly" || search.kind === "daily" ? search.kind : undefined,
    date: typeof search.date === "string" ? search.date : undefined,
  }),
  component: ReviewsPage,
});

const peopleRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/people",
  component: PeoplePage,
});

const areasRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/areas",
  component: AreasPage,
});

const galleryRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/gallery",
  component: GalleryPage,
});

const routeTree = rootRoute.addChildren([
  indexRoute,
  inboxRoute,
  mapRoute,
  tasksRoute,
  habitsRoute,
  foodRoute,
  activityRoute,
  activityConnectedRoute,
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
});

declare module "@tanstack/react-router" {
  interface Register {
    router: typeof router;
  }
}
