import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { mockReads } from "@/lib/mock/api";
import { CommandPalette } from "@/components/shell/command-palette";

const navigate = vi.fn();

vi.mock("@tanstack/react-router", () => ({
  useNavigate: () => navigate,
}));

beforeEach(() => {
  navigate.mockClear();
});

function renderPalette() {
  const onOpenChange = vi.fn();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <CommandPalette open onOpenChange={onOpenChange} onNew={() => {}} onCapture={() => {}} onVoice={() => {}} />
    </QueryClientProvider>,
  );
  return { onOpenChange };
}

describe("Command palette: finding subjects", () => {
  it("finds an item past the first twenty, and opens a Decision in Browse", async () => {
    // The Blazor decision sits well past the twenty the search box used to look through.
    const all = await mockReads.listSubjects({ includeArchived: true });
    expect(all.findIndex((s) => s.id === "dec-blazor")).toBeGreaterThanOrEqual(20);

    const user = userEvent.setup();
    const { onOpenChange } = renderPalette();
    await user.type(screen.getByRole("combobox"), "blazor");
    const option = await screen.findByRole("option", { name: /Blazor for Production UI/ });
    expect(option).toHaveTextContent("Decision");

    await user.click(option);
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(navigate).toHaveBeenCalledWith({
      to: "/browse",
      search: { type: "Decision", selected: "dec-blazor" },
    });
  });

  it("matches the words typed in any order, and lists nothing before anything is typed", async () => {
    const user = userEvent.setup();
    renderPalette();
    expect(screen.queryByRole("group", { name: "Subjects" })).not.toBeInTheDocument();

    await user.type(screen.getByRole("combobox"), "production react");
    expect(await screen.findByRole("option", { name: /React \+ Vite for Production UI/ })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: /Blazor/ })).not.toBeInTheDocument();
  });

  it("still opens a Goal on the Map", async () => {
    const user = userEvent.setup();
    renderPalette();
    await user.type(screen.getByRole("combobox"), "half-marathon");
    await user.click(await screen.findByRole("option", { name: /Run a half-marathon/ }));
    expect(navigate).toHaveBeenCalledWith({ to: "/map", search: { selected: expect.any(String) } });
  });
});
