import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ListRow } from "@/components/primitives/list-row";
import { formatDay } from "@/lib/dates";
import type { CoverRead, SubjectListItem } from "@/lib/production-ui-types";

const cover: CoverRead = {
  source: "unsplash",
  photoId: "abc",
  url: "https://images.unsplash.com/photo-1?ixid=x",
  color: "#334455",
  width: 3000,
  height: 2000,
  alt: null,
  photographer: "Annie Spratt",
  photographerLink: "https://unsplash.com/@anniespratt?utm_source=lifeos&utm_medium=referral",
  photoLink: "https://unsplash.com/photos/abc?utm_source=lifeos&utm_medium=referral",
  unsplashLink: "https://unsplash.com/?utm_source=lifeos&utm_medium=referral",
  positionY: 30,
  setAt: "2026-10-23T18:00:00Z",
};

const goal: SubjectListItem = {
  id: "g",
  urn: "urn:bsk:goal:g",
  type: "Goal",
  title: "Hike the Colorado Trail",
  status: "Active",
  archived: false,
  createdAt: "2026-01-01T00:00:00.000Z",
  cover,
};

describe("ListRow dates", () => {
  beforeEach(() => {
    vi.useFakeTimers({ toFake: ["Date"] });
    vi.setSystemTime(new Date(2026, 9, 28, 12, 0, 0));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  const trash: SubjectListItem = {
    id: "t",
    urn: "urn:bsk:task:t",
    type: "Task",
    title: "Take out trash",
    status: "Not started",
    due: "2026-09-16",
    scheduled: null,
    archived: false,
    createdAt: "2026-09-16T00:00:00.000Z",
  };

  it("says an open Task is overdue", () => {
    render(<ListRow item={trash} />);
    expect(screen.getByText("42 days overdue")).toBeInTheDocument();
  });

  it("shows a finished Task's dates as plain days, not overdue", () => {
    const { rerender } = render(<ListRow item={{ ...trash, status: "Completed" }} />);
    expect(screen.queryByText(/overdue/)).not.toBeInTheDocument();
    expect(screen.getByText(formatDay("2026-09-16"))).toBeInTheDocument();

    rerender(<ListRow item={{ ...trash, status: "Missed", scheduled: "2026-09-14" }} />);
    expect(screen.queryByText(/overdue/)).not.toBeInTheDocument();
    expect(screen.getByText(`Do ${formatDay("2026-09-14")}`)).toBeInTheDocument();
  });
});

describe("ListRow covers (COVER-3)", () => {
  it("leads with a 36 px thumbnail, credited in its tooltip, when asked", () => {
    render(<ListRow item={goal} withCover />);
    const thumb = screen.getByTestId("cover-image");
    expect(thumb).toHaveAttribute("title", "Photo by Annie Spratt on Unsplash");
    expect(thumb).toHaveStyle({ width: "36px", height: "36px" });
    const img = thumb.querySelector("img")!;
    expect(img).toHaveAttribute("alt", "");
    expect(img.getAttribute("src")).toContain("w=36");
    expect(img.style.objectPosition).toBe("50% 30%");
    // Still one button, named by its title.
    expect(screen.getByRole("button", { name: /Hike the Colorado Trail/ })).toBeInTheDocument();
  });

  it("looks as it always has without a cover, or where rows stay dense", () => {
    const { rerender } = render(<ListRow item={{ ...goal, cover: null }} withCover />);
    expect(screen.queryByTestId("cover-image")).not.toBeInTheDocument();
    rerender(<ListRow item={goal} />);
    expect(screen.queryByTestId("cover-image")).not.toBeInTheDocument();
  });
});
