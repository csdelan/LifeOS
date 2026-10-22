/**
 * Rewards vocabulary and rules the UI shares with the kernel (REWARD-1 / REWARD-3 / REWARD-6).
 * The currency list mirrors `Currencies` in LifeOs.Domain; the redeem checks mirror
 * `RewardService.RedeemAsync` / `RewardPrice.Normalize`, so the dialog can say why
 * before it asks — the kernel still has the final word.
 */
import type {
  Amounts,
  Award,
  BountyLine,
  BountyResult,
  BountyTypeDefaults,
  HabitRow,
  HabitTargetResult,
  LedgerDay,
  LedgerRow,
  Redemption,
  RewardRow,
  SubjectDetail,
  SubjectListItem,
  TimerActionResult,
  Wallet,
  WalletLine,
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

/** Display order, as the Domain lists them. */
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

// ---------------------------------------------------------------------------
// The API types int64 / decimal as `number | string`; the UI needs numbers.
// ---------------------------------------------------------------------------

function num(value: unknown): number {
  const n = Number(value);
  return Number.isFinite(n) ? n : 0;
}

function numOrNull(value: unknown): number | null {
  return value === null || value === undefined || value === "" ? null : num(value);
}

export function normalizeAmounts(raw: unknown): Amounts {
  const out: Amounts = {};
  if (raw && typeof raw === "object") {
    for (const [k, v] of Object.entries(raw as Record<string, unknown>)) out[k] = num(v);
  }
  return out;
}

export function normalizeWallet(raw: Wallet): Wallet {
  return {
    openedAt: raw.openedAt ?? null,
    coinValueUsd: num(raw.coinValueUsd),
    balances: (raw.balances ?? []).map(
      (b): WalletLine => ({
        ...b,
        earned: num(b.earned),
        reversed: num(b.reversed),
        spent: num(b.spent),
        balance: num(b.balance),
      }),
    ),
  };
}

export function normalizeLedgerRow(raw: LedgerRow): LedgerRow {
  return {
    ...raw,
    amounts: normalizeAmounts(raw.amounts),
    dollars: numOrNull(raw.dollars),
    coinRate: numOrNull(raw.coinRate),
  };
}

export function normalizeLedgerDay(raw: LedgerDay): LedgerDay {
  return {
    date: raw.date,
    earned: normalizeAmounts(raw.earned),
    reversed: normalizeAmounts(raw.reversed),
    spent: normalizeAmounts(raw.spent),
    net: normalizeAmounts(raw.net),
  };
}

export function normalizeReward(raw: RewardRow): RewardRow {
  return {
    ...raw,
    kind: raw.kind === "savings" ? "savings" : "instant",
    price: normalizeAmounts(raw.price),
    targetUsd: numOrNull(raw.targetUsd),
    redemptions: num(raw.redemptions),
    coinsRedeemed: num(raw.coinsRedeemed),
    dollarsRedeemed: num(raw.dollarsRedeemed),
    percentOfTarget: numOrNull(raw.percentOfTarget),
    remainingUsd: numOrNull(raw.remainingUsd),
  };
}

export function normalizeAward(raw: Award): Award {
  return {
    ...raw,
    amounts: normalizeAmounts(raw.amounts),
    base: raw.base ? normalizeAmounts(raw.base) : null,
    multipliers: raw.multipliers ? normalizeAmounts(raw.multipliers) : null,
  };
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

/** The `awards` of an earning write's response (`{ awards }`), as numbers. */
export function normalizeAwards(raw: unknown): Award[] {
  const awards = (raw as { awards?: Award[] } | null | undefined)?.awards;
  return Array.isArray(awards) ? awards.map(normalizeAward) : [];
}

export function normalizeTimerAwards(raw: TimerActionResult): TimerActionResult {
  return { ...raw, awards: (raw.awards ?? []).map(normalizeAward) };
}

export function normalizeBountyLine(raw: BountyLine): BountyLine {
  return {
    ...raw,
    amounts: normalizeAmounts(raw.amounts),
    default: normalizeAmounts(raw.default),
    ...(raw.everyMinutes != null ? { everyMinutes: Number(raw.everyMinutes) } : {}),
  };
}

export function normalizeBountyResult(raw: BountyResult): BountyResult {
  return { ...raw, lines: (raw.lines ?? []).map(normalizeBountyLine) };
}

export function normalizeBountyDefaults(raw: BountyTypeDefaults[]): BountyTypeDefaults[] {
  return raw.map((d) => ({ ...d, lines: (d.lines ?? []).map(normalizeBountyLine) }));
}

export function normalizeListBounty(raw: SubjectListItem): SubjectListItem {
  return raw.bounty ? { ...raw, bounty: normalizeAmounts(raw.bounty) } : raw;
}

export function normalizeDetailBounty(raw: SubjectDetail): SubjectDetail {
  return raw.bounty ? { ...raw, bounty: raw.bounty.map(normalizeBountyLine) } : raw;
}

export function normalizeHabit(raw: HabitRow): HabitRow {
  const e = raw.establishment;
  return {
    ...raw,
    bounty: raw.bounty ? normalizeAmounts(raw.bounty) : null,
    establishment: e
      ? {
          ...e,
          minAdherence: num(e.minAdherence),
          windowDays: num(e.windowDays),
          followed: num(e.followed),
          partial: num(e.partial),
          missed: num(e.missed),
          concluded: num(e.concluded),
          adherence: numOrNull(e.adherence),
          ageDays: num(e.ageDays),
        }
      : null,
  };
}

export function normalizeHabitTarget(raw: HabitTargetResult): HabitTargetResult {
  return { ...raw, minAdherence: num(raw.minAdherence), windowDays: num(raw.windowDays) };
}

export function normalizeRedemption(raw: Redemption): Redemption {
  return {
    ...raw,
    amounts: normalizeAmounts(raw.amounts),
    dollars: numOrNull(raw.dollars),
    coinRate: numOrNull(raw.coinRate),
    reward: raw.reward ? normalizeReward(raw.reward) : null,
    wallet: normalizeWallet(raw.wallet),
  };
}
