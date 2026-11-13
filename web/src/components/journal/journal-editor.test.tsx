import { act, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { Editor } from "@tiptap/core";
import { describe, expect, it, vi } from "vitest";
import { JournalEditor } from "./journal-editor";

const toastError = vi.hoisted(() => vi.fn());
vi.mock("sonner", () => ({ toast: { error: toastError } }));

/** The TipTap editor behind the rendered editor, as TipTap exposes it on its element. */
async function editorIn(container: HTMLElement): Promise<Editor> {
  await waitFor(() => expect(container.querySelector(".ProseMirror")).not.toBeNull());
  return (container.querySelector(".ProseMirror") as HTMLElement & { editor: Editor }).editor;
}

describe("JournalEditor", () => {
  it("keeps the entry when appending it fails", async () => {
    const onAppend = vi.fn().mockRejectedValue(new Error("Network down."));
    const { container } = render(<JournalEditor onAppend={onAppend} />);
    const editor = await editorIn(container);
    act(() => {
      editor.commands.setContent("<p>A hard day</p>");
    });

    await userEvent.click(screen.getByRole("button", { name: "Append" }));

    await waitFor(() => expect(toastError).toHaveBeenCalledWith("Network down. It's still in the editor."));
    expect(onAppend).toHaveBeenCalledWith("<p>A hard day</p>");
    expect(editor.getText()).toBe("A hard day");
  });

  it("clears the entry once it is saved", async () => {
    const onAppend = vi.fn().mockResolvedValue(undefined);
    const { container } = render(<JournalEditor onAppend={onAppend} />);
    const editor = await editorIn(container);
    act(() => {
      editor.commands.setContent("<p>A good day</p>");
    });

    await userEvent.click(screen.getByRole("button", { name: "Append" }));

    await waitFor(() => expect(editor.isEmpty).toBe(true));
    expect(onAppend).toHaveBeenCalledWith("<p>A good day</p>");
  });
});
