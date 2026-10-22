import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { MealGallery } from "@/components/food/meal-gallery";
import type { MealRow } from "@/lib/production-ui-types";

function meal(id: string, name: string, at: string, overrides: Partial<MealRow> = {}): MealRow {
  return {
    id,
    name,
    score: 1,
    calories: 150,
    proteinG: null,
    carbsG: null,
    fatG: null,
    mealType: "drink",
    eatenAt: new Date(`2026-10-21T${at}:00`).toISOString(),
    eatenOn: "2026-10-21",
    photoArtifactId: null,
    alcoholic: true,
    duringFast: null,
    firstLoggedAt: "2026-10-21T00:00:00Z",
    loggedAt: "2026-10-21T00:00:00Z",
    versions: 1,
    ...overrides,
  };
}

const MEALS = [
  meal("b1", "Lager", "19:10"),
  meal("s1", "Grain bowl", "19:30", { score: 4, calories: 700, mealType: "dinner", alcoholic: false }),
  meal("b2", "Lager", "20:15"),
  meal("b3", "Lager", "21:30"),
];

function renderGallery(meals = MEALS) {
  const onOpen = vi.fn();
  const onAnother = vi.fn();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <MealGallery meals={meals} dayLabel="today" onOpen={onOpen} onAnother={onAnother} />
    </QueryClientProvider>,
  );
  return { onOpen, onAnother };
}

describe("MealGallery (FOOD-7)", () => {
  it("shows identical entries as one card, oldest first, with the calories summed and the time as a range", () => {
    renderGallery();
    const cards = within(screen.getByRole("list", { name: "Entries for today" })).getAllByRole("button");
    expect(cards.map((c) => c.getAttribute("aria-label"))).toEqual([
      expect.stringMatching(/^Lager ×3 \(alcoholic drink\), 7:10\sPM–9:30\sPM: show the 3 entries$/),
      expect.stringMatching(/^Grain bowl, 7:30\sPM: change or delete$/),
    ]);
    expect(cards[0]).toHaveTextContent("Lager ×3");
    expect(cards[0]).toHaveTextContent("450 cal");
    expect(cards[0]).toHaveTextContent("1 · alcoholic");
  });

  it("opens a single entry straight away", async () => {
    const { onOpen } = renderGallery();
    await userEvent.click(screen.getByRole("button", { name: /^Grain bowl, / }));
    expect(onOpen).toHaveBeenCalledWith(MEALS[1]);
  });

  it("lists a combined card's copies, each opening to change or delete, and offers Have another", async () => {
    const { onOpen, onAnother } = renderGallery();
    await userEvent.click(screen.getByRole("button", { name: /^Lager ×3/ }));
    const dialog = await screen.findByRole("dialog", { name: "Lager ×3" });
    const copies = within(within(dialog).getByRole("list", { name: "Each Lager" })).getAllByRole("button");
    expect(copies).toHaveLength(3);
    await userEvent.click(copies[1]);
    expect(onOpen).toHaveBeenCalledWith(MEALS[2]);

    await userEvent.click(screen.getByRole("button", { name: /^Lager ×3/ }));
    await userEvent.click(await screen.findByRole("button", { name: "Have another" }));
    // Have another starts from the last copy.
    expect(onAnother).toHaveBeenCalledWith(MEALS[3]);
  });
});
