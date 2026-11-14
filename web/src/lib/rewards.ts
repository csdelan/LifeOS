/**
 * Rewards vocabulary and rules the UI shares with the kernel (REWARD-1 / REWARD-3 / REWARD-6).
 * The currency list mirrors `Currencies` in LifeOs.Domain; the redeem checks mirror
 * `RewardService.RedeemAsync` / `RewardPrice.Normalize`, so the dialog can say why
 * before it asks — the kernel still has the final word.
 */
import type {
  Amounts,
  Award,
  LedgerRow,
  Redemption,
  RewardRow,
  Wallet,
} from "@/lib/production-ui-types";

/**
 * Where a currency sits in the bounty editor: spent on rewards, tracked, or the XP of a
 * life domain (REWARD-6) — tracked too, and the basis of levels and ranks later.
 */
export type CurrencyGroup = "spend" | "track" | "domain";

export interface CurrencyDef {
  key: string;
  label: string;
  spendable: boolean;
  group: CurrencyGroup;
  /** Shown beside amounts; decorative (screen readers get the label). */
  symbol: string;
  /** What one unit buys, for the wallet card. */
  hint: string;
}

/**
 * Display order, as the Domain's `Currencies.All` lists them, with the web's own symbol and hint.
 * The keys, labels and order are the kernel's: `tests/rules/catalogs.json` checks them, and the
 * wallet card reads each label from the wallet itself (code review CR73).
 */
export const CURRENCIES: readonly CurrencyDef[] = [
  { key: "coins", label: "Coins", spendable: true, group: "spend", symbol: "🪙", hint: "Real money, into savings" },
  { key: "freedom", label: "Freedom", spendable: true, group: "spend", symbol: "⏳", hint: "1 Freedom = 1 minute of fun" },
  { key: "gems", label: "Gems", spendable: true, group: "spend", symbol: "💎", hint: "For the big ones" },
  { key: "karma", label: "Karma", spendable: true, group: "spend", symbol: "✨", hint: "Good deeds" },
  { key: "xp", label: "Life XP", spendable: false, group: "track", symbol: "⭐", hint: "General experience from everything" },
  { key: "resolve", label: "Resolve", spendable: false, group: "track", symbol: "🛡️", hint: "Resistance overcome" },
  { key: "health_xp", label: "Health XP", spendable: false, group: "domain", symbol: "❤️", hint: "Exercise, sleep, nutrition" },
  { key: "social_xp", label: "Social XP", spendable: false, group: "domain", symbol: "🤝", hint: "Family, friends, outreach" },
  { key: "environment_xp", label: "Environment XP", spendable: false, group: "domain", symbol: "🏡", hint: "Clean, tidy, maintain, declutter" },
  { key: "finance_xp", label: "Finance XP", spendable: false, group: "domain", symbol: "📈", hint: "Budget, save, grow net worth" },
];

/** The bounty editor's sections, in display order, each with its currencies. */
export const CURRENCY_GROUPS: readonly { id: CurrencyGroup; label: string; currencies: readonly CurrencyDef[] }[] = [
  { id: "spend", label: "Spend", currencies: CURRENCIES.filter((c) => c.group === "spend") },
  { id: "track", label: "Track", currencies: CURRENCIES.filter((c) => c.group === "track") },
  { id: "domain", label: "Life domains", currencies: CURRENCIES.filter((c) => c.group === "domain") },
];

export const SPENDABLE = CURRENCIES.filter((c) => c.spendable);

export function currency(key: string): CurrencyDef | undefined {
  const k = key.trim().toLowerCase();
  return CURRENCIES.find((c) => c.key === k);
}

export function currencyLabel(key: string): string {
  return currency(key)?.label ?? key;
}

/** Amounts in display order, zeros dropped. */
export function orderedAmounts(amounts: Amounts): [string, number][] {
  const known = CURRENCIES.map((c) => c.key).filter((k) => amounts[k]);
  const unknown = Object.keys(amounts).filter((k) => !currency(k) && amounts[k]);
  return [...known, ...unknown].map((k) => [k, amounts[k]]);
}

/** "120 Freedom · 2 Karma"; `signed` writes "+10 XP −2 Coins" for ledger rows. */
export function formatAmounts(amounts: Amounts, opts?: { signed?: boolean }): string {
  const parts = orderedAmounts(amounts).map(([k, n]) => {
    const sign = opts?.signed ? (n > 0 ? "+" : "−") : n < 0 ? "−" : "";
    return `${sign}${Math.abs(n).toLocaleString()} ${currencyLabel(k)}`;
  });
  return parts.join(opts?.signed ? " " : " · ");
}

export function formatUsd(value: number): string {
  return value.toLocaleString(undefined, { style: "currency", currency: "USD" });
}

/** Coins are real money: dollars at the rate, rounded to the cent (CoinRates.Dollars). */
export function coinsToUsd(coins: number, rate: number): number {
  return Math.round(coins * rate * 100) / 100;
}

export function balanceOf(wallet: Wallet | undefined, key: string): number {
  return wallet?.balances.find((b) => b.currency === key)?.balance ?? 0;
}

/**
 * The first currency the wallet can't cover, in the kernel's words
 * ("You have 40 Freedom; this needs 120."), or null when it covers everything.
 */
export function shortfall(wallet: Wallet | undefined, spend: Amounts): string | null {
  for (const [key, n] of orderedAmounts(spend)) {
    const have = balanceOf(wallet, key);
    if (n > have) return `You have ${have} ${currencyLabel(key)}; this needs ${n}.`;
  }
  return null;
}

