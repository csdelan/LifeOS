import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ReadFailed, type Read } from "@/components/primitives/read-state";

function read(isError: boolean, isFetching = false) {
  return { isError, isFetching, refetch: vi.fn<() => unknown>() } satisfies Read;
}

describe("ReadFailed (code review CR85)", () => {
  it("shows nothing while the read hasn't failed", () => {
    const { container } = render(<ReadFailed read={read(false)} what="The steps" />);
    expect(container).toBeEmptyDOMElement();
  });

  it("says what failed and reads it again on Retry", async () => {
    const steps = read(true);
    render(<ReadFailed read={steps} what="The steps" />);
    expect(screen.getByRole("alert")).toHaveTextContent("The steps failed to load.");
    await userEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(steps.refetch).toHaveBeenCalledTimes(1);
  });

  it("says, for a page, the record it isn't", () => {
    render(<ReadFailed read={read(true)} what="Habits" notEmpty="an empty list" size="page" />);
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Habits failed to load");
    expect(alert).toHaveTextContent("This is not an empty list: the read didn't come back.");
    expect(screen.getByRole("button", { name: "Retry" })).toBeEnabled();
  });

  it("retries each read that failed, and only those", async () => {
    const metrics = read(true);
    const catalog = read(false);
    render(<ReadFailed read={[metrics, catalog]} what="Measurements" />);
    await userEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(metrics.refetch).toHaveBeenCalledTimes(1);
    expect(catalog.refetch).not.toHaveBeenCalled();
  });

  it("waits while it's reading again", () => {
    render(<ReadFailed read={read(true, true)} what="The ledger" />);
    expect(screen.getByRole("button", { name: "Retrying…" })).toBeDisabled();
  });

  it("carries the view's own words for what went wrong", () => {
    render(<ReadFailed read={read(true)} what="The calls" detail="no answer" />);
    expect(screen.getByRole("alert")).toHaveTextContent("The calls failed to load: no answer.");
  });
});

// Every view says a failed read with ReadFailed, so each has Retry and the same words. A view
// that writes its own "failed to load" says it without them. (An error that isn't a read, Oura
// not connecting or a review not opening, keeps its own EmptyState and its own Try again.)
describe("a failed read in the views", () => {
  const views = import.meta.glob<string>(["/src/**/*.tsx", "!/src/**/*.test.tsx", "!/src/components/primitives/read-state.tsx"], {
    query: "?raw",
    import: "default",
    eager: true,
  });

  it("is said by ReadFailed", () => {
    const own = Object.entries(views).flatMap(([path, text]) =>
      text
        .split("\n")
        .map((line, i) => ({ line, at: `${path}:${i + 1}` }))
        .filter(({ line }) => /failed to load|didn't load\b/.test(line))
        .map(({ at, line }) => `${at}: ${line.trim()}`),
    );
    expect(own).toEqual([]);
  });
});
