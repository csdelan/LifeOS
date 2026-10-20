import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { RedeemDialog } from "@/components/rewards/redeem-dialog";
import { RewardFormDialog } from "@/components/rewards/reward-form-dialog";
import { celebrate } from "@/lib/celebrate";
import { ApiRequestError } from "@/lib/live/http";
import { mockRewards, mockWallet } from "@/lib/mock/store";
import { createMockWriteClient } from "@/lib/mock/write-client";
import type { LifeOsWriteClient, RewardRow } from "@/lib/production-ui-types";

vi.mock("sonner", () => ({
  toast: Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), message: vi.fn() }),
}));

vi.mock("@/lib/celebrate", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/celebrate")>()),
  celebrate: vi.fn(() => ({ confetti: true, sound: true })),
}));

let writes: LifeOsWriteClient;

vi.mock("@/lib/client", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/client")>()),
  createWriteClient: () => writes,
}));

afterEach(() => {
  vi.clearAllMocks();
  vi.restoreAllMocks();
});

const seeded = (id: string) => mockRewards().find((r) => r.id === id)!;

function withQuery(ui: React.ReactElement) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

function renderRedeem(reward: RewardRow | null, onOpenChange = vi.fn()) {
  writes = createMockWriteClient();
  withQuery(<RedeemDialog open onOpenChange={onOpenChange} reward={reward} wallet={mockWallet()} />);
  return onOpenChange;
}

const redeemButton = () => screen.getByRole("button", { name: /^(Redeem|Deposit)$/ });

describe("RedeemDialog", () => {
  it("redeems an instant reward at its price, celebrates, and closes", async () => {
    const onOpenChange = renderRedeem(seeded("reward-episode"));
    const redeem = vi.spyOn(writes, "redeem");

    expect(screen.getByLabelText(/Freedom/)).toHaveValue(45);
    fireEvent.change(screen.getByLabelText(/What's it for/), { target: { value: "Season finale" } });
    fireEvent.click(redeemButton());

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(redeem).toHaveBeenCalledWith({ reward: "reward-episode", amounts: undefined, note: "Season finale" });
    expect(celebrate).toHaveBeenCalledTimes(1);
  });

  it("blocks Redeem until the wallet covers it, saying why", () => {
    renderRedeem(seeded("reward-movie"));

    expect(screen.getByText("You have 60 Freedom; this needs 120.")).toBeInTheDocument();
    expect(redeemButton()).toBeDisabled();

    fireEvent.change(screen.getByLabelText(/Freedom/), { target: { value: "45" } });
    expect(redeemButton()).toBeEnabled();
  });

  it("needs a note for a custom redemption", () => {
    renderRedeem(null);

    fireEvent.change(screen.getByLabelText(/Freedom/), { target: { value: "15" } });
    expect(screen.getByText(/a note is required/)).toBeInTheDocument();
    expect(redeemButton()).toBeDisabled();

    fireEvent.change(screen.getByLabelText(/What's it for/), { target: { value: "45 minutes of gaming" } });
    expect(redeemButton()).toBeEnabled();
  });

  it("shows the dollars a savings deposit moves", () => {
    renderRedeem(seeded("reward-vacation"));

    expect(screen.queryByLabelText(/Freedom/)).toBeNull();
    fireEvent.change(screen.getByLabelText(/Coins/), { target: { value: "4" } });

    expect(screen.getByText("Move $2.00 into Vacation fund")).toBeInTheDocument();
    expect(redeemButton()).toHaveTextContent("Deposit");
  });

  it("keeps the dialog open with the kernel's reason when it refuses", async () => {
    const onOpenChange = renderRedeem(seeded("reward-episode"));
    vi.spyOn(writes, "redeem").mockRejectedValue(
      new ApiRequestError("'Episode of a show' is archived; restore it to redeem against it.", 400),
    );

    fireEvent.click(redeemButton());

    expect(await screen.findByRole("alert")).toHaveTextContent("is archived");
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
    expect(celebrate).not.toHaveBeenCalled();
  });
});

describe("RewardFormDialog", () => {
  function renderForm(reward?: RewardRow) {
    writes = createMockWriteClient();
    const onOpenChange = vi.fn();
    withQuery(<RewardFormDialog open onOpenChange={onOpenChange} reward={reward} />);
    return onOpenChange;
  }

  it("adds a treat with its price", async () => {
    const onOpenChange = renderForm();
    const add = vi.spyOn(writes, "addReward");

    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Pizza night" } });
    fireEvent.change(screen.getByLabelText(/Freedom/), { target: { value: "30" } });
    fireEvent.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(add).toHaveBeenCalledWith({
      title: "Pizza night",
      kind: "instant",
      price: { freedom: 30 },
      targetUsd: undefined,
      description: undefined,
    });
  });

  it("adds a savings goal with a target", async () => {
    renderForm();
    const add = vi.spyOn(writes, "addReward");

    fireEvent.click(screen.getByRole("radio", { name: "Savings goal" }));
    fireEvent.change(screen.getByLabelText("Name"), { target: { value: "Bike fund" } });
    fireEvent.change(screen.getByLabelText(/Target in dollars/), { target: { value: "300" } });
    fireEvent.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => expect(add).toHaveBeenCalled());
    expect(add.mock.calls[0][0]).toMatchObject({ kind: "savings", targetUsd: 300, price: undefined });
  });

  it("saves only what changed on an edit", async () => {
    const onOpenChange = renderForm(seeded("reward-vacation"));
    const rename = vi.spyOn(writes, "rename");
    const setAttributes = vi.spyOn(writes, "setAttributes");
    const target = vi.spyOn(writes, "targetReward");

    fireEvent.change(screen.getByLabelText(/Target in dollars/), { target: { value: "750" } });
    fireEvent.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(target).toHaveBeenCalledWith("reward-vacation", 750);
    expect(rename).not.toHaveBeenCalled();
    expect(setAttributes).not.toHaveBeenCalled();
  });
});
