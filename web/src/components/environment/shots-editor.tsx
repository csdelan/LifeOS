import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { SHOT_NAME_MAX, SHOTS_MAX } from "@/lib/environment";
import type { SpaceShotRow } from "@/lib/production-ui-types";
import { useWrites } from "@/lib/queries";

function photosText(n: number): string {
  return n === 1 ? "1 photo" : `${n} photos`;
}

/**
 * A space's shots (HOME-4, environment plan §8.1 answer 1): the spots it's photographed from.
 * Add one (up to six), rename one, or remove one; a space keeps at least one. A removed shot's
 * photos stay in the record but leave the page, so removing one with photos asks first.
 */
export function ShotsEditor({ spaceId, space, shots }: { spaceId: string; space: string; shots: SpaceShotRow[] }) {
  const ids = useId();
  const writes = useWrites();
  const [name, setName] = useState("");
  const [renaming, setRenaming] = useState<string | null>(null);
  const [rename, setRename] = useState("");
  const [removing, setRemoving] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const full = shots.length >= SHOTS_MAX;

  async function run(write: () => Promise<unknown>, after: () => void) {
    if (busy) return;
    setBusy(true);
    setError(null);
    try {
      await write();
      after();
    } catch (e) {
      setError(e instanceof Error ? e.message : "That didn't save. Nothing was written.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-3 rounded-lg bg-muted/60 p-3" data-testid="shots-editor">
      <div>
        <h4 className="text-sm font-medium">Shots of {space}</h4>
        <p className="text-xs text-muted-foreground">
          The spots you photograph {space} from, each with its own Then and Now.
        </p>
      </div>

      <ul className="divide-y divide-foreground/10" aria-label={`Shots of ${space}`}>
        {shots.map((shot) => (
          <li key={shot.id} className="space-y-2 py-2">
            {renaming === shot.id ? (
              <form
                className="flex flex-wrap items-end gap-2"
                onSubmit={(e) => {
                  e.preventDefault();
                  void run(
                    () => writes.renameShot(spaceId, shot.id, rename),
                    () => setRenaming(null),
                  );
                }}
              >
                <div className="min-w-40 flex-1 space-y-1">
                  <Label htmlFor={`${ids}-${shot.id}-name`} className="text-xs">
                    New name for {shot.name}
                  </Label>
                  <Input
                    id={`${ids}-${shot.id}-name`}
                    value={rename}
                    maxLength={SHOT_NAME_MAX}
                    autoComplete="off"
                    disabled={busy}
                    onChange={(e) => {
                      setError(null);
                      setRename(e.target.value);
                    }}
                  />
                </div>
                <Button type="submit" size="sm" className="min-h-11 md:min-h-0" disabled={busy || !rename.trim()}>
                  Save
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  className="min-h-11 md:min-h-0"
                  onClick={() => setRenaming(null)}
                >
                  Cancel
                </Button>
              </form>
            ) : (
              <div className="flex flex-wrap items-center justify-between gap-2">
                <p className="min-w-0 text-sm">
                  {shot.name}{" "}
                  <span className="text-xs text-muted-foreground tabular-nums">· {photosText(shot.photos)}</span>
                </p>
                <div className="flex gap-2">
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    className="min-h-11 md:min-h-0"
                    aria-label={`Rename the shot ${shot.name}`}
                    disabled={busy}
                    onClick={() => {
                      setError(null);
                      setRemoving(null);
                      setRename(shot.name);
                      setRenaming(shot.id);
                    }}
                  >
                    Rename
                  </Button>
                  {shots.length > 1 ? (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      className="min-h-11 md:min-h-0"
                      aria-label={`Remove the shot ${shot.name}`}
                      disabled={busy}
                      onClick={() => {
                        setError(null);
                        setRenaming(null);
                        if (shot.photos === 0) {
                          void run(
                            () => writes.removeShot(spaceId, shot.id),
                            () => {},
                          );
                        } else {
                          setRemoving(shot.id);
                        }
                      }}
                    >
                      Remove
                    </Button>
                  ) : null}
                </div>
              </div>
            )}
            {removing === shot.id ? (
              <div role="group" aria-label={`Remove ${shot.name}?`} className="space-y-2 rounded-md bg-background p-2">
                <p className="text-xs">
                  Remove {shot.name}? Its {photosText(shot.photos)} leave the page but stay in the record.
                </p>
                <div className="flex gap-2">
                  <Button
                    type="button"
                    variant="destructive"
                    size="sm"
                    className="min-h-11 md:min-h-0 dark:bg-destructive/10"
                    disabled={busy}
                    onClick={() =>
                      void run(
                        () => writes.removeShot(spaceId, shot.id),
                        () => setRemoving(null),
                      )
                    }
                  >
                    Remove
                  </Button>
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    className="min-h-11 md:min-h-0"
                    onClick={() => setRemoving(null)}
                  >
                    Keep it
                  </Button>
                </div>
              </div>
            ) : null}
          </li>
        ))}
      </ul>

      <form
        className="flex flex-wrap items-end gap-2"
        onSubmit={(e) => {
          e.preventDefault();
          void run(
            () => writes.addShot(spaceId, name),
            () => setName(""),
          );
        }}
      >
        <div className="min-w-40 flex-1 space-y-1">
          <Label htmlFor={`${ids}-add`} className="text-xs">
            Add a shot
          </Label>
          <Input
            id={`${ids}-add`}
            value={name}
            maxLength={SHOT_NAME_MAX}
            autoComplete="off"
            placeholder="Desk"
            disabled={busy || full}
            aria-describedby={full ? `${ids}-full` : undefined}
            onChange={(e) => {
              setError(null);
              setName(e.target.value);
            }}
          />
        </div>
        <Button type="submit" size="sm" className="min-h-11 md:min-h-0" disabled={busy || full || !name.trim()}>
          Add shot
        </Button>
      </form>
      {full ? (
        <p id={`${ids}-full`} className="text-xs text-muted-foreground">
          {SHOTS_MAX} shots is the most a space can have.
        </p>
      ) : null}

      {error ? (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      ) : null}
    </div>
  );
}
