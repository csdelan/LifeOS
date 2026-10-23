import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactNode } from "react";
import { describe, expect, it, vi } from "vitest";
import type { CoverRead, SubjectListItem, SubjectType } from "@/lib/production-ui-types";
import { VisionPage } from "@/pages/vision-page";

const navigate = vi.fn();

function cover(photographer: string, positionY = 50): CoverRead {
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
    positionY,
    setAt: "2026-10-23T18:00:00Z",
  };
}

function item(id: string, type: SubjectType, title: string, extra: Partial<SubjectListItem> = {}): SubjectListItem {
  return {
    id,
    urn: `urn:bsk:${type.toLowerCase()}:${id}`,
    type,
    title,
    status: null,
    archived: false,
    createdAt: "2026-01-01T00:00:00.000Z",
    ...extra,
  };
}

const values = [
  item("v1", "Value", "Steward of my health", { areaName: "Health", cover: cover("Ines", 60) }),
  item("v2", "Value", "Present with my family", { areaName: "Family" }),
];
const goals = [
  item("g1", "Goal", "Ship LifeOS", { targetDate: "2026-12-02", cover: cover("Mina") }),
  item("g2", "Goal", "Run a half-marathon"),
];

vi.mock("@/lib/queries", () => ({
  useSubjects: (type: string) => ({
    data: type === "Value" ? values : goals,
    isLoading: false,
    isError: false,
  }),
}));

vi.mock("@tanstack/react-router", () => ({
  Link: ({ children }: { children: ReactNode }) => <a href="#">{children}</a>,
  useNavigate: () => navigate,
}));

describe("VisionPage covers (COVER-3)", () => {
  it("shows a statement's cover across its card, credited, and the card still opens it", async () => {
    render(<VisionPage />);
    const card = screen.getByRole("heading", { name: "Steward of my health" }).closest("article")!;
    const photo = within(card).getByTestId("cover-image");
    expect(photo).toHaveTextContent("Photo by Ines on Unsplash");
    expect(photo.querySelector("img")).toHaveAttribute("alt", "");
    expect(photo.querySelector("img")!.style.objectPosition).toBe("50% 60%");
    const links = within(photo).getAllByRole("link");
    expect(links.map((a) => a.textContent)).toEqual(["Ines", "Unsplash"]);
    // The credit's links sit beside the card's button, never inside it.
    for (const link of links) expect(link.closest("button")).toBeNull();

    await userEvent.click(within(card).getByRole("button", { name: "Steward of my health" }));
    expect(navigate).toHaveBeenCalledWith({ to: "/map", search: { selected: "v1" } });
  });

  it("leaves a statement without a cover as it was", () => {
    render(<VisionPage />);
    const card = screen.getByRole("heading", { name: "Present with my family" }).closest("article")!;
    expect(within(card).queryByTestId("cover-image")).not.toBeInTheDocument();
    expect(card).toHaveTextContent("Family");
  });

  it("puts a 40 px thumbnail before a long-term goal with a cover, credited in its tooltip", () => {
    render(<VisionPage />);
    const ship = screen.getByRole("button", { name: /Ship LifeOS/ });
    const thumb = within(ship).getByTestId("cover-image");
    expect(thumb).toHaveAttribute("title", "Photo by Mina on Unsplash");
    expect(thumb).toHaveStyle({ width: "40px", height: "40px" });
    expect(within(screen.getByRole("button", { name: /Run a half-marathon/ })).queryByTestId("cover-image")).toBeNull();
  });

  it("no longer says it isn't a mood board", () => {
    render(<VisionPage />);
    expect(screen.queryByText(/mood board/)).not.toBeInTheDocument();
    expect(screen.getByText(/Identity Statements first/)).toBeInTheDocument();
  });
});
