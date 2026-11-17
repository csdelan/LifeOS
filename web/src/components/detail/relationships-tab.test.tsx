import { render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { RelationshipsTab } from "@/components/detail/relationships-tab";
import type { RelationEdge } from "@/lib/production-ui-types";

const edge = (type: RelationEdge["type"], title: string, extra: Partial<RelationEdge> = {}): RelationEdge => ({
  relation: type === "Project" ? "results_in" : "serves",
  urn: `urn:bsk:${type.toLowerCase()}:${title}`,
  type,
  subjectId: title,
  title,
  ...extra,
});

let relations: { parents: RelationEdge[]; children: RelationEdge[] } = { parents: [], children: [] };

vi.mock("@/lib/queries", () => ({
  useRelations: () => ({ data: relations, isLoading: false, isError: false }),
  useWrites: () => ({ link: vi.fn() }),
  useSubjectSearch: () => ({ data: [], isFetching: false }),
}));

describe("RelationshipsTab", () => {
  it("names a Goal's Goals its sub-goals, each with its status and target date (focus plan G2)", () => {
    relations = {
      parents: [edge("Value", "Steward of my health")],
      children: [
        edge("Goal", "Run a 10K", { status: "Active", targetDate: "2027-02-01" }),
        edge("Goal", "Run a trail race"),
        edge("Project", "Couch to 5K", { status: "Completed", targetDate: "2026-11-30" }),
      ],
    };
    render(<RelationshipsTab subjectId="half" type="Goal" />);

    const subGoals = screen.getByText("Sub-goal", { selector: "p" }).nextElementSibling as HTMLElement;
    const [tenK, trail] = within(subGoals).getAllByRole("listitem");
    expect(within(tenK).getByText("Active")).toBeInTheDocument();
    expect(within(tenK).getByText(/Feb 1/)).toBeInTheDocument();
    // No status yet is the type's first one.
    expect(within(trail).getByText("New")).toBeInTheDocument();

    // A finished item keeps its status but not its date, which no longer says anything.
    const projects = screen.getByText("Project", { selector: "p" }).nextElementSibling as HTMLElement;
    expect(within(projects).getByText("Completed")).toBeInTheDocument();
    expect(within(projects).queryByText(/Nov 30/)).not.toBeInTheDocument();
    // An Identity Statement has no status.
    const parent = screen.getByText("Steward of my health").closest("li")!;
    expect(within(parent).queryByText("New")).not.toBeInTheDocument();
  });

  it("names any other item's children by their type", () => {
    relations = { parents: [], children: [edge("Task", "Buy shoes", { status: "Not started" })] };
    render(<RelationshipsTab subjectId="couch" type="Project" />);

    expect(screen.getByText("Task", { selector: "p" })).toBeInTheDocument();
    expect(screen.queryByText("Sub-goal")).not.toBeInTheDocument();
  });
});
