import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { LogFoodSheet } from "@/components/food/log-food-sheet";
import { todayIso } from "@/lib/dates";
import { mockFoodDay, mockLogFood, mockSetFoodPhoto, resetStore } from "@/lib/mock/store";
import type { MealRow } from "@/lib/production-ui-types";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), message: vi.fn() }),
}));

function renderSheet(template: MealRow | null = null) {
  const today = todayIso();
  const onOpenChange = vi.fn();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <LogFoodSheet
        open
        onOpenChange={onOpenChange}
        meal={null}
        template={template}
        day={today}
        today={today}
        running={null}
      />
    </QueryClientProvider>,
  );
  return { onOpenChange, picker: within(screen.getByRole("group", { name: "Health score" })) };
}

describe("LogFoodSheet's score picker (food-log plan §8)", () => {
  it("offers the half points between the whole ones, in score order", () => {
    const { picker } = renderSheet();
    expect(picker.getAllByRole("radio").map((r) => r.getAttribute("aria-label"))).toEqual([
      "1 · junk",
      "1.5 · junk to indulgent",
      "2 · indulgent",
      "2.5 · indulgent to average",
      "3 · average",
      "3.5 · average to good",
      "4 · good",
      "4.5 · good to very healthy",
      "5 · very healthy",
    ]);
    expect(screen.getByText("1 junk · 3 average · 5 very healthy · halves in between")).toBeInTheDocument();
  });

  it("logs a 3.5 and says what it means", async () => {
    const user = userEvent.setup();
    const { onOpenChange, picker } = renderSheet();

    await user.click(picker.getByRole("radio", { name: "3.5 · average to good" }));
    expect(picker.getByRole("radio", { name: "3.5 · average to good" })).toBeChecked();
    expect(screen.getByText("Average to good: halfway between 3 and 4.")).toBeInTheDocument();

    // A whole score still shows its examples.
    await user.click(picker.getByRole("radio", { name: "4 · good" }));
    expect(screen.getByText("Good: grilled chicken and vegetables, oatmeal and fruit.")).toBeInTheDocument();
    await user.click(picker.getByRole("radio", { name: "3.5 · average to good" }));

    await user.type(screen.getByLabelText("Name"), "Turkey wrap");
    await user.type(screen.getByLabelText("Calories"), "550");
    await user.click(screen.getByRole("button", { name: "Log" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    const wrap = mockFoodDay().meals.find((m) => m.name === "Turkey wrap");
    expect(wrap).toMatchObject({ score: 3.5, calories: 550 });
  });

  it("greys every score out at 1 for a drink, halves included", async () => {
    const user = userEvent.setup();
    const { picker } = renderSheet();
    await user.click(picker.getByRole("radio", { name: "2.5 · indulgent to average" }));
    await user.click(screen.getByRole("switch", { name: "Alcoholic drink" }));

    // Disabled through their fieldset, which toBeDisabled reads.
    for (const radio of picker.getAllByRole("radio")) expect(radio).toBeDisabled();
    expect(picker.getByRole("radio", { name: "1 · junk" })).toBeChecked();

    // Off again, the half picked before comes back.
    await user.click(screen.getByRole("switch", { name: "Alcoholic drink" }));
    expect(picker.getByRole("radio", { name: "2.5 · indulgent to average" })).toBeChecked();
  });
});

describe("LogFoodSheet's photo on Log again and Have another (FOOD-5)", () => {
  /** A burrito logged yesterday with a photo; the photo's artifact id. */
  function burritoWithPhoto(): { meal: MealRow; photo: string } {
    resetStore();
    const meal = mockLogFood({
      name: "Sausage burrito",
      score: 2,
      calories: 650,
      eatenAt: new Date(Date.now() - 26 * 3_600_000).toISOString(),
    }).meal!;
    const blob = new Blob([new Uint8Array([0xff, 0xd8, 0xff, 1])], { type: "image/jpeg" });
    const set = mockSetFoodPhoto(meal.id, blob, "burrito.jpg").meal!;
    return { meal: set, photo: set.photoArtifactId! };
  }

  it("carries the photo with Log again, and logs it without an upload", async () => {
    const user = userEvent.setup();
    const { photo } = burritoWithPhoto();
    const { onOpenChange } = renderSheet();

    // jsdom drops the space after the hidden "Log again:"; a browser keeps it.
    await user.click(await screen.findByRole("button", { name: /^Log again: ?Sausage burrito · 2 · 650 cal$/ }));
    expect(screen.getByRole("button", { name: "Show the photo of Sausage burrito full size" })).toBeInTheDocument();
    expect(screen.getByText(/^The last one's photo comes with it\./)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Log" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    const logged = mockFoodDay().meals.filter((m) => m.name === "Sausage burrito");
    expect(logged).toHaveLength(1);
    expect(logged[0].photoArtifactId).toBe(photo);
  });

  it("leaves the photo off when it's removed, and Have another carries it too", async () => {
    const user = userEvent.setup();
    const { meal, photo } = burritoWithPhoto();
    const { onOpenChange } = renderSheet(meal);

    expect(screen.getByRole("button", { name: "Show the photo of Sausage burrito full size" })).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Remove photo" }));
    expect(screen.queryByRole("button", { name: /^Show the photo/ })).toBeNull();
    expect(screen.getByText("Optional. It's shrunk to 1,200 px before it's sent.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "Log" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    const today = mockFoodDay().meals.filter((m) => m.name === "Sausage burrito");
    expect(today.map((m) => m.photoArtifactId)).toEqual([null]);
    expect(photo).toBeTruthy();
  });
});
