import { useState } from "react";
import { Button } from "@/components/ui/button";
import { AmountFields } from "@/components/rewards/amount-fields";
import { BountyChip } from "@/components/rewards/bounty-chip";
import type { BountyLine, SubjectType } from "@/lib/production-ui-types";
import { bountyProblem, bountyTriggerTitle, draftForLine, type BountyDraft } from "@/lib/bounty";
import { CURRENCIES, amountsFrom, draftFrom, sameAmounts } from "@/lib/rewards";

/**
 * An item's bounty (REWARD-2), one row of currencies per trigger, pre-filled with what it
 * pays now — the type default until Chris prices it. The draft holds only the triggers he
 * touched; `bountyWrites` turns it into the writes, saving only what changed.
 */
export function BountyEditor({
  idPrefix,
  type,
  lines,
  draft,
  onChange,
}: {
  idPrefix: string;
  type: SubjectType;
  lines: BountyLine[];
  draft: BountyDraft;
  onChange: (next: BountyDraft) => void;
}) {
  const problem = bountyProblem(draft);
  return (
    <div className="space-y-3">
      {lines.map((line) => {
        const value = draftForLine(line, draft);
        const onDefault = sameAmounts(amountsFrom(value), line.default);
        const headingId = `${idPrefix}-${line.trigger}-title`;
        return (
          <div
            key={line.trigger}
            role="group"
            aria-labelledby={headingId}
            className="space-y-2 rounded-lg border border-border p-3"
          >
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p id={headingId} className="text-sm font-medium">
                {bountyTriggerTitle(line.trigger)}
              </p>
              {onDefault ? (
                <span className="text-[0.6875rem] text-muted-foreground">Type default</span>
              ) : (
                <Button
                  type="button"
                  variant="ghost"
                  size="xs"
                  onClick={() => onChange({ ...draft, [line.trigger]: draftFrom(line.default, CURRENCIES) })}
                >
                  Use the default
                </Button>
              )}
            </div>
            <AmountFields
              idPrefix={`${idPrefix}-${line.trigger}`}
              currencies={CURRENCIES}
              value={value}
              min={0}
              onChange={(next) => onChange({ ...draft, [line.trigger]: next })}
            />
          </div>
        );
      })}
      <p className="text-xs text-muted-foreground">
        {type === "Habit" ? "A partial day pays half of a followed one. " : null}
        Clear every box to pay nothing. Changing a bounty never changes what was already paid.
      </p>
      {problem ? (
        <p role="alert" className="text-xs text-destructive">
          {problem}
        </p>
      ) : null}
    </div>
  );
}

/** Each trigger and what it pays, read-only; "default" marks the type default. */
export function BountySummary({ lines, draft = {} }: { lines: BountyLine[]; draft?: BountyDraft }) {
  return (
    <ul className="space-y-1">
      {lines.map((line) => {
        const amounts = amountsFrom(draftForLine(line, draft));
        const custom = draft[line.trigger] ? !sameAmounts(amounts, line.default) : line.isCustom;
        return (
          <li key={line.trigger} className="flex flex-wrap items-center gap-2 text-sm">
            <span className="text-muted-foreground">{bountyTriggerTitle(line.trigger)}</span>
            {Object.keys(amounts).length > 0 ? (
              <BountyChip amounts={amounts} />
            ) : (
              <span className="text-[0.75rem] text-muted-foreground">pays nothing</span>
            )}
            {custom ? null : <span className="text-[0.6875rem] text-muted-foreground">default</span>}
          </li>
        );
      })}
    </ul>
  );
}

/**
 * The Bounty section of a form: a summary with Change, which opens the editor. Shown for
 * the types that earn, once their lines have loaded.
 */
export function BountySection({
  idPrefix,
  type,
  lines,
  draft,
  onChange,
  editing,
  startOpen = false,
}: {
  idPrefix: string;
  type: SubjectType;
  lines: BountyLine[];
  draft: BountyDraft;
  onChange: (next: BountyDraft) => void;
  editing: boolean;
  startOpen?: boolean;
}) {
  const [open, setOpen] = useState(startOpen);
  if (lines.length === 0) return null;
  const showEditor = editing && open;
  return (
    <section className="space-y-2" aria-labelledby={`${idPrefix}-heading`}>
      <div className="flex items-center justify-between gap-2">
        <p id={`${idPrefix}-heading`} className="type-scale-section text-muted-foreground">
          Bounty
        </p>
        {editing ? (
          <Button
            type="button"
            variant="outline"
            size="xs"
            aria-expanded={showEditor}
            onClick={() => setOpen((v) => !v)}
          >
            {showEditor ? "Done" : "Change"}
          </Button>
        ) : null}
      </div>
      {showEditor ? (
        <BountyEditor idPrefix={idPrefix} type={type} lines={lines} draft={draft} onChange={onChange} />
      ) : (
        <BountySummary lines={lines} draft={draft} />
      )}
    </section>
  );
}
