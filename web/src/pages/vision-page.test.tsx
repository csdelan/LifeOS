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

describe("VisionPage gallery (COVER-3, Chris's \"Vision page convert to gallery view\")", () => {
  it("lays identity statements out as a gallery: the photo across a tile's top, then the title, area and credit", async () => {
    render(<VisionPage />);
    const gallery = screen.getByRole("region", { name: "Identity statements" });
    expect(within(gallery).getByRole("list")).toHaveClass("sm:grid-cols-2", "lg:grid-cols-3");
    const tile = within(gallery).getByRole("heading", { name: "Steward of my health" }).closest("article")!;
    const photo = within(tile).getByTestId("cover-image");
    // The photo leads the tile, across its top, and carries no credit of its own.
    expect(tile.firstElementChild).toBe(photo);
    expect(photo).toHaveClass("aspect-[3/2]", "w-full");
    expect(photo).toHaveTextContent("");
    expect(photo.querySelector("img")).toHaveAttribute("alt", "");
    expect(photo.querySelector("img")!.style.objectPosition).toBe("50% 60%");
    expect(tile).toHaveTextContent("Health");
    // The credit is a line under the area name, its links beside the tile's button, never in it.
    const credit = within(tile).getByText((_, el) => el?.tagName === "P" && el.textContent === "Photo by Ines on Unsplash");
    const links = within(credit).getAllByRole("link");
    expect(links.map((a) => a.textContent)).toEqual(["Ines", "Unsplash"]);
    for (const link of links) expect(link.closest("button")).toBeNull();

    await userEvent.click(within(tile).getByRole("button", { name: "Steward of my health" }));
    expect(navigate).toHaveBeenCalledWith({ to: "/map", search: { selected: "v1" } });
  });

  it("gives a statement without a cover a quiet panel in the photo's place, so the grid stays even", () => {
    render(<VisionPage />);
    const tile = screen.getByRole("heading", { name: "Present with my family" }).closest("article")!;
    expect(within(tile).queryByTestId("cover-image")).not.toBeInTheDocument();
    expect(tile.firstElementChild).toBe(within(tile).getByTestId("cover-placeholder"));
    expect(tile).toHaveTextContent("Family");
    expect(tile).not.toHaveTextContent("Photo by");
  });

  it("shows long-term goals as smaller tiles in the same style, credited, each opening its goal", async () => {
    render(<VisionPage />);
    const goalsGallery = screen.getByRole("region", { name: "Long-term goals" });
    expect(within(goalsGallery).getByRole("list")).toHaveClass("grid-cols-2", "sm:grid-cols-3", "lg:grid-cols-4");
    const ship = within(goalsGallery).getByRole("heading", { name: "Ship LifeOS" }).closest("article")!;
    expect(ship.firstElementChild).toBe(within(ship).getByTestId("cover-image"));
    expect(ship).toHaveTextContent("2026-12-02");
    expect(ship).toHaveTextContent("Photo by Mina on Unsplash");
    const run = within(goalsGallery).getByRole("heading", { name: "Run a half-marathon" }).closest("article")!;
    expect(within(run).queryByTestId("cover-image")).toBeNull();
    expect(within(run).getByTestId("cover-placeholder")).toBeInTheDocument();

    await userEvent.click(within(ship).getByRole("button", { name: "Ship LifeOS" }));
    expect(navigate).toHaveBeenCalledWith({ to: "/map", search: { selected: "g1" } });
  });

  it("no longer says it isn't a mood board", () => {
    render(<VisionPage />);
    expect(screen.queryByText(/mood board/)).not.toBeInTheDocument();
    expect(screen.getByText(/Identity Statements first/)).toBeInTheDocument();
  });
});
