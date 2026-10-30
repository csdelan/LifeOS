import { Fragment, useId, useState, type ReactNode } from "react";
import { ArchiveIcon, CameraIcon, InfoIcon, PencilIcon } from "lucide-react";
import { ShotsEditor } from "@/components/environment/shots-editor";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip";
import { formatDay } from "@/lib/dates";
import { DEFAULT_PACE, DEFAULT_WEIGHT, NAME_MAX, PACE_MAX, PACE_MIN, WEIGHT_MAX, WEIGHT_MIN } from "@/lib/environment";
import type { SpaceRequest, SpaceRow } from "@/lib/production-ui-types";
import { useEnvironment, useSpaces, useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";

const PACES = Array.from({ length: PACE_MAX - PACE_MIN + 1 }, (_, i) => {
  const id = String(PACE_MIN + i);
  return { id, label: id };
});

const WEIGHT_HELP = `Weight ${WEIGHT_MIN}–${WEIGHT_MAX}: how much the space counts in the Home Score.`;
const PACE_HELP = `Pace: how fast Tidiness and Cleanliness fade, from ${PACE_MIN} (slow to get messy) to ${PACE_MAX} (fast).`;

/** A space's name, free to wrap after each slash ("Stairway/Foyer/Halls") on a narrow phone. */
function wrappableName(name: string): ReactNode {
  const parts = name.split("/");
  return parts.map((part, i) => (
    <Fragment key={i}>
      {part}
      {i < parts.length - 1 ? (
        <>
          /<wbr />
        </>
      ) : null}
    </Fragment>
  ));
}

/** The table's columns: Space, Weight, Pace, % of score and the row's buttons. */
const COLUMNS = 5;

interface Fields {
  name: string;
  weight: string;
  pace: string;
}

/**
 * A small ⓘ that says what a field means on hover or focus. Help in words is for the first
 * view only (Chris: "fine for the first view, but from thereinafter they are no longer
 * needed"); after that it lives here.
 */
function HelpTip({ label, children }: { label: string; children: ReactNode }) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <button
          type="button"
          aria-label={label}
          className="inline-flex size-5 items-center justify-center rounded-full text-muted-foreground hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50"
        >
          <InfoIcon className="size-3.5" aria-hidden />
        </button>
      </TooltipTrigger>
      <TooltipContent className="max-w-64">{children}</TooltipContent>
    </Tooltip>
  );
}

/** One of a row's small buttons: an icon, its name in a tooltip and for screen readers. */
function RowButton({
  label,
  tip,
  children,
  ...props
}: {
  label: string;
  tip: string;
  children: ReactNode;
} & Omit<React.ComponentProps<typeof Button>, "aria-label" | "children">) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button
          type="button"
          variant="ghost"
          size="icon-sm"
          aria-label={label}
          className="size-11 md:size-7"
          {...props}
        >
          {children}
        </Button>
      </TooltipTrigger>
      <TooltipContent>{tip}</TooltipContent>
    </Tooltip>
  );
}

/**
 * Name, weight (1–10) and pace (1–5), for adding a space or changing one: one line where
 * there's room. `help` says what weight and pace mean in words (the first view) or in ⓘ tips.
 */
