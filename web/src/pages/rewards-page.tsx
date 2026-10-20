import { useState } from "react";
import { useNavigate, useSearch } from "@tanstack/react-router";
import { GiftIcon, PencilIcon, PiggyBankIcon, PlusIcon, Volume2Icon, VolumeXIcon } from "lucide-react";
import { SubjectDetail } from "@/components/detail/subject-detail";
import { EmptyState } from "@/components/primitives/empty-state";
import { CardSkeleton, ListSkeleton } from "@/components/primitives/skeletons";
import { RedeemDialog } from "@/components/rewards/redeem-dialog";
import { RewardFormDialog } from "@/components/rewards/reward-form-dialog";
import { RewardsHistory } from "@/components/rewards/rewards-history";
import { useDirty } from "@/components/shell/app-shell";
import { PeekPanel } from "@/components/shell/panels";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { useSoundOn } from "@/lib/celebrate";
import { formatTimestamp } from "@/lib/dates";
import { useLedger, useRewards, useWallet, useWrites } from "@/lib/queries";
import {
  canAfford,
  coinsToUsd,
  currency,
  CURRENCIES,
  formatAmounts,
  formatUsd,
  ledgerLabel,
  shortfall,
} from "@/lib/rewards";
import type { RewardRow, Wallet } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

const RECENT = 8;

/**
 * The Rewards tab (REWARD-3): the wallet, the catalog, and what was spent lately.
 * Redeeming celebrates (REWARD-4). History (REWARD-5) shows what was earned and spent by
 * day, week and month, and opens the item behind any entry in the side panel.
 */