/** Can the wallet pay this Reward's catalog price as it stands? */
export function canAfford(wallet: Wallet | undefined, reward: RewardRow): boolean {
  return Object.keys(reward.price).length > 0 && shortfall(wallet, reward.price) === null;
}

/** What a redemption needs, before it goes to the kernel. */
export interface RedeemDraft {
  reward?: RewardRow | null;
  amounts: Amounts;
  note: string;
}

/** The first reason the draft would be refused, or null. Mirrors `RedeemAsync`. */
export function redeemProblem(draft: RedeemDraft, wallet?: Wallet): string | null {
  const spend = orderedAmounts(draft.amounts);
  for (const [key, n] of spend) {
    const c = currency(key);
    if (!c) return `Unknown currency '${key}'.`;
    if (!c.spendable) return `${c.label} is tracked, not spent; it can't be redeemed.`;
    if (!Number.isInteger(n) || n <= 0) return `Use a whole number of ${c.label} above zero.`;
  }
  const reward = draft.reward;
  if (!reward) {
    if (spend.length === 0) return "Say what to spend.";
    if (!draft.note.trim()) return "Say what this redemption is for: a note is required.";
  } else if (reward.archived) {
    return `'${reward.name}' is archived; restore it to redeem against it.`;
  } else if (reward.kind === "savings") {
    if (spend.length === 0) return `Say how many Coins to put toward '${reward.name}'.`;
    if (spend.some(([k]) => k !== "coins")) return `'${reward.name}' is a savings goal: only Coins go toward it.`;
  } else if (spend.length === 0 && Object.keys(reward.price).length === 0) {
    return `'${reward.name}' has no price yet: say what it costs this time.`;
  }
  const cost = spend.length > 0 ? draft.amounts : (reward?.price ?? {});
  return shortfall(wallet, cost);
}

/** Why a catalog price would be refused (RewardPrice.Normalize), or null. */
export function priceProblem(price: Amounts): string | null {
  for (const [key, n] of Object.entries(price)) {
    const c = currency(key);
    if (!c) return `Unknown currency '${key}'.`;
    if (!c.spendable) return `${c.label} is tracked, not spent; it can't be a price.`;
    if (!Number.isInteger(n) || n <= 0) return `Use a whole number of ${c.label} above zero.`;
  }
  return null;
}

/** Why a savings target would be refused, or null. Blank is no target. */
export function targetProblem(text: string): string | null {
  if (!text.trim()) return null;
  const n = Number(text);
  return Number.isFinite(n) && Math.round(n * 100) > 0
    ? null
    : "A savings target is a dollar amount above zero.";
}

/** What's typed in each currency's box, as typed. */
export type AmountDraft = Record<string, string>;

export function draftFrom(amounts: Amounts, currencies: readonly CurrencyDef[]): AmountDraft {
  return Object.fromEntries(
    currencies.map((c) => [c.key, amounts[c.key] ? String(amounts[c.key]) : ""]),
  );
}

/**
 * The amounts a draft names: blank and zero boxes drop out; anything else is kept as
 * typed (a fraction or a negative), so the rules can say what's wrong with it.
 */
export function amountsFrom(draft: AmountDraft): Amounts {
  const out: Amounts = {};
  for (const [key, text] of Object.entries(draft)) {
    if (!text.trim()) continue;
    const n = Number(text);
    if (n !== 0) out[key] = n;
  }
  return out;
}

export function sameAmounts(a: Amounts, b: Amounts): boolean {
  const keys = new Set([...Object.keys(a), ...Object.keys(b)]);
  return [...keys].every((k) => (a[k] ?? 0) === (b[k] ?? 0));
}

/** Toast text for a redemption. */
export function redemptionMessage(r: Redemption): string {
  const spent = formatAmounts(r.amounts);
  const usd = r.dollars != null ? ` (${formatUsd(r.dollars)})` : "";
  if (r.reward?.kind === "savings") {
    return `Put ${spent}${usd} toward ${r.reward.name}`;
  }
  return r.reward ? `Redeemed ${r.reward.name}: ${spent}${usd}` : `Redeemed ${spent}${usd}`;
}

/** One line describing a ledger row: what it was for. */
export function ledgerLabel(row: LedgerRow): string {
  if (row.entry === "open") return "Wallet opened";
  const what = row.subject?.title ?? row.note ?? "Custom redemption";
  if (row.entry === "redeem") return row.subject && row.note ? `${what} — ${row.note}` : what;
  const why = row.trigger ? triggerLabel(row.trigger) : "";
  return row.entry === "reversal" ? `Given back: ${what}` : why ? `${what} · ${why}` : what;
}

export function triggerLabel(trigger: string): string {
  if (trigger === "completed") return "completed";
  if (trigger === "followed") return "day followed";
  if (trigger === "partial") return "partial day";
  if (trigger === "established") return "established";
  if (trigger === "timed") return "time on the timer";
  if (trigger === "rank") return "rank-up";
  if (trigger === "diet") return "diet score";
  if (trigger === "fast") return "fasting";
  if (trigger === "steps" || trigger === "exercise" || trigger === "sleep") return "from Oura";
  const streak = /^streak_(\d+)$/.exec(trigger);
  return streak ? `${streak[1]}-day streak` : trigger;
}

/**
 * How an award's life-domain XP was multiplied (xp-domains plan §3.6): "❤️ 60 × 1.3" for each
 * currency paid at a multiplier, or "" when none was.
 */
export function multipliedLabel(award: Award): string {
  return Object.entries(award.multipliers ?? {})
    .filter(([key]) => award.base?.[key] !== undefined)
    .map(([key, m]) => `${currency(key)?.symbol ?? currencyLabel(key)} ${award.base![key]} × ${m}`)
    .join(", ");
}
