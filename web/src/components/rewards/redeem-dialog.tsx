import { useState } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { AmountFields } from "@/components/rewards/amount-fields";
import { celebrate } from "@/lib/celebrate";
import { useWrites } from "@/lib/queries";
import {
  amountsFrom,
  coinsToUsd,
  currency,
  draftFrom,
  formatAmounts,
  formatUsd,
  redeemProblem,
  sameAmounts,
  SPENDABLE,
  type AmountDraft,
  type CurrencyDef,
} from "@/lib/rewards";
import type { Redemption, RewardRow, Wallet } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/**
 * Spend currencies (REWARD-3): an instant reward at its price (or a different amount
 * this time), Coins into a savings goal, or — with no Reward — anything, with a note
 * saying what it was for. The kernel's rules are checked as you type, so Redeem stays
 * blocked until the wallet covers it; a success celebrates (REWARD-4).
 */
export function RedeemDialog({
  open,
  onOpenChange,
  reward,
  wallet,
  onRedeemed,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Null: a custom redemption, not against the catalog. */
  reward: RewardRow | null;
  wallet?: Wallet;
  onRedeemed?: (redemption: Redemption) => void;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <RedeemForm
          key={reward?.id ?? "custom"}
          reward={reward}
          wallet={wallet}
          onDone={(redemption) => {
            onRedeemed?.(redemption);
            onOpenChange(false);
          }}
          onCancel={() => onOpenChange(false)}
        />
      </DialogContent>
    </Dialog>
  );
}

function fieldsFor(reward: RewardRow | null): readonly CurrencyDef[] {
  if (reward?.kind === "savings") return SPENDABLE.filter((c) => c.key === "coins");
  const priced = Object.keys(reward?.price ?? {});
  return priced.length > 0
    ? priced.map((k) => currency(k)).filter((c): c is CurrencyDef => !!c)
    : SPENDABLE;
}

function RedeemForm({
  reward,
  wallet,
  onDone,
  onCancel,
}: {
  reward: RewardRow | null;
  wallet?: Wallet;
  onDone: (redemption: Redemption) => void;
  onCancel: () => void;
}) {
  const writes = useWrites();
  const fields = fieldsFor(reward);
  const [draft, setDraft] = useState<AmountDraft>(() => draftFrom(reward?.price ?? {}, fields));
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const amounts = amountsFrom(draft);
  const problem = redeemProblem({ reward, amounts, note }, wallet);
  const savings = reward?.kind === "savings";
  const custom = !reward;
  const rate = wallet?.coinValueUsd ?? 0.5;
  const coins = amounts.coins ?? 0;

  async function submit() {
    if (problem || pending) return;
    setError(null);
    setPending(true);
    try {
      // Unchanged from the catalog price: let the kernel charge the price itself.
      const atPrice = reward?.kind === "instant" && sameAmounts(amounts, reward.price);
      const redemption = await writes.redeem({
        reward: reward?.id,
        amounts: atPrice ? undefined : amounts,
        note: note.trim() || undefined,
      });
      celebrate();
      onDone(redemption);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Redeem failed. Nothing was spent.");
    } finally {
      setPending(false);
    }
  }

  const title = custom
    ? "Redeem something else"
    : savings
      ? `Deposit into ${reward.name}`
      : `Redeem ${reward.name}`;

  return (
    <form
      className="space-y-4"
      onSubmit={(e) => {
        e.preventDefault();
        void submit();
      }}
    >
      <DialogHeader>
        <DialogTitle>{title}</DialogTitle>
        <DialogDescription>
          {custom
            ? "Spend on anything that isn't in the catalog. Say what it was for."
            : savings
              ? `Coins are real money: move the dollars into this goal when you deposit. 1 Coin = ${formatUsd(rate)}.`
              : Object.keys(reward.price).length > 0
                ? `Costs ${formatAmounts(reward.price)}. Change the amount for this time if you like.`
                : "This reward has no price yet: say what it costs this time."}
        </DialogDescription>
      </DialogHeader>

      {savings && reward.targetUsd ? (
        <p className="text-sm">
          {formatUsd(reward.dollarsRedeemed)} of {formatUsd(reward.targetUsd)} saved
          {reward.remainingUsd ? ` · ${formatUsd(reward.remainingUsd)} to go` : ""}
        </p>
      ) : null}

      <AmountFields
        idPrefix="redeem"
        currencies={fields}
        value={draft}
        onChange={setDraft}
        wallet={wallet}
        disabled={pending}
      />
      {savings && coins > 0 && Number.isInteger(coins) ? (
        <p className="text-sm font-medium" aria-live="polite">
          Move {formatUsd(coinsToUsd(coins, rate))} into {reward.name}
        </p>
      ) : null}

      <div className="space-y-1">
        <Label htmlFor="redeem-note">
          What's it for?{" "}
          <span className="font-normal text-muted-foreground">
            {custom ? "(required)" : "(optional)"}
          </span>
        </Label>
        <Textarea
          id="redeem-note"
          rows={2}
          value={note}
          required={custom}
          disabled={pending}
          placeholder={custom ? "45 minutes of gaming" : "A note for the log"}
          onChange={(e) => setNote(e.target.value)}
        />
      </div>

      {problem ? (
        <p
          className={cn(
            "text-xs",
            problem.startsWith("You have") ? "text-destructive" : "text-muted-foreground",
          )}
          aria-live="polite"
        >
          {problem}
        </p>
      ) : null}
      {error ? (
        <p role="alert" className="text-xs text-destructive">
          {error}
        </p>
      ) : null}

      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="submit" disabled={!!problem || pending}>
          {pending ? "Redeeming…" : savings ? "Deposit" : "Redeem"}
        </Button>
      </div>
    </form>
  );
}
