import { render, screen, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import type { CoverRead, HabitRow, SubjectListItem } from "@/lib/production-ui-types";
import { HabitsPage } from "@/pages/habits-page";

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
    positionY: 50,
    setAt: "2026-10-29T18:00:00Z",
  };
}

function habit(id: string, name: string, extra: Partial<HabitRow> = {}): HabitRow {
  return {
    id,
    urn: `urn:bsk:habit:${id}`,
    name,
    allowsPartial: false,
    archived: false,
    createdAt: "2026-01-01T00:00:00.000Z",
    currentStreak: 3,
    ...extra,
  };
}

function subject(id: string, title: string, extra: Partial<SubjectListItem> = {}): SubjectListItem {
  return {
    id,
    urn: `urn:bsk:habit:${id}`,
    type: "Habit",
    title,
    status: null,
    archived: false,
    createdAt: "2026-01-01T00:00:00.000Z",
    ...extra,
  };
}

const habits = [
  habit("h1", "Morning walk"),
  habit("h2", "Floss"),
  habit("h3", "Read before bed", { status: "Established" }),
  habit("h4", "Cold shower", { status: "Planned" }),
];
const subjects = [
  subject("h1", "Morning walk", { cover: cover("Ines") }),
  subject("h2", "Floss"),
  subject("h3", "Read before bed", { status: "Established", cover: cover("Mina") }),
  subject("h4", "Cold shower", { status: "Planned", cover: cover("Oskar") }),
];

vi.mock("@/lib/queries", () => ({
  useHabits: () => ({ data: habits, isLoading: false, isError: false }),
  useDashboard: () => ({ data: undefined }),
  useSubjects: () => ({ data: subjects, isLoading: false, isError: false }),
  useWrites: () => ({ adhere: vi.fn(), setStatus: vi.fn() }),
}));

vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => vi.fn(),
  useSearch: () => ({ selected: undefined }),
}));

vi.mock("@/components/shell/app-shell", () => ({
  useDirty: () => ({ setDirty: vi.fn(), requestNavigation: (go: () => void) => go() }),
}));

vi.mock("@/components/shell/panels", () => ({
  PeekPanel: ({ children }: { children: ReactNode }) => <div>{children}</div>,
}));

vi.mock("@/components/detail/subject-detail", () => ({ SubjectDetail: () => null }));

describe("HabitsPage covers (COVER-3)", () => {
  it("puts a 36 px thumbnail before a habit with a cover, credited in its tooltip", () => {
    render(<HabitsPage />);
    const walk = screen.getByRole("button", { name: /Morning walk/ });
    const thumb = within(walk).getByTestId("cover-image");
    expect(thumb).toHaveAttribute("title", "Photo by Ines on Unsplash");
    expect(thumb).toHaveStyle({ width: "36px", height: "36px" });
  });

  it("leaves a habit without a cover as it was", () => {
    render(<HabitsPage />);
    expect(within(screen.getByRole("button", { name: /Floss/ })).queryByTestId("cover-image")).toBeNull();
  });

  it("shows an Established or Planned habit's cover too", () => {
    render(<HabitsPage />);
    const read = screen.getByRole("button", { name: /Read before bed/ });
    expect(within(read).getByTestId("cover-image")).toHaveAttribute("title", "Photo by Mina on Unsplash");
    const shower = screen.getByRole("button", { name: /^Cold shower/ });
    expect(within(shower).getByTestId("cover-image")).toHaveAttribute("title", "Photo by Oskar on Unsplash");
  });
});
