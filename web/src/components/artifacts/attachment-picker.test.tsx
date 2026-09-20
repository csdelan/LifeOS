import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { useState } from "react";
import { describe, expect, it } from "vitest";
import { AttachmentPicker } from "@/components/artifacts/attachment-picker";

function Harness() {
  const [file, setFile] = useState<File | null>(null);
  return <AttachmentPicker file={file} onChange={setFile} />;
}

describe("AttachmentPicker", () => {
  it("shows a selected-file chip with name, size, and a clear button", async () => {
    const user = userEvent.setup();
    const { container } = render(<Harness />);
    const input = container.querySelector("input[type=file]") as HTMLInputElement;
    const file = new File(["hello world"], "notes.txt", { type: "text/plain" });
    await user.upload(input, file);
    expect(screen.getByText("notes.txt")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Remove notes.txt" }));
    expect(screen.queryByText("notes.txt")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Attach file" })).toBeInTheDocument();
  });
});
