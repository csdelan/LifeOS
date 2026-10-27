import { useId, useState } from "react";
import { ShotsEditor } from "@/components/environment/shots-editor";
import { ToggleGroup } from "@/components/primitives/toggle-group";
import { ListSkeleton } from "@/components/primitives/skeletons";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { formatDay } from "@/lib/dates";
import { DEFAULT_PACE, DEFAULT_WEIGHT, NAME_MAX, PACE_MAX, PACE_MIN, WEIGHT_MAX, WEIGHT_MIN } from "@/lib/environment";
import type { SpaceRequest, SpaceRow } from "@/lib/production-ui-types";
import { useEnvironment, useSpaces, useWrites } from "@/lib/queries";

const PACES = Array.from({ length: PACE_MAX - PACE_MIN + 1 }, (_, i) => {
  const id = String(PACE_MIN + i);
  return { id, label: id };
});

interface Fields {
  name: string;
  weight: string;
  pace: string;
}

/** Name, weight (1–10) and pace (1–5), for adding a space or changing one. */
function SpaceForm({
  initial,
  submitLabel,
  onSubmit,
  onCancel,
}: {
  initial: Fields;
  submitLabel: string;
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
      className="space-y-3"
      onSubmit={(e) => {
        e.preventDefault();
        void submit();
      }}
    >
      <div className="grid gap-3 sm:grid-cols-[minmax(0,1fr)_6rem]">
        <div className="space-y-1.5">
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
        <div className="space-y-1.5">
          <Label htmlFor={`${ids}-weight`}>Weight</Label>
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
      </div>
      <p id={`${ids}-weight-help`} className="text-xs text-muted-foreground">
        Weight {WEIGHT_MIN}–{WEIGHT_MAX}: how much the space counts in the Home Score.
      </p>
      <div className="space-y-1">
        <ToggleGroup label="Pace" value={fields.pace} options={PACES} onChange={(pace) => set({ pace })} />
        <p className="text-xs text-muted-foreground">
          Pace: how fast Tidiness and Cleanliness fade, from {PACE_MIN} (slow to get messy) to {PACE_MAX} (fast).
        </p>
      </div>
      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
      <div className="flex gap-2">
        <Button type="submit" size="sm" className="min-h-11 md:min-h-0" disabled={pending}>
          {pending ? "Saving…" : submitLabel}
        </Button>
        {onCancel ? (
          <Button type="button" variant="outline" size="sm" className="min-h-11 md:min-h-0" onClick={onCancel}>
            Cancel
          </Button>
        ) : null}
      </div>
    </form>
  );
}

/**
 * The spaces editor (HOME-1, HOME-4): each space with its weight, pace and share of the Home
 * Score; add one, change one, or archive it (it leaves the score from today; Restore brings it
 * back for all of history), and its shots, the spots it's photographed from. A weight or pace
 * change re-reads every past day, as any retune does.
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
    <section aria-labelledby={titleId} className="space-y-4 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
      <div>
        <h2 id={titleId} className="font-heading text-xl">
          Manage spaces
        </h2>
        <p className="text-xs text-muted-foreground">
          Each space's weight sets its share of the Home Score; its pace, how fast it gets messy.
        </p>
      </div>

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
        <ul className="divide-y" aria-label="Spaces in the Home Score">
          {live.map((space) => {
            const shots = shotsOf(space.id);
            return (
              <li key={space.id} className="space-y-2 py-2.5" data-testid="space-row">
                {editing === space.id ? (
                  <SpaceForm
                    initial={{
                      name: space.name,
                      weight: String(space.weight),
                      pace: String(space.pace),
                    }}
                    submitLabel="Save"
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
                ) : (
                  <div className="flex flex-wrap items-center justify-between gap-2">
                    <div className="min-w-0">
                      <p className="font-medium">{space.name}</p>
                      <p className="text-xs text-muted-foreground tabular-nums">
                        weight {space.weight} · pace {space.pace}
                        {space.share != null ? ` · ${space.share}% of the score` : ""}
                        {shots.length > 1 ? ` · ${shots.length} shots` : ""}
                      </p>
                    </div>
                    <div className="flex gap-2">
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        className="min-h-11 md:min-h-0"
                        aria-label={`Shots of ${space.name}`}
                        aria-expanded={shooting === space.id}
                        disabled={shots.length === 0}
                        onClick={() => setShooting(shooting === space.id ? null : space.id)}
                      >
                        Shots
                      </Button>
                      <Button
                        type="button"
                        variant="outline"
                        size="sm"
                        className="min-h-11 md:min-h-0"
                        aria-label={`Change ${space.name}`}
                        onClick={() => setEditing(space.id)}
                      >
                        Change
                      </Button>
                      <Button
                        type="button"
                        variant="ghost"
                        size="sm"
                        className="min-h-11 md:min-h-0"
                        aria-label={`Archive ${space.name}`}
                        disabled={busy === space.id}
                        onClick={() => void archive(space, false)}
                      >
                        Archive
                      </Button>
                    </div>
                  </div>
                )}
                {shooting === space.id && shots.length > 0 ? (
                  <ShotsEditor spaceId={space.id} space={space.name} shots={shots} />
                ) : null}
              </li>
            );
          })}
        </ul>
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
        <div className="space-y-2">
          <h3 className="text-sm font-medium text-muted-foreground">Archived</h3>
          <ul className="divide-y" aria-label="Archived spaces">
            {archived.map((space) => (
              <li key={space.id} className="flex flex-wrap items-center justify-between gap-2 py-2">
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
