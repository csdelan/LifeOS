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

const reviewsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/reviews",
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
