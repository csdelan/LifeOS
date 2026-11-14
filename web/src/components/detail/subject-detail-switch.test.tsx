import type { ReactNode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { CreateActions } from "@/components/create/create-context";
import { TooltipProvider } from "@/components/ui/tooltip";
import { getState, resetStore } from "@/lib/mock/store";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), message: vi.fn() }),
}));

// Every page that shows an item keeps one panel open and changes the item it shows: a click on
// another row, or Discard in the unsaved-edits dialog and then the click goes through. So these
// render one panel and change its subjectId, as the pages do (code review CR91).

const A = "goal-lifeos";
const B = "goal-half";

function mount(id: string, onDirtyChange?: (dirty: boolean) => void) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: 0 } } });
  const wrap = (node: ReactNode) => (
    <QueryClientProvider client={client}>
      <TooltipProvider>
        <CreateActions.Provider value={{ openNew: () => {}, openCapture: () => {}, openVoice: () => {} }}>
          {node}
        </CreateActions.Provider>
      </TooltipProvider>
    </QueryClientProvider>
  );
  const view = render(wrap(<SubjectDetail subjectId={id} onDirtyChange={onDirtyChange} />));
  return {
    ...view,
    show: (next: string) => view.rerender(wrap(<SubjectDetail subjectId={next} onDirtyChange={onDirtyChange} />)),
  };
}

const heading = (title: string) => screen.findByRole("heading", { name: title }, { timeout: 4000 });

describe("SubjectDetail when another item opens (code review CR91)", () => {
  beforeEach(() => {
    resetStore();
  });

  it("shows the new item read-only, and Save can't write the first item's form onto it", async () => {
    const titleA = getState().details[A]!.title;
    const titleB = getState().details[B]!.title;
    const user = userEvent.setup();
    const panel = mount(A);
    await heading(titleA);

    await user.click(screen.getByRole("button", { name: "Edit" }));
    const title = screen.getByLabelText("Title");
    await user.clear(title);
    await user.type(title, "Typed for the first item");

    panel.show(B);

    expect(await heading(titleB)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Save" })).not.toBeInTheDocument();

    // Editing the new item starts from its own values, so a save changes only what was typed.
    await user.click(screen.getByRole("button", { name: "Edit" }));
    expect(screen.getByLabelText("Title")).toHaveValue(titleB);
    const description = screen.getByLabelText("Description");
    await user.clear(description);
    await user.type(description, "Typed for the second item.");
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(await screen.findByRole("button", { name: "Edit" }, { timeout: 4000 })).toBeInTheDocument();
    expect(getState().details[B]?.title).toBe(titleB);
    expect(getState().details[B]?.scope).toBe("Typed for the second item.");
    expect(getState().details[A]?.title).toBe(titleA);
  });

  it("drops an entry being written in the journal", async () => {
    const user = userEvent.setup();
    // The second item was shown before, so it's read from the cache and the panel never shows
    // its loading state in between, the way going back to an item works.
    const panel = mount(B);
    await heading(getState().details[B]!.title);
    panel.show(A);
    await heading(getState().details[A]!.title);
    await user.click(screen.getByRole("tab", { name: /Journal/ }));
    await user.click(await screen.findByRole("button", { name: "Append" }, { timeout: 4000 }));
    expect(await screen.findByRole("button", { name: "Heading" })).toBeInTheDocument();

    panel.show(B);

    await heading(getState().details[B]!.title);
    expect(await screen.findByRole("button", { name: "Append" }, { timeout: 4000 })).toBeEnabled();
    expect(screen.queryByRole("button", { name: "Heading" })).not.toBeInTheDocument();
  });

  it("stays on the tab that was open", async () => {
    const user = userEvent.setup();
    const panel = mount(A);
    await heading(getState().details[A]!.title);
    await user.click(screen.getByRole("tab", { name: /History/ }));

    panel.show(B);

    await heading(getState().details[B]!.title);
    expect(screen.getByRole("tab", { name: /History/ })).toHaveAttribute("aria-selected", "true");
  });

  it("says nothing is left unsaved once another item opens or the panel closes", async () => {
    const onDirtyChange = vi.fn();
    const user = userEvent.setup();
    const panel = mount(A, onDirtyChange);
    await heading(getState().details[A]!.title);
    await user.click(screen.getByRole("button", { name: "Edit" }));
    await user.type(screen.getByLabelText("Description"), " more");
    await waitFor(() => expect(onDirtyChange).toHaveBeenLastCalledWith(true));

    panel.show(B);
    await heading(getState().details[B]!.title);
    expect(onDirtyChange).toHaveBeenLastCalledWith(false);

    await user.click(screen.getByRole("button", { name: "Edit" }));
    await user.type(screen.getByLabelText("Description"), " more");
    await waitFor(() => expect(onDirtyChange).toHaveBeenLastCalledWith(true));

    panel.unmount();
    expect(onDirtyChange).toHaveBeenLastCalledWith(false);
  });
});
