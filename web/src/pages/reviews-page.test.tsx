import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import type { ReviewDoc } from "@/lib/production-ui-types";
import { ReviewsPage } from "@/pages/reviews-page";

// Monday 9 November 2026: the review shown is the daily one for that day, already open.
const day = "2026-11-09";

function review(extra: Partial<ReviewDoc> = {}): ReviewDoc {
  return {
    id: "review-1",
    urn: "urn:bsk:review:review-1",
    title: "Daily review, Mon Nov 9",
    kind: "daily",
    date: day,
    weekStart: "2026-11-09",
    weekEnd: "2026-11-15",
    planStart: "2026-11-10",
    planEnd: "2026-11-10",
    dueOn: day,
    status: "Open",
    completedAt: null,
    missed: false,
    completedLate: false,
    keepFocus: true,
    reflection: null,
    ...extra,
  };
}

let reviews: ReviewDoc[] = [];

vi.mock("@/lib/dates", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/dates")>()),
  todayIso: () => day,
}));

vi.mock("@/lib/queries", () => ({
  useReviews: () => ({ data: reviews, isLoading: false, isError: false }),
  useWrites: () => ({
    openReview: vi.fn(() => Promise.resolve()),
    saveReview: vi.fn(() => Promise.resolve()),
    completeReview: vi.fn(() => Promise.resolve()),
    adhere: vi.fn(),
  }),
  useDashboard: () => ({ data: undefined }),
  useRecap: () => ({ data: [], isLoading: false, isError: false }),
  useHabits: () => ({ data: [], isLoading: false, isError: false }),
  useOccurrencesBetween: () => ({ data: [], isLoading: false, isError: false }),
}));

vi.mock("@tanstack/react-router", () => ({
  useSearch: () => ({ kind: "daily", date: day }),
  useNavigate: () => vi.fn(),
}));

vi.mock("@/components/shell/unsaved-edits", () => ({
  useDirty: () => ({ dirty: false, setDirty: vi.fn(), requestNavigation: (go: () => void) => go(), discards: 0 }),
}));

// The real dialog records from the microphone; here it shows what it was opened for.
vi.mock("@/components/create/voice-capture-dialog", () => ({
  VoiceCaptureDialog: ({ open, about }: { open: boolean; about?: { id: string; title: string } }) =>
    open ? (
      <div role="dialog" aria-label={about?.title}>
        about {about?.id}
      </div>
    ) : null,
}));

describe("ReviewsPage reflection (reviews plan §4.1)", () => {
  beforeEach(() => {
    reviews = [review()];
  });

  it("asks for a dictation tied to the review, with no answer boxes to type in", async () => {
    render(<ReviewsPage />);

    expect(screen.queryByRole("textbox")).toBeNull();
    expect(screen.getByText(/The nightly sweep files it in your notes vault/)).toBeTruthy();
    expect(screen.queryByRole("link", { name: "Open in Obsidian" })).toBeNull();

    await userEvent.click(screen.getByRole("button", { name: "Dictate reflection" }));

    expect(screen.getByRole("dialog", { name: "Dictate reflection" }).textContent).toContain("about review-1");
  });

  it("shows a linked reflection's summary and opens it in Obsidian", () => {
    reviews = [
      review({
        reflection: {
          path: "10 Reflections/2026-11-09 Daily reflection.md",
          vault: "Chris2.0",
          summary: "Shipped the shed plans and slept badly.",
          linkedAt: "2026-11-10T11:00:00Z",
        },
      }),
    ];
    render(<ReviewsPage />);

    expect(screen.getByText("Shipped the shed plans and slept badly.")).toBeTruthy();
    expect(screen.getByRole("link", { name: "Open in Obsidian" }).getAttribute("href")).toBe(
      "obsidian://open?vault=Chris2.0&file=10%20Reflections%2F2026-11-09%20Daily%20reflection.md",
    );
    expect(screen.getByRole("button", { name: "Dictate more" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Complete review" })).toBeTruthy();
  });
});