export function RewardsPage() {
  const search = useSearch({ from: "/rewards" });
  const navigate = useNavigate({ from: "/rewards" });
  const { setDirty, requestNavigation } = useDirty();
  const view = search.view === "history" ? "history" : "wallet";
  const selected = search.selected;
  const wallet = useWallet();
  const [showArchived, setShowArchived] = useState(false);
  const rewards = useRewards(showArchived);
  const ledger = useLedger(RECENT);
  const [soundOn, setSoundOn] = useSoundOn();
  const [redeeming, setRedeeming] = useState<{ reward: RewardRow | null } | null>(null);
  const [editing, setEditing] = useState<{ reward: RewardRow | null } | null>(null);

  const rows = rewards.data ?? [];
  const live = rows.filter((r) => !r.archived);
  const treats = live.filter((r) => r.kind === "instant");
  const goals = live.filter((r) => r.kind === "savings");
  const archived = rows.filter((r) => r.archived);

  const showView = (next: "wallet" | "history") =>
    void navigate({ search: (prev) => ({ ...prev, view: next === "history" ? "history" : undefined }) });
  const openSubject = (id: string) =>
    requestNavigation(() => void navigate({ search: (prev) => ({ ...prev, selected: id }) }));
  const closeSubject = () =>
    requestNavigation(() => void navigate({ search: (prev) => ({ ...prev, selected: undefined }) }));

  return (
    <div className="flex h-full min-h-0">
      <Tabs
        value={view}
        onValueChange={(v) => showView(v === "history" ? "history" : "wallet")}
        className="flex h-full min-h-0 min-w-0 flex-1 flex-col gap-0"
      >
        <header className="shrink-0 border-b px-4 pt-4 md:px-6">
          <div className="flex flex-wrap items-end justify-between gap-3">
            <div>
              <p className="type-scale-section text-primary">Rewards</p>
              <h1 className="font-heading text-2xl">Spend what you've earned</h1>
              <p className="mt-1 text-xs text-muted-foreground">
                Every redemption is logged with what it was for. Coins are real money, at{" "}
                {formatUsd(wallet.data?.coinValueUsd ?? 0.5)} each.
              </p>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <div className="flex items-center gap-2 pr-1">
                {soundOn ? (
                  <Volume2Icon className="size-4 text-muted-foreground" aria-hidden />
                ) : (
                  <VolumeXIcon className="size-4 text-muted-foreground" aria-hidden />
                )}
                <Label htmlFor="rewards-sound" className="text-xs font-normal">
                  Sound
                </Label>
                <Switch id="rewards-sound" checked={soundOn} onCheckedChange={setSoundOn} />
              </div>
              <Button variant="outline" onClick={() => setEditing({ reward: null })}>
                <PlusIcon aria-hidden /> Add reward
              </Button>
              <Button onClick={() => setRedeeming({ reward: null })} disabled={!wallet.data}>
                Redeem something else
              </Button>
            </div>
          </div>
          <TabsList variant="line" className="mt-3 -mb-px h-auto">
            <TabsTrigger value="wallet">Wallet &amp; rewards</TabsTrigger>
            <TabsTrigger value="history">History</TabsTrigger>
          </TabsList>
        </header>

        <TabsContent value="history" className="min-h-0 flex-1 overflow-y-auto px-4 py-5 md:px-6">
          <RewardsHistory onOpenSubject={openSubject} />
        </TabsContent>

        <TabsContent value="wallet" className="min-h-0 flex-1 space-y-8 overflow-y-auto px-4 py-5 md:px-6">
          <WalletSection wallet={wallet.data} isLoading={wallet.isLoading} isError={wallet.isError} />

          {rewards.isLoading ? <ListSkeleton rows={3} /> : null}
          {rewards.isError ? (
            <EmptyState
              tone="error"
              icon={GiftIcon}
              title="The catalog failed to load"
              description="This is not an empty catalog — the reader never returned."
            />
          ) : null}
          {!rewards.isLoading && !rewards.isError && live.length === 0 ? (
            <EmptyState
              icon={GiftIcon}
              title="No rewards yet"
              description="Add what you're working toward: a treat with a price, or a savings goal."
              action={
                <Button onClick={() => setEditing({ reward: null })}>
                  <PlusIcon aria-hidden /> Add reward
                </Button>
              }
            />
          ) : null}

          {treats.length > 0 ? (
            <section aria-labelledby="rewards-treats">
              <h2 id="rewards-treats" className="type-scale-section mb-3 text-muted-foreground">
                Treats
              </h2>
              <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                {treats.map((r) => (
                  <li key={r.id}>
                    <TreatCard
                      reward={r}
                      wallet={wallet.data}
                      onRedeem={() => setRedeeming({ reward: r })}
                      onEdit={() => setEditing({ reward: r })}
                    />
                  </li>
                ))}
              </ul>
            </section>
          ) : null}

          {goals.length > 0 ? (
            <section aria-labelledby="rewards-savings">
              <h2 id="rewards-savings" className="type-scale-section mb-3 text-muted-foreground">
                Savings goals
              </h2>
              <ul className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                {goals.map((r) => (
                  <li key={r.id}>
                    <SavingsCard
                      reward={r}
                      wallet={wallet.data}
                      onDeposit={() => setRedeeming({ reward: r })}
                      onEdit={() => setEditing({ reward: r })}
                    />
                  </li>
                ))}
              </ul>
            </section>
          ) : null}

          <section aria-labelledby="rewards-recent">
            <div className="mb-3 flex items-baseline justify-between gap-3">
              <h2 id="rewards-recent" className="type-scale-section text-muted-foreground">
                Recent activity
              </h2>
              <Button variant="link" size="sm" className="h-auto p-0" onClick={() => showView("history")}>
                See all history
              </Button>
            </div>
            {ledger.isLoading ? <ListSkeleton rows={4} /> : null}
            {ledger.isError ? (
              <p role="alert" className="text-sm text-destructive">
                The ledger failed to load.
              </p>
            ) : null}
            {ledger.data && ledger.data.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                Nothing yet. Complete a task or follow a habit to start earning.
              </p>
            ) : null}
            <ul className="divide-y rounded-xl ring-1 ring-foreground/10">
              {(ledger.data ?? []).map((row) => (
                <li key={row.id} className="flex items-baseline justify-between gap-3 px-3 py-2.5 text-sm">
                  <div className="min-w-0">
                    <p className="truncate">{ledgerLabel(row)}</p>
                    <p className="text-[0.6875rem] text-muted-foreground">
                      {formatTimestamp(row.occurredAt)}
                      {row.dollars != null ? ` · ${formatUsd(row.dollars)}` : ""}
                    </p>
                  </div>
                  <span
                    className={cn(
                      "shrink-0 text-xs tabular-nums",
                      row.entry === "earn" ? "text-success" : "text-muted-foreground",
                    )}
                  >
                    {formatAmounts(row.amounts, { signed: true })}
                  </span>
                </li>
              ))}
            </ul>
          </section>

          <section aria-label="Archived rewards" className="space-y-3">
            <div className="flex items-center gap-2">
              <Switch id="rewards-archived" checked={showArchived} onCheckedChange={setShowArchived} />
              <Label htmlFor="rewards-archived" className="text-xs font-normal">
                Show archived rewards
              </Label>
            </div>
            {showArchived && !rewards.isLoading ? (
              archived.length === 0 ? (
                <p className="text-sm text-muted-foreground">Nothing archived.</p>
              ) : (
                <ul className="space-y-2">
                  {archived.map((r) => (
                    <ArchivedRow key={r.id} reward={r} />
                  ))}
                </ul>
              )
            ) : null}
          </section>
        </TabsContent>

        <RedeemDialog
          open={!!redeeming}
          onOpenChange={(open) => !open && setRedeeming(null)}
          reward={redeeming?.reward ?? null}
          wallet={wallet.data}
        />
        <RewardFormDialog
          open={!!editing}
          onOpenChange={(open) => !open && setEditing(null)}
          reward={editing?.reward}
        />
      </Tabs>
      <PeekPanel open={!!selected} title="Detail" onClose={closeSubject}>
        {selected ? (
          <SubjectDetail subjectId={selected} onDirtyChange={setDirty} onClose={closeSubject} />
        ) : null}
      </PeekPanel>
    </div>
  );
}

