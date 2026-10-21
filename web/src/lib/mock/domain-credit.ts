/**
 * The mock's `v_award_credit` (migration 0032) and the life-domain reads over it: what each
 * paid award credits, and to which day, folded under `lib/domain-fold.ts`.
 *
 * - Every award key whose net in a currency is positive counts, on the credited day of its
 *   latest earn; a reversal takes it off its day; earning it again files it on the new day.
 * - `base` is the latest earn's `base` for the currency (stamped when a multiplier above 1.0
 *   applied), else its amount; `paid` is the key's net.
 */
import { CONFIGURED_DOMAINS, DomainProgress, shiftDay, type DomainDay } from "@/lib/domain-fold";
import type { Amounts } from "@/lib/production-ui-types";
import type { MockLedgerEntry } from "@/lib/mock/seed";

/** "2026-10-20": the local date an instant reads (the mock's home zone is the browser's). */
export function localDay(iso: string): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

const DAY_KEY = /^day:.+:(\d{4}-\d{2}-\d{2}):(?:followed|partial)$/;
const STREAK_KEY = /^streak_(\d+):.+:(\d{4}-\d{2}-\d{2})$/;

/**
 * The day an award counts on (migration 0030): a habit day's own date, a streak's last
 * (nth) day, and otherwise the trigger's local date.
 */
export function creditedOnFor(awardKey: string, at: string): string {
  const day = DAY_KEY.exec(awardKey);
  if (day) return day[1];
  const streak = STREAK_KEY.exec(awardKey);
  if (streak) return shiftDay(streak[2], Number(streak[1]) - 1);
  return localDay(at);
}

/** Each currency's credit by day, from a ledger as it stands. */
export function creditDays(ledger: readonly MockLedgerEntry[]): Map<string, DomainDay[]> {
  const net = new Map<string, Amounts>();
  const latestEarn = new Map<string, MockLedgerEntry>();
  for (const e of ledger) {
    if (!e.awardKey || (e.entry !== "earn" && e.entry !== "reversal")) continue;
    const sums = net.get(e.awardKey) ?? {};
    for (const [k, n] of Object.entries(e.amounts)) sums[k] = (sums[k] ?? 0) + n;
    net.set(e.awardKey, sums);
    const latest = latestEarn.get(e.awardKey);
    // The latest earn by when it happened; entries in the same instant keep write order.
    if (e.entry === "earn" && (!latest || e.occurredAt >= latest.occurredAt)) latestEarn.set(e.awardKey, e);
  }
  const out = new Map<string, DomainDay[]>();
  for (const [key, sums] of net) {
    const earn = latestEarn.get(key);
    if (!earn) continue;
    const day = earn.creditedOn ?? localDay(earn.occurredAt);
    for (const [currency, paid] of Object.entries(sums)) {
      if (paid <= 0) continue;
      const base = earn.base?.[currency] ?? earn.amounts[currency] ?? paid;
      out.set(currency, [...(out.get(currency) ?? []), { day, base, paid }]);
    }
  }
  return out;
}

/** Every configured domain folded from `ledger`, as of `today`. */
export function foldDomains(ledger: readonly MockLedgerEntry[], today: string): DomainProgress[] {
  const credit = creditDays(ledger);
  return CONFIGURED_DOMAINS.map((rules) => DomainProgress.fold(rules, credit.get(rules.currency) ?? [], today));
}
