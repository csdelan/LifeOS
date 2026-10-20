import { useState } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { AmountFields } from "@/components/rewards/amount-fields";
import { useWrites } from "@/lib/queries";
import {
  amountsFrom,
  draftFrom,
  priceProblem,
  sameAmounts,
  SPENDABLE,
  targetProblem,
  type AmountDraft,
} from "@/lib/rewards";
import type { RewardKind, RewardRow } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/**
 * Add a catalog Reward, or edit one (REWARD-3): its name, price or target, and
 * description. A Reward's kind is fixed once it exists. Archive takes it off the
 * catalog; it can be restored.
 */
export function RewardFormDialog({
  open,
  onOpenChange,
  reward,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** Present: edit this Reward. Absent: add one. */
  reward?: RewardRow | null;
}) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <RewardForm
          key={reward?.id ?? "new"}
          reward={reward ?? null}
          onDone={() => onOpenChange(false)}
        />
      </DialogContent>
    </Dialog>
  );
}

const KINDS: { kind: RewardKind; label: string; hint: string }[] = [
  { kind: "instant", label: "Treat", hint: "Something you buy with a price, like Movie night — 120 Freedom." },
  { kind: "savings", label: "Savings goal", hint: "Coins go in as real dollars toward a target, like a vacation fund." },
];

function RewardForm({ reward, onDone }: { reward: RewardRow | null; onDone: () => void }) {
  const writes = useWrites();
  const editing = !!reward;
  const [kind, setKind] = useState<RewardKind>(reward?.kind ?? "instant");
  const [name, setName] = useState(reward?.name ?? "");
  const [price, setPrice] = useState<AmountDraft>(() => draftFrom(reward?.price ?? {}, SPENDABLE));
  const [target, setTarget] = useState(reward?.targetUsd ? String(reward.targetUsd) : "");
  const [description, setDescription] = useState(reward?.description ?? "");
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);

  const amounts = amountsFrom(price);
  const priced = Object.keys(amounts).length > 0;
  const problem = !name.trim()
    ? "Give it a name."
    : kind === "instant"
      ? (priceProblem(amounts) ??
        (editing && !priced && Object.keys(reward.price).length > 0
          ? "Give it a price, or archive it."
          : null))
      : targetProblem(target);

  async function run(action: () => Promise<unknown>) {
    setError(null);
    setPending(true);
    try {
      await action();
      onDone();
    } catch (e) {
      setError(e instanceof Error ? e.message : "Saving failed. Your changes are still here.");
    } finally {
      setPending(false);
    }
  }

  function save() {
    if (problem || pending) return;
    const targetUsd = target.trim() ? Number(target) : null;
    void run(async () => {
      if (!reward) {
        await writes.addReward({
          title: name.trim(),
          kind,
          price: kind === "instant" && priced ? amounts : undefined,
          targetUsd: kind === "savings" && targetUsd !== null ? targetUsd : undefined,
          description: description.trim() || undefined,
        });
        return;
      }
      if (name.trim() !== reward.name) await writes.rename(reward.id, name.trim());
      if (description.trim() !== (reward.description ?? "")) {
        await writes.setAttributes(reward.id, { description: description.trim() });
      }
      if (kind === "instant" && priced && !sameAmounts(amounts, reward.price)) {
        await writes.priceReward(reward.id, amounts);
      }
      if (kind === "savings" && targetUsd !== (reward.targetUsd ?? null)) {
        await writes.targetReward(reward.id, targetUsd);
      }
    });
  }

  return (
    <form
      className="space-y-4"
      onSubmit={(e) => {
        e.preventDefault();
        save();
      }}
    >
      <DialogHeader>
        <DialogTitle>{editing ? `Edit ${reward.name}` : "Add a reward"}</DialogTitle>
        <DialogDescription>
          {KINDS.find((k) => k.kind === kind)?.hint}
        </DialogDescription>
      </DialogHeader>

      {editing ? null : (
        <div className="flex gap-1" role="radiogroup" aria-label="Kind of reward">
          {KINDS.map((k) => (
            <button
              key={k.kind}
              type="button"
              role="radio"
              aria-checked={kind === k.kind}
              disabled={pending}
              onClick={() => setKind(k.kind)}
              className={cn(
                "min-h-11 rounded-md px-2.5 py-1 text-sm md:min-h-0",
                kind === k.kind
                  ? "bg-primary/12 font-medium text-primary"
                  : "text-muted-foreground hover:bg-muted",
              )}
            >
              {k.label}
            </button>
          ))}
        </div>
      )}

      <div className="space-y-1">
        <Label htmlFor="reward-name">Name</Label>
        <Input
          id="reward-name"
          autoFocus
          value={name}
          disabled={pending}
          placeholder={kind === "savings" ? "Vacation fund" : "Movie night"}
          onChange={(e) => setName(e.target.value)}
        />
      </div>

      {kind === "instant" ? (
        <fieldset className="space-y-2">
          <legend className="text-sm font-medium">Price</legend>
          <AmountFields
            idPrefix="reward-price"
            currencies={SPENDABLE}
            value={price}
            onChange={setPrice}
            disabled={pending}
          />
        </fieldset>
      ) : (
        <div className="space-y-1">
          <Label htmlFor="reward-target">
            Target in dollars <span className="font-normal text-muted-foreground">(optional)</span>
          </Label>
          <Input
            id="reward-target"
            type="number"
            inputMode="decimal"
            min={0.01}
            step={0.01}
            value={target}
            disabled={pending}
            placeholder="500"
            onChange={(e) => setTarget(e.target.value)}
          />
        </div>
      )}

      <div className="space-y-1">
        <Label htmlFor="reward-description">
          Description <span className="font-normal text-muted-foreground">(optional)</span>
        </Label>
        <Textarea
          id="reward-description"
          rows={2}
          value={description}
          disabled={pending}
          onChange={(e) => setDescription(e.target.value)}
        />
      </div>

      {problem ? (
        <p className="text-xs text-muted-foreground" aria-live="polite">
          {problem}
        </p>
      ) : null}
      {error ? (
        <p role="alert" className="text-xs text-destructive">
          {error}
        </p>
      ) : null}

      <div className="flex flex-wrap justify-end gap-2">
        {editing ? (
          <Button
            type="button"
            variant="ghost"
            className="mr-auto"
            disabled={pending}
            onClick={() => void run(() => writes.archive(reward.id))}
          >
            Archive
          </Button>
        ) : null}
        <Button type="button" variant="outline" onClick={onDone}>
          Cancel
        </Button>
        <Button type="submit" disabled={!!problem || pending}>
          {pending ? "Saving…" : editing ? "Save" : "Add"}
        </Button>
      </div>
    </form>
  );
}