function WalletSection({
  wallet,
  isLoading,
  isError,
}: {
  wallet?: Wallet;
  isLoading: boolean;
  isError: boolean;
}) {
  return (
    <section aria-labelledby="rewards-wallet">
      <h2 id="rewards-wallet" className="type-scale-section mb-3 text-muted-foreground">
        Wallet
      </h2>
      {isLoading ? (
        <div className="grid gap-3 sm:grid-cols-3">
          <CardSkeleton />
          <CardSkeleton />
          <CardSkeleton />
        </div>
      ) : null}
      {isError ? (
        <p role="alert" className="text-sm text-destructive">
          The wallet failed to load.
        </p>
      ) : null}
      {wallet ? (
        <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-6">
          {CURRENCIES.map((c) => {
            const line = wallet.balances.find((b) => b.currency === c.key);
            const balance = line?.balance ?? 0;
            return (
              <li
                key={c.key}
                className="rounded-xl px-3 py-3 ring-1 ring-foreground/10"
                aria-label={`${c.label}: ${balance.toLocaleString()}`}
              >
                <p className="flex items-center gap-1.5 text-xs text-muted-foreground">
                  <span aria-hidden>{c.symbol}</span> {c.label}
                  {c.spendable ? null : (
                    <span className="ml-auto rounded-full bg-muted px-1.5 text-[0.625rem]">Tracked</span>
                  )}
                </p>
                <p className="font-heading text-2xl tabular-nums">{balance.toLocaleString()}</p>
                <p className="text-[0.6875rem] text-muted-foreground">
                  {c.key === "coins" ? `≈ ${formatUsd(coinsToUsd(balance, wallet.coinValueUsd))}` : c.hint}
                </p>
              </li>
            );
          })}
        </ul>
      ) : null}
    </section>
  );
}

function PriceChips({ reward }: { reward: RewardRow }) {
  const entries = Object.entries(reward.price);
  if (entries.length === 0) {
    return <p className="text-xs text-muted-foreground">No price yet</p>;
  }
  return (
    <p className="flex flex-wrap gap-1.5">
      {entries.map(([key, n]) => {
        const c = currency(key);
        return (
          <span
            key={key}
            className="rounded-full bg-[var(--type-reward)]/12 px-2 py-0.5 text-xs font-medium text-[var(--type-reward)]"
          >
            <span aria-hidden>{c?.symbol} </span>
            {n.toLocaleString()} {c?.label ?? key}
          </span>
        );
      })}
    </p>
  );
}

function CardShell({
  reward,
  onEdit,
  children,
}: {
  reward: RewardRow;
  onEdit: () => void;
  children: React.ReactNode;
}) {
  return (
    <div className="flex h-full flex-col gap-2 rounded-xl px-4 py-3 ring-1 ring-foreground/10">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <h3 className="truncate font-medium">{reward.name}</h3>
          {reward.description ? (
            <p className="text-xs text-muted-foreground">{reward.description}</p>
          ) : null}
        </div>
        <Button variant="ghost" size="icon-sm" onClick={onEdit} aria-label={`Edit ${reward.name}`}>
          <PencilIcon aria-hidden />
        </Button>
      </div>
      {children}
    </div>
  );
}