function SpaceForm({
  initial,
  submitLabel,
  help,
  onSubmit,
  onCancel,
}: {
  initial: Fields;
  submitLabel: string;
  help: "words" | "tips";
  onSubmit: (req: SpaceRequest) => Promise<unknown>;
  onCancel?: () => void;
}) {
  const ids = useId();
  const [fields, setFields] = useState(initial);
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const set = (next: Partial<Fields>) => {
    setError(null);
    setFields((f) => ({ ...f, ...next }));
  };

  async function submit() {
    if (pending) return;
    setPending(true);
    setError(null);
    try {
      await onSubmit({
        name: fields.name,
        weight: Number(fields.weight),
        pace: Number(fields.pace),
      });
      if (!onCancel) setFields(initial);
      onCancel?.();
    } catch (e) {
      setError(e instanceof Error ? e.message : "That didn't save. Nothing was written.");
    } finally {
      setPending(false);
    }
  }

  return (
    <form
      className="space-y-2"
      onSubmit={(e) => {
        e.preventDefault();
        void submit();
      }}
    >
      <div className="flex flex-wrap items-end gap-3">
        <div className="min-w-40 flex-1 space-y-1.5">
          <Label htmlFor={`${ids}-name`}>Name</Label>
          <Input
            id={`${ids}-name`}
            value={fields.name}
            maxLength={NAME_MAX}
            autoComplete="off"
            placeholder="Office"
            disabled={pending}
            onChange={(e) => set({ name: e.target.value })}
          />
        </div>
        <div className="w-24 space-y-1.5">
          <div className="flex items-center gap-1">
            <Label htmlFor={`${ids}-weight`}>Weight</Label>
            {help === "tips" ? <HelpTip label="About weight">{WEIGHT_HELP}</HelpTip> : null}
          </div>
          <Input
            id={`${ids}-weight`}
            type="number"
            inputMode="numeric"
            min={WEIGHT_MIN}
            max={WEIGHT_MAX}
            step={1}
            value={fields.weight}
            disabled={pending}
            aria-describedby={`${ids}-weight-help`}
            onChange={(e) => set({ weight: e.target.value })}
          />
        </div>
        <div className="flex items-center gap-1">
          <ToggleGroup label="Pace" value={fields.pace} options={PACES} onChange={(pace) => set({ pace })} />
          {help === "tips" ? <HelpTip label="About pace">{PACE_HELP}</HelpTip> : null}
        </div>
        <div className="flex gap-2">
          <Button type="submit" size="sm" className="min-h-11 md:min-h-8" disabled={pending}>
            {pending ? "Saving…" : submitLabel}
          </Button>
          {onCancel ? (
            <Button type="button" variant="outline" size="sm" className="min-h-11 md:min-h-8" onClick={onCancel}>
              Cancel
            </Button>
          ) : null}
        </div>
      </div>
      {/* Read with the weight field either way; shown only on the first view. */}
      <p id={`${ids}-weight-help`} className={cn("text-xs text-muted-foreground", help === "tips" && "sr-only")}>
        {WEIGHT_HELP}
      </p>
      {help === "words" ? <p className="text-xs text-muted-foreground">{PACE_HELP}</p> : null}
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
    </form>
  );
}

/**
 * The spaces editor (HOME-1, HOME-4): a table of the spaces, one line each, with weight, pace
 * and share of the Home Score, and small buttons to see its shots (the spots it's
 * photographed from), change it, or archive it (it leaves the score from today; Restore
 * brings it back for all of history). A weight or pace change re-reads every past day, as any
 * retune does. What weight and pace mean is said in words until the first space exists, then
 * in ⓘ tips. On a phone weight and pace sit under the name.
 */
