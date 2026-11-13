import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SubjectCombobox, SUBJECT_PICKER_LIMIT } from "./subject-combobox";

const many = Array.from({ length: 80 }, (_, i) => ({ id: `id-${i}`, title: `Chore ${i}` }));

describe("SubjectCombobox", () => {
  it("reaches an item past the first page by searching for it", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<SubjectCombobox options={many} value="" onChange={onChange} ariaLabel="Relate to subject" />);

    await user.click(screen.getByRole("combobox", { name: "Relate to subject" }));
    expect(screen.getAllByRole("option")).toHaveLength(SUBJECT_PICKER_LIMIT);
    expect(screen.getByText("30 more: type to narrow.")).toBeInTheDocument();

    await user.type(screen.getByRole("combobox", { name: "Search relate to subject" }), "chore 79");
    await user.click(screen.getByRole("option", { name: "Chore 79" }));

    expect(onChange).toHaveBeenCalledWith("id-79");
    expect(screen.queryByRole("option")).not.toBeInTheDocument();
  });

  it("shows the chosen item and offers none first when it may be cleared", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(
      <SubjectCombobox
        options={many.slice(0, 3)}
        value="id-1"
        onChange={onChange}
        ariaLabel="Serves"
        noneLabel="None"
      />,
    );

    const trigger = screen.getByRole("combobox", { name: "Serves" });
    expect(trigger).toHaveTextContent("Chore 1");
    await user.click(trigger);
    await user.click(screen.getAllByRole("option")[0]!);
    expect(onChange).toHaveBeenCalledWith("");
  });

  it("says when nothing matches", async () => {
    const user = userEvent.setup();
    render(<SubjectCombobox options={many} value="" onChange={() => {}} ariaLabel="Target subject" />);
    await user.click(screen.getByRole("combobox", { name: "Target subject" }));
    await user.type(screen.getByRole("combobox", { name: "Search target subject" }), "zebra");
    expect(screen.getByText("No matching items.")).toBeInTheDocument();
  });
});