function TreatCard({
  reward,
  wallet,
  onRedeem,
  onEdit,
}: {
  reward: RewardRow;
  wallet?: Wallet;
  onRedeem: () => void;
  onEdit: () => void;
}) {
  const priced = Object.keys(reward.price).length > 0;
  const short = priced ? shortfall(wallet, reward.price) : null;
  return (
    <CardShell reward={reward} onEdit={onEdit}>
      <PriceChips reward={reward} />
      <p className="text-[0.6875rem] text-muted-foreground">
        {reward.redemptions === 0
          ? "Not redeemed yet"
          : `Redeemed ${reward.redemptions} time${reward.redemptions === 1 ? "" : "s"}`}
        {reward.lastRedeemedAt ? ` · last ${formatTimestamp(reward.lastRedeemedAt)}` : ""}
      </p>
      <div className="mt-auto flex flex-wrap items-center gap-2 pt-1">
        <Button
          size="sm"
          onClick={onRedeem}
          disabled={!wallet || (priced && !canAfford(wallet, reward))}
          aria-label={`Redeem ${reward.name}`}
          aria-describedby={short ? `short-${reward.id}` : undefined}
        >
          <GiftIcon aria-hidden /> Redeem
        </Button>
        {short ? (
          <p id={`short-${reward.id}`} className="text-xs text-muted-foreground">
            {short}
          </p>
        ) : null}
      </div>
    </CardShell>
  );
}

function SavingsCard({
  reward,
  wallet,
  onDeposit,
  onEdit,
}: {
  reward: RewardRow;
  wallet?: Wallet;
  onDeposit: () => void;
  onEdit: () => void;
}) {
  const coins = wallet?.balances.find((b) => b.currency === "coins")?.balance ?? 0;
  const percent = Math.min(reward.percentOfTarget ?? 0, 100);
  return (
    <CardShell reward={reward} onEdit={onEdit}>
      {reward.targetUsd ? (
        <div className="space-y-1">
          <div
            role="progressbar"
            aria-label={`${reward.name} progress`}
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={percent}
            className="h-2 overflow-hidden rounded-full bg-muted"
          >
            <div className="h-full rounded-full bg-[var(--type-reward)]" style={{ width: `${percent}%` }} />
          </div>
          <p className="text-xs">
            {formatUsd(reward.dollarsRedeemed)} of {formatUsd(reward.targetUsd)}
            <span className="text-muted-foreground">
              {" "}
              · {reward.percentOfTarget ?? 0}%
              {reward.remainingUsd ? ` · ${formatUsd(reward.remainingUsd)} to go` : " · reached"}
            </span>
          </p>
        </div>
      ) : (
        <p className="text-xs">
          {formatUsd(reward.dollarsRedeemed)} saved{" "}
          <span className="text-muted-foreground">· no target</span>
        </p>
      )}
      <div className="mt-auto flex flex-wrap items-center gap-2 pt-1">
        <Button
          size="sm"
          onClick={onDeposit}
          disabled={!wallet || coins <= 0}
          aria-label={`Deposit into ${reward.name}`}
        >
          <PiggyBankIcon aria-hidden /> Deposit
        </Button>
        {wallet && coins <= 0 ? (
          <p className="text-xs text-muted-foreground">No Coins to deposit yet.</p>
        ) : null}
      </div>
    </CardShell>
  );
}

function ArchivedRow({ reward }: { reward: RewardRow }) {
  const writes = useWrites();
  const [pending, setPending] = useState(false);
  return (
    <li className="flex items-center justify-between gap-3 rounded-xl px-3 py-2 ring-1 ring-foreground/10">
      <span className="truncate text-sm text-muted-foreground">{reward.name}</span>
      <Button
        size="sm"
        variant="outline"
        disabled={pending}
        aria-label={`Restore ${reward.name}`}
        onClick={() => {
          setPending(true);
          void writes.restore(reward.id).finally(() => setPending(false));
        }}
      >
        Restore
      </Button>
    </li>
  );
}
