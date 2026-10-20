import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { balanceOf, type AmountDraft, type CurrencyDef } from "@/lib/rewards";
import type { Wallet } from "@/lib/production-ui-types";

/**
 * One whole-number box per currency; with a wallet, each shows what you have. `min` is the
 * smallest amount a box takes: 1 for a spend, 0 for a bounty (a currency it doesn't pay).
 */
export function AmountFields({
  idPrefix,
  currencies,
  value,
  onChange,
  wallet,
  disabled,
  min = 1,
}: {
  idPrefix: string;
  currencies: readonly CurrencyDef[];
  value: AmountDraft;
  onChange: (next: AmountDraft) => void;
  wallet?: Wallet;
  disabled?: boolean;
  min?: number;
}) {
  return (
    <div className="grid grid-cols-2 gap-3">
      {currencies.map((c) => {
        const id = `${idPrefix}-${c.key}`;
        return (
          <div key={c.key} className="space-y-1">
            <Label htmlFor={id}>
              <span aria-hidden>{c.symbol}</span> {c.label}
            </Label>
            <Input
              id={id}
              type="number"
              inputMode="numeric"
              min={min}
              step={1}
              placeholder="0"
              value={value[c.key] ?? ""}
              disabled={disabled}
              aria-describedby={wallet ? `${id}-have` : undefined}
              onChange={(e) => onChange({ ...value, [c.key]: e.target.value })}
            />
            {wallet ? (
              <p id={`${id}-have`} className="text-[0.6875rem] text-muted-foreground">
                You have {balanceOf(wallet, c.key).toLocaleString()}
              </p>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}