export function SpacesEditor() {
  const titleId = useId();
  const spaces = useSpaces();
  const environment = useEnvironment();
  const writes = useWrites();
  const [editing, setEditing] = useState<string | null>(null);
  const [shooting, setShooting] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const live = (spaces.data ?? []).filter((s) => s.archivedAt == null);
  const archived = (spaces.data ?? []).filter((s) => s.archivedAt != null);
  const shotsOf = (id: string) => environment.data?.spaces.find((s) => s.id === id)?.shots ?? [];
  const help = live.length === 0 ? "words" : "tips";

  async function archive(space: SpaceRow, restore: boolean) {
    setBusy(space.id);
    setError(null);
    try {
      if (restore) await writes.restore(space.urn);
      else await writes.archive(space.urn);
    } catch (e) {
      setError(e instanceof Error ? e.message : "That didn't save. Nothing was written.");
    } finally {
      setBusy(null);
    }
  }

  return (
    <section aria-labelledby={titleId} className="space-y-3 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <h2 id={titleId} className="font-heading text-xl">
        Manage spaces
      </h2>

      {spaces.isLoading ? <ListSkeleton rows={3} /> : null}
      {spaces.isError ? (
        <p role="alert" className="text-sm text-destructive">
          The spaces failed to load.
        </p>
      ) : null}
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : null}

      {live.length > 0 ? (
        <table className="w-full text-sm" aria-label="Spaces in the Home Score">
          <thead>
            <tr className="border-b text-left text-xs text-muted-foreground">
              <th scope="col" className="py-1.5 pr-2 font-medium">
                Space
              </th>
              <th scope="col" className="hidden w-20 px-2 py-1.5 font-medium sm:table-cell">
                <span className="flex items-center justify-end gap-1">
                  Weight <HelpTip label="About weight">{WEIGHT_HELP}</HelpTip>
                </span>
              </th>
              <th scope="col" className="hidden w-20 px-2 py-1.5 font-medium sm:table-cell">
                <span className="flex items-center justify-end gap-1">
                  Pace <HelpTip label="About pace">{PACE_HELP}</HelpTip>
                </span>
              </th>
              <th scope="col" className="w-16 px-2 py-1.5 text-right font-medium sm:w-24">
                % of score
              </th>
              <th scope="col" className="py-1.5 pl-2">
                <span className="sr-only">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody className="divide-y">
            {live.map((space) => {
              const shots = shotsOf(space.id);
              if (editing === space.id) {
                return (
                  <tr key={space.id} data-testid="space-row">
                    <td colSpan={COLUMNS} className="py-2.5">
                      <SpaceForm
                        initial={{
                          name: space.name,
                          weight: String(space.weight),
                          pace: String(space.pace),
                        }}
                        submitLabel="Save"
                        help="tips"
                        onSubmit={async (req) => {
                          // Only what changed: changing nothing writes nothing.
                          const patch: SpaceRequest = {};
                          if (req.name !== space.name) patch.name = req.name;
                          if (req.weight !== space.weight) patch.weight = req.weight;
                          if (req.pace !== space.pace) patch.pace = req.pace;
                          if (Object.keys(patch).length > 0) await writes.setSpace(space.id, patch);
                        }}
                        onCancel={() => setEditing(null)}
                      />
                    </td>
                  </tr>
                );
              }
              return (
                <Fragment key={space.id}>
                  <tr data-testid="space-row" className="align-middle">
                    <th scope="row" className="py-1 pr-2 text-left font-medium [overflow-wrap:anywhere]">
                      {wrappableName(space.name)}
                      <span className="block text-xs font-normal text-muted-foreground tabular-nums sm:hidden">
                        weight {space.weight} · pace {space.pace}
                      </span>
                    </th>
                    <td className="hidden px-2 py-1 text-right tabular-nums sm:table-cell" data-testid="space-weight">
                      {space.weight}
                    </td>
                    <td className="hidden px-2 py-1 text-right tabular-nums sm:table-cell" data-testid="space-pace">
                      {space.pace}
                    </td>
                    <td className="px-2 py-1 text-right tabular-nums" data-testid="space-share">
                      {space.share != null ? `${space.share}%` : "—"}
                    </td>
                    <td className="py-1 pl-2">
                      <div className="flex justify-end gap-0.5">
                        <RowButton
                          label={`Shots of ${space.name}`}
                          tip={shots.length === 1 ? "1 shot" : `${shots.length} shots`}
                          aria-expanded={shooting === space.id}
                          disabled={shots.length === 0}
                          onClick={() => setShooting(shooting === space.id ? null : space.id)}
                        >
                          <CameraIcon aria-hidden />
                        </RowButton>
                        <RowButton label={`Change ${space.name}`} tip="Change" onClick={() => setEditing(space.id)}>
                          <PencilIcon aria-hidden />
                        </RowButton>
                        <RowButton
                          label={`Archive ${space.name}`}
                          tip="Archive"
                          disabled={busy === space.id}
                          onClick={() => void archive(space, false)}
                        >
                          <ArchiveIcon aria-hidden />
                        </RowButton>
                      </div>
                    </td>
                  </tr>
                  {shooting === space.id && shots.length > 0 ? (
                    <tr>
                      <td colSpan={COLUMNS} className="pb-3">
                        <ShotsEditor spaceId={space.id} space={space.name} shots={shots} />
                      </td>
                    </tr>
                  ) : null}
                </Fragment>
              );
            })}
          </tbody>
        </table>
      ) : null}

      <div className="space-y-2 rounded-lg bg-muted/60 p-3">
        <h3 className="text-sm font-medium">Add a space</h3>
        <SpaceForm
          initial={{
            name: "",
            weight: String(DEFAULT_WEIGHT),
            pace: String(DEFAULT_PACE),
          }}
          submitLabel="Add space"
          help={help}
          onSubmit={(req) =>
            writes.addSpace({
              name: req.name ?? "",
              weight: req.weight,
              pace: req.pace,
            })
          }
        />
      </div>

      {archived.length > 0 ? (
        <div className="space-y-1">
          <h3 className="text-sm font-medium text-muted-foreground">Archived</h3>
          <ul className="divide-y" aria-label="Archived spaces">
            {archived.map((space) => (
              <li key={space.id} className="flex flex-wrap items-center justify-between gap-2 py-1">
                <span className="text-sm">
                  {space.name}{" "}
                  <span className="text-xs text-muted-foreground">
                    · archived {space.archivedOn ? formatDay(space.archivedOn) : ""}
                  </span>
                </span>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="min-h-11 md:min-h-0"
                  aria-label={`Restore ${space.name}`}
                  disabled={busy === space.id}
                  onClick={() => void archive(space, true)}
                >
                  Restore
                </Button>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </section>
  );
}
