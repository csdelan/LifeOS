import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { TreeNode } from "@/components/primitives/tree-node";
import type { SubjectListItem } from "@/lib/production-ui-types";

function subject(id: string, title: string, type: SubjectListItem["type"] = "Goal"): SubjectListItem {
  return {
    id,
    urn: `urn:lifeos:subject:${id}`,
    type,
    title,
    status: "Active",
    due: null,
    tags: [],
    area: null,
    updatedAt: "2026-11-13T12:00:00Z",
  } as unknown as SubjectListItem;
}

describe("TreeNode (code review CR87)", () => {
  it("is a list item in a nested list, its toggle saying whether it's open", () => {
    render(
      <ul aria-label="Outline">
        <TreeNode subject={subject("goal", "Run a half-marathon")} depth={0} expanded expandable selected>
          <TreeNode subject={subject("task", "Buy race shoes", "Task")} depth={1} />
        </TreeNode>
      </ul>,
    );
    const outline = screen.getByRole("list", { name: "Outline" });
    const children = within(outline).getByRole("list", { name: "Children of Run a half-marathon" });
    expect(within(outline).getAllByRole("listitem")).toHaveLength(2);
    expect(within(children).getByRole("button", { name: "Buy race shoes" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Collapse Run a half-marathon" })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("button", { name: "Run a half-marathon" })).toHaveAttribute("aria-current", "true");
    expect(screen.getByRole("button", { name: "Buy race shoes" })).not.toHaveAttribute("aria-current");
    // An item with nothing under it has no toggle to open.
    expect(screen.getByRole("button", { name: "Expand Buy race shoes" })).not.toHaveAttribute("aria-expanded");
    expect(screen.queryAllByRole("treeitem")).toEqual([]);
  });

  it("adds under the selected item on Enter, and leaves Enter on its other buttons to them", async () => {
    const onCreateChild = vi.fn();
    const onToggle = vi.fn();
    const onSelect = vi.fn();
    render(
      <ul aria-label="Outline">
        <TreeNode
          subject={subject("goal", "Run a half-marathon")}
          depth={0}
          expandable
          selected
          onToggle={onToggle}
          onSelect={onSelect}
          onCreateChild={onCreateChild}
        />
      </ul>,
    );
    screen.getByRole("button", { name: "Run a half-marathon" }).focus();
    await userEvent.keyboard("{Enter}");
    expect(onCreateChild).toHaveBeenCalledTimes(1);
    expect(onSelect).not.toHaveBeenCalled();

    screen.getByRole("button", { name: "Expand Run a half-marathon" }).focus();
    await userEvent.keyboard("{Enter}");
    expect(onToggle).toHaveBeenCalledTimes(1);
    expect(onCreateChild).toHaveBeenCalledTimes(1);
  });

  it("selects an item that isn't selected yet on Enter", async () => {
    const onCreateChild = vi.fn();
    const onSelect = vi.fn();
    render(
      <ul aria-label="Outline">
        <TreeNode subject={subject("goal", "Run a half-marathon")} depth={0} onSelect={onSelect} onCreateChild={onCreateChild} />
      </ul>,
    );
    screen.getByRole("button", { name: "Run a half-marathon" }).focus();
    await userEvent.keyboard("{Enter}");
    expect(onSelect).toHaveBeenCalledTimes(1);
    expect(onCreateChild).not.toHaveBeenCalled();
  });
});

const views = import.meta.glob<string>(["/src/**/*.tsx", "!/src/**/*.test.tsx", "!/src/components/ui/**"], {
  query: "?raw",
  import: "default",
  eager: true,
});

function lines(match: (line: string) => boolean) {
  return Object.entries(views).flatMap(([path, text]) =>
    text
      .split("\n")
      .map((line, i) => ({ line, at: `${path}:${i + 1}` }))
      .filter(({ line }) => match(line))
      .map(({ at }) => at),
  );
}

describe("keyboard access in the views (code review CR87)", () => {
  // An ARIA tree tells a screen reader the arrow keys move through it. The outlines are nested
  // lists with buttons, so they claim nothing they don't do.
  it("claims no tree it can't move through with the arrow keys", () => {
    expect(lines((line) => /role="(tree|treeitem)"/.test(line))).toEqual([]);
  });

  // A control shown only on hover is invisible to the keyboard that focuses it.
  it("shows a control hidden until hover when it has focus too", () => {
    expect(
      lines(
        (line) =>
          /group-hover:opacity-100/.test(line) && !/(focus-visible|group-focus-within):opacity-100/.test(line),
      ),
    ).toEqual([]);
  });
});
