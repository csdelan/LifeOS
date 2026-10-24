import { useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { AmountFields } from "@/components/rewards/amount-fields";
import { BountyChip } from "@/components/rewards/bounty-chip";
import type { BountyLine, SubjectType } from "@/lib/production-ui-types";
import {
  DEFAULT_EVERY_MINUTES,
  EVERY_MINUTES,
  TIMED,
  bountyProblem,
  bountyTriggerTitle,
  draftAmounts,
  draftForLine,
  draftMinutes,
  draftOnDefault,
  lineDraft,
  perMinutes,
  type BountyDraft,
} from "@/lib/bounty";
import { CURRENCY_GROUPS } from "@/lib/rewards";

/**
 * An item's bounty (REWARD-2), one row of currencies per trigger, pre-filled with what it
 * pays now — the type default until Chris prices it. Each row groups its boxes: what can
 * be spent, what is tracked, and the life domains' XP (REWARD-6). The draft holds only the triggers he
 * touched; `bountyWrites` turns it into the writes, saving only what changed. A Task's
 * time on the timer also takes the minutes each payment needs.
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
        const onDefault = draftOnDefault(line, value);
        const headingId = `${idPrefix}-${line.trigger}-title`;
        const minutesId = `${idPrefix}-${line.trigger}-every`;
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
                  onClick={() =>
                    onChange({ ...draft, [line.trigger]: lineDraft(line, line.default, DEFAULT_EVERY_MINUTES) })
                  }
                >
                  Use the default
                </Button>
              )}
            </div>
            {line.trigger === TIMED ? (
              <div className="space-y-1">
                <Label htmlFor={minutesId}>Minutes per payment</Label>
                <Input
                  id={minutesId}
                  type="number"
                  inputMode="numeric"
                  min={1}
                  step={1}
                  placeholder={String(DEFAULT_EVERY_MINUTES)}
                  className="w-24"
                  value={value[EVERY_MINUTES] ?? ""}
                  aria-describedby={`${minutesId}-hint`}
                  onChange={(e) =>
                    onChange({ ...draft, [line.trigger]: { ...value, [EVERY_MINUTES]: e.target.value } })
                  }
                />
                <p id={`${minutesId}-hint`} className="text-[0.6875rem] text-muted-foreground">
                  Paid on top of completing, once for every {minutesWords(draftMinutes(value))} the timer
                  recorded before the task was done. Nothing without the timer.
                </p>
              </div>
            ) : null}
            {CURRENCY_GROUPS.map((group) => (
              <fieldset key={group.id} className="space-y-1.5">
                <legend className="mb-1.5 text-[0.6875rem] font-medium text-muted-foreground">{group.label}</legend>
                <AmountFields
                  idPrefix={`${idPrefix}-${line.trigger}`}
                  currencies={group.currencies}
                  value={value}
                  min={0}
                  onChange={(next) => onChange({ ...draft, [line.trigger]: next })}
                />
              </fieldset>
            ))}
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

/** "minute", "15 minutes". */
function minutesWords(n: number): string {
  return Number.isInteger(n) && n > 1 ? `${n} minutes` : "minute";
}

/** Each trigger and what it pays, read-only; "default" marks the type default. */
export function BountySummary({ lines, draft = {} }: { lines: BountyLine[]; draft?: BountyDraft }) {
  return (
    <ul className="space-y-1">
      {lines.map((line) => {
        const typed = draftForLine(line, draft);
        const amounts = draftAmounts(typed);
        const custom = draft[line.trigger] ? !draftOnDefault(line, typed) : line.isCustom;
        return (
          <li key={line.trigger} className="flex flex-wrap items-center gap-2 text-sm">
            <span className="text-muted-foreground">{bountyTriggerTitle(line.trigger)}</span>
            {Object.keys(amounts).length > 0 ? (
              <BountyChip
                amounts={amounts}
                suffix={line.trigger === TIMED ? perMinutes(draftMinutes(typed)) : undefined}
              />
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
  note,
}: {
  idPrefix: string;
  type: SubjectType;
  lines: BountyLine[];
  draft: BountyDraft;
  onChange: (next: BountyDraft) => void;
  editing: boolean;
  startOpen?: boolean;
  /** A line under the heading: where the price comes from (a repeating Task's, TASKS-3). */
  note?: string;
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
      {note ? <p className="text-xs text-muted-foreground">{note}</p> : null}
      {showEditor ? (
        <BountyEditor idPrefix={idPrefix} type={type} lines={lines} draft={draft} onChange={onChange} />
      ) : (
        <BountySummary lines={lines} draft={draft} />
      )}
    </section>
  );
}
