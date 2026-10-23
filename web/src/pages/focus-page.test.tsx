import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { DashboardRead } from "@/lib/derive";
import type { CoverRead, HabitOccurrenceRow, SubjectListItem } from "@/lib/production-ui-types";
import { FocusPage } from "@/pages/focus-page";

const adhere = vi.fn();

function row(name: string, occurrenceDate: string): HabitOccurrenceRow {
  return {
    habitId: name.toLowerCase(),
    habitUrn: `urn:bsk:habit:${name.toLowerCase()}`,
    habitName: name,
    occurrenceDate,
    state: "unrecorded",
    allowsPartial: false,
  };
}

// The rows' day in the browser: 9 pm on 20 October in Pacific time is already the 21st in UTC.
const base: DashboardRead = {
  primaryFocus: { title: "No active goal yet", subjectId: "", kind: "goal" },
  objectives: [],
  dueAndOverdue: [],
  nextActions: [],
  inboxCount: 0,
  todayHabits: [row("Walk", "2026-10-20")],
  maintainingHabits: [row("Floss", "2026-10-20")],
  activeGoals: [],
  activeProjects: [],
};
let dashboard: DashboardRead = base;

vi.mock("@/lib/queries", () => ({
  useDashboard: () => ({ data: dashboard, isLoading: false, isError: false }),
  useWrites: () => ({ adhere }),
}));

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children }: { children: ReactNode }) => <a href="#">{children}</a>,
  useNavigate: () => vi.fn(),
}));

describe("FocusPage habit check-offs", () => {
  beforeEach(() => {
    dashboard = base;
  });

  it("records each check-off on its row's date, never leaving the day to the server", async () => {
    render(<FocusPage />);

    const walk = screen.getByText("Walk").closest("li")!;
    await userEvent.click(within(walk).getByRole("button", { name: "Followed" }));
    await userEvent.click(screen.getByRole("button", { name: /Maintaining/ }));
    await userEvent.click(screen.getByRole("button", { name: "Floss: followed today" }));

    expect(adhere.mock.calls).toEqual([
      ["walk", "followed", { on: "2026-10-20" }],
      ["floss", "followed", { on: "2026-10-20" }],
    ]);
  });
});

function cover(photographer: string): CoverRead {
  return {
    source: "unsplash",
    photoId: photographer.toLowerCase(),
    url: "https://images.unsplash.com/photo-1?ixid=x",
    color: "#334455",
    width: 3000,
    height: 2000,
    alt: null,
    photographer,
    photographerLink: `https://unsplash.com/@${photographer.toLowerCase()}?utm_source=lifeos&utm_medium=referral`,
    photoLink: "https://unsplash.com/photos/x?utm_source=lifeos&utm_medium=referral",
    unsplashLink: "https://unsplash.com/?utm_source=lifeos&utm_medium=referral",
    positionY: 40,
    setAt: "2026-10-23T18:00:00Z",
  };
}

function active(id: string, type: "Goal" | "Project", title: string, withCover?: CoverRead): SubjectListItem {
  return {
    id,
    urn: `urn:bsk:${type.toLowerCase()}:${id}`,
    type,
    title,
    status: "Active",
    archived: false,
    createdAt: "2026-01-01T00:00:00.000Z",
    cover: withCover ?? null,
  };
}

describe("FocusPage covers (COVER-3)", () => {
  beforeEach(() => {
    dashboard = base;
  });

  it("runs the focus's cover across the top of its card, credited", () => {
    dashboard = {
      ...base,
      primaryFocus: { title: "Ship LifeOS", subjectId: "g1", kind: "goal", cover: cover("Mina") },
    };
    render(<FocusPage />);
    const card = screen.getByText("Primary focus").closest("[data-slot=card]")!;
    const strip = within(card as HTMLElement).getByTestId("cover-image");
    expect(strip).toHaveTextContent("Photo by Mina on Unsplash");
    expect(within(strip).getAllByRole("link").map((a) => a.textContent)).toEqual(["Mina", "Unsplash"]);
    expect(strip.querySelector("img")).toHaveAttribute("alt", "");
    expect(strip.querySelector("img")!.style.objectPosition).toBe("50% 40%");
  });

  it("has no strip when the focus has no cover", () => {
    render(<FocusPage />);
    const card = screen.getByText("Primary focus").closest("[data-slot=card]")!;
    expect(within(card as HTMLElement).queryByTestId("cover-image")).not.toBeInTheDocument();
  });

  it("leads covered goal and project rows with a thumbnail, and leaves the rest as they were", () => {
    dashboard = {
      ...base,
      activeGoals: [active("g1", "Goal", "Ship LifeOS", cover("Mina")), active("g2", "Goal", "Run a half")],
      activeProjects: [active("p1", "Project", "Production web UI", cover("Sam"))],
    };
    render(<FocusPage />);
    const ship = screen.getByRole("button", { name: /Ship LifeOS/ });
    expect(within(ship).getByTestId("cover-image")).toHaveAttribute("title", "Photo by Mina on Unsplash");
    expect(within(screen.getByRole("button", { name: /Run a half/ })).queryByTestId("cover-image")).toBeNull();
    const web = screen.getByRole("button", { name: /Production web UI/ });
    expect(within(web).getByTestId("cover-image")).toHaveAttribute("title", "Photo by Sam on Unsplash");
  });
});
