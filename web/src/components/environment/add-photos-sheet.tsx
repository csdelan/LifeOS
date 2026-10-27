import { useEffect, useId, useRef, useState } from "react";
import { CameraIcon, ImageIcon, XIcon } from "lucide-react";
import { toast } from "sonner";
import { SELECT_CLASS } from "@/components/environment/photo-compare-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle } from "@/components/ui/sheet";
import { formatDay } from "@/lib/dates";
import { addedMessage, SHOT_NAME_MAX, SHOTS_MAX } from "@/lib/environment";
import { useIsDesktop } from "@/lib/media-query";
import { preparePhoto } from "@/lib/photo";
import type { HomeSpaceRow } from "@/lib/production-ui-types";
import { useWrites } from "@/lib/queries";
import { cn } from "@/lib/utils";

/** One picked photo, shrunk and waiting to be added. */
interface Pending {
  key: number;
  blob: Blob;
  url: string;
  name: string;
  /** The day its EXIF says it was taken, if it says. */
  takenOn: string | null;
  day: string;
  /** A shot's id, or NEW_SHOT for one named in `newName`. */
  shot: string;
  newName: string;
  error: string | null;
}

/** The shot picker's "New shot…": the photo starts a shot of its own. */
const NEW_SHOT = "__new__";

function sameName(a: string, b: string): boolean {
  return a.trim().localeCompare(b.trim(), undefined, { sensitivity: "base" }) === 0;
}

/** "Sink", "Sink and Table", "Sink, Table and Window". */
function namesText(names: readonly string[]): string {
  return names.length <= 1 ? (names[0] ?? "") : `${names.slice(0, -1).join(", ")} and ${names.at(-1)}`;
}

/**
 * Add photos of a space (HOME-4, environment plan §8.1 answer 2): several at once, for photos
 * taken earlier on the phone. Each is dated from when it was taken when the file says
 * (its EXIF, read before it's shrunk), otherwise the day picked here, and goes to the shot
 * chosen, or a new one named here for a photo from another spot (photos given the same new
 * name share it); either can be changed before adding. A phone can take one with its camera
 * too. New shots are added first, then the photos one by one, so one that fails stays here
 * with why, and the rest are in.
 */
export function AddPhotosSheet({
  open,
  onOpenChange,
  space,
  shotId,
  today,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  space: HomeSpaceRow;
  /** The shot the photos go to unless changed. */
  shotId: string;
  today: string;
}) {
  const desktop = useIsDesktop();
  return (
    <Sheet open={open} onOpenChange={onOpenChange}>
      <SheetContent
        side={desktop ? "right" : "bottom"}
        className={cn("gap-0", desktop ? "w-full sm:max-w-md" : "max-h-[92dvh] rounded-t-2xl")}
      >
        {open ? (
          <AddPhotosForm
            key={`${space.id}-${shotId}`}
            space={space}
            shotId={shotId}
            today={today}
            camera={!desktop}
            onDone={() => onOpenChange(false)}
          />
        ) : null}
      </SheetContent>
    </Sheet>
  );
}

function AddPhotosForm({
  space,
  shotId,
  today,
  camera,
  onDone,
}: {
  space: HomeSpaceRow;
  shotId: string;
  today: string;
  camera: boolean;
  onDone: () => void;
}) {
  const ids = useId();
  const writes = useWrites();
  const [rows, setRows] = useState<Pending[]>([]);
  const [fallback, setFallback] = useState(today);
  const [preparing, setPreparing] = useState(0);
  const [progress, setProgress] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const nextKey = useRef(0);
  const urls = useRef(new Set<string>());
  const cameraInput = useRef<HTMLInputElement>(null);
  const libraryInput = useRef<HTMLInputElement>(null);
  useEffect(() => {
    const held = urls.current;
    return () => held.forEach((u) => URL.revokeObjectURL(u));
  }, []);

  const busy = progress != null;
  const badDay = rows.some((r) => !r.day || r.day > today);
  /** The shot a new name means when the space already has it. */
  const existing = (name: string) => space.shots.find((s) => sameName(s.name, name));
  // The new shots Add makes: each name once, in any case, leaving out ones the space has.
  const newNames = rows
    .filter((r) => r.shot === NEW_SHOT && r.newName.trim() && !existing(r.newName))
    .map((r) => r.newName.trim())
    .filter((name, i, all) => all.findIndex((n) => sameName(n, name)) === i);
  const unnamed = rows.some((r) => r.shot === NEW_SHOT && !r.newName.trim());
  const tooMany = space.shots.length + newNames.length > SHOTS_MAX;

  async function pick(files: FileList | null) {
    if (!files || files.length === 0) return;
    setError(null);
    const list = [...files];
    setPreparing((n) => n + list.length);
    for (const file of list) {
      try {
        const prepared = await preparePhoto(file);
        const url = URL.createObjectURL(prepared.blob);
        urls.current.add(url);
        const taken = prepared.takenOn && prepared.takenOn <= today ? prepared.takenOn : null;
        setRows((all) => [
          ...all,
          {
            key: nextKey.current++,
            ...prepared,
            url,
            takenOn: taken,
            day: taken ?? fallback,
            shot: shotId,
            newName: "",
            error: null,
          },
        ]);
      } catch (e) {
        setError(`${file.name}: ${e instanceof Error ? e.message : "couldn't be read."}`);
      } finally {
        setPreparing((n) => n - 1);
      }
    }
  }

  function change(key: number, next: Partial<Pending>) {
    setRows((all) => all.map((r) => (r.key === key ? { ...r, ...next, error: null } : r)));
  }

  function drop(key: number) {
    setRows((all) => {
      const gone = all.find((r) => r.key === key);
      if (gone) {
        URL.revokeObjectURL(gone.url);
        urls.current.delete(gone.url);
      }
      return all.filter((r) => r.key !== key);
    });
  }

  /** The day for photos that don't say: it moves every such photo still on the old one. */
  function changeFallback(day: string) {
    setRows((all) => all.map((r) => (r.takenOn == null && r.day === fallback ? { ...r, day } : r)));
    setFallback(day);
  }

  async function submit() {
    if (rows.length === 0 || busy || badDay || unnamed || tooMany) return;
    setError(null);
    // New shots first, so every photo has its shot; a name the space has already goes there.
    const made: { name: string; id: string }[] = [];
    const shotFor = (row: Pending) =>
      row.shot !== NEW_SHOT
        ? row.shot
        : (existing(row.newName)?.id ?? made.find((m) => sameName(m.name, row.newName))?.id ?? NEW_SHOT);
    for (const name of newNames) {
      setProgress(`Adding the shot ${name}…`);
      try {
        const result = await writes.addShot(space.id, name, { quiet: true });
        const shot = result.shots.find((s) => sameName(s.name, name));
        if (!shot) throw new Error("it didn't come back.");
        made.push({ name: shot.name, id: shot.id });
      } catch (e) {
        // Rows whose shot was made point at it now, so trying again doesn't make it twice.
        setRows((all) =>
          all.map((r) => ({ ...r, shot: shotFor(r), newName: shotFor(r) === NEW_SHOT ? r.newName : "" })),
        );
        setProgress(null);
        setError(
          `The shot ${name} wasn't added, so no photos were: ${e instanceof Error ? e.message : "it didn't save."}`,
        );
        if (made.length > 0)
          toast.success(`Added ${made.length === 1 ? "the shot" : "the shots"} ${namesText(made.map((m) => m.name))}`);
        return;
      }
    }
    const ready = rows.map((r) => ({ ...r, shot: shotFor(r), newName: "" }));
    let added = 0;
    const failed: Pending[] = [];
    for (const [i, row] of ready.entries()) {
      setProgress(`Adding ${i + 1} of ${ready.length}…`);
      try {
        await writes.addSpacePhoto(space.id, row.blob, { day: row.day, shot: row.shot, filename: row.name });
        added += 1;
        URL.revokeObjectURL(row.url);
        urls.current.delete(row.url);
      } catch (e) {
        failed.push({ ...row, error: e instanceof Error ? e.message : "It didn't upload." });
      }
    }
    setProgress(null);
    const shots =
      made.length === 0
        ? ""
        : ` and ${made.length === 1 ? "the shot" : "the shots"} ${namesText(made.map((m) => m.name))}`;
    if (added > 0) toast.success(`${addedMessage(added, space.name)}${shots}`);
    else if (made.length > 0) toast.success(`Added ${shots.slice(" and ".length)}`);
    if (failed.length === 0) {
      onDone();
      return;
    }
    setRows(failed);
    setError(
      failed.length === 1
        ? "One photo didn't save: see why below."
        : `${failed.length} photos didn't save: see why below.`,
    );
  }

  return (
    <form
      className="flex min-h-0 flex-1 flex-col"
      onSubmit={(e) => {
        e.preventDefault();
        void submit();
      }}
    >
      <SheetHeader className="pb-2">
        <SheetTitle>Add photos of {space.name}</SheetTitle>
        <SheetDescription>
          Each photo is dated from when it was taken, if the file says, or the day below, and goes to the shot you pick:
          New shot… for another spot. They're shrunk to 1,200 px, and the location is left out.
        </SheetDescription>
      </SheetHeader>

      <div className="min-h-0 flex-1 space-y-4 overflow-y-auto px-4 pb-4">
        <div className="flex flex-wrap gap-2">
          {camera ? (
            <Button
              type="button"
              variant="outline"
              className="min-h-11 md:min-h-0"
              disabled={busy}
              onClick={() => cameraInput.current?.click()}
            >
              <CameraIcon />
              Take photo
            </Button>
          ) : null}
          <Button
            type="button"
            variant="outline"
            className="min-h-11 md:min-h-0"
            disabled={busy}
            onClick={() => libraryInput.current?.click()}
          >
            <ImageIcon />
            Choose photos
          </Button>
        </div>
        {/* Hidden: the buttons above open them. capture asks a phone for its rear camera. */}
        <input
          ref={cameraInput}
          type="file"
          accept="image/*"
          capture="environment"
          hidden
          onChange={(e) => {
            void pick(e.target.files);
            e.target.value = "";
          }}
        />
        <input
          ref={libraryInput}
          type="file"
          accept="image/*"
          multiple
          hidden
          data-testid="add-photos-input"
          onChange={(e) => {
            void pick(e.target.files);
            e.target.value = "";
          }}
        />

        <div className="flex items-center gap-3">
          <Label htmlFor={`${ids}-fallback`} className="shrink-0">
            Day for photos that don't say
          </Label>
          <Input
            id={`${ids}-fallback`}
            type="date"
            max={today}
            value={fallback}
            disabled={busy}
            onChange={(e) => changeFallback(e.target.value)}
            className="w-auto"
          />
        </div>

        <p className="text-xs text-muted-foreground" aria-live="polite">
          {preparing > 0
            ? `Preparing ${preparing === 1 ? "a photo" : `${preparing} photos`}…`
            : rows.length === 0
              ? "No photos picked yet."
              : rows.length === 1
                ? "1 photo ready to add."
                : `${rows.length} photos ready to add.`}
        </p>

        {rows.length > 0 ? (
          <ul className="space-y-3" aria-label="Photos to add">
            {rows.map((row, i) => (
              <li
                key={row.key}
                className="flex gap-3 rounded-xl p-2 ring-1 ring-foreground/10"
                data-testid="pending-photo"
              >
                <img src={row.url} alt="" className="size-20 shrink-0 rounded-lg object-cover" />
                <div className="min-w-0 flex-1 space-y-2">
                  <div className="flex items-start justify-between gap-2">
                    <p className="truncate text-sm font-medium">{row.name}</p>
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-sm"
                      disabled={busy}
                      aria-label={`Leave out photo ${i + 1}, ${row.name}`}
                      onClick={() => drop(row.key)}
                    >
                      <XIcon />
                    </Button>
                  </div>
                  <div className="flex flex-wrap items-end gap-2">
                    <div className="space-y-1">
                      <Label htmlFor={`${ids}-${row.key}-day`} className="text-xs">
                        Day
                      </Label>
                      <Input
                        id={`${ids}-${row.key}-day`}
                        type="date"
                        max={today}
                        value={row.day}
                        disabled={busy}
                        aria-describedby={`${ids}-${row.key}-from`}
                        onChange={(e) => change(row.key, { day: e.target.value })}
                        className="w-auto"
                      />
                    </div>
                    <div className="min-w-32 flex-1 space-y-1">
                      <Label htmlFor={`${ids}-${row.key}-shot`} className="text-xs">
                        Shot
                      </Label>
                      <select
                        id={`${ids}-${row.key}-shot`}
                        value={row.shot}
                        disabled={busy}
                        onChange={(e) => change(row.key, { shot: e.target.value })}
                        className={SELECT_CLASS}
                      >
                        {space.shots.map((s) => (
                          <option key={s.id} value={s.id}>
                            {s.name}
                          </option>
                        ))}
                        <option value={NEW_SHOT}>New shot…</option>
                      </select>
                    </div>
                  </div>
                  {row.shot === NEW_SHOT ? (
                    <div className="space-y-1">
                      <Label htmlFor={`${ids}-${row.key}-new`} className="text-xs">
                        New shot's name
                      </Label>
                      <Input
                        id={`${ids}-${row.key}-new`}
                        value={row.newName}
                        maxLength={SHOT_NAME_MAX}
                        autoComplete="off"
                        placeholder="Sink"
                        disabled={busy}
                        aria-describedby={`${ids}-${row.key}-new-help`}
                        onChange={(e) => change(row.key, { newName: e.target.value })}
                      />
                      <p id={`${ids}-${row.key}-new-help`} className="text-xs text-muted-foreground">
                        {row.newName.trim() && existing(row.newName)
                          ? `${space.name} has that shot already: the photo goes there.`
                          : "Photos from the same spot get the same name."}
                      </p>
                    </div>
                  ) : null}
                  <p id={`${ids}-${row.key}-from`} className="text-xs text-muted-foreground">
                    {row.takenOn
                      ? row.day === row.takenOn
                        ? `Taken ${formatDay(row.takenOn)}, from the photo.`
                        : `The photo says ${formatDay(row.takenOn)}.`
                      : "The photo doesn't say when it was taken."}
                    {row.day > today ? " A photo is for today or earlier." : ""}
                  </p>
                  {row.error ? (
                    <p role="alert" className="text-xs text-destructive">
                      {row.error}
                    </p>
                  ) : null}
                </div>
              </li>
            ))}
          </ul>
        ) : null}

        {unnamed ? <p className="text-xs text-muted-foreground">Name each new shot to add the photos.</p> : null}
        {tooMany ? (
          <p className="text-xs text-muted-foreground">
            {space.name} can have {SHOTS_MAX} shots; these photos would make {space.shots.length + newNames.length}.
            Give some the same name, or pick a shot it has.
          </p>
        ) : null}

        {error ? (
          <p role="alert" className="text-sm text-destructive">
            {error}
          </p>
        ) : null}
      </div>

      <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 border-t px-4 py-3">
        <p className="text-xs text-muted-foreground" aria-live="polite">
          {progress ?? ""}
        </p>
        <div className="flex gap-2">
          <Button type="button" variant="outline" className="min-h-11 md:min-h-0" onClick={onDone}>
            Cancel
          </Button>
          <Button
            type="submit"
            className="min-h-11 md:min-h-0"
            disabled={rows.length === 0 || busy || preparing > 0 || badDay || unnamed || tooMany}
          >
            {busy ? "Adding…" : rows.length > 1 ? `Add ${rows.length} photos` : "Add photo"}
          </Button>
        </div>
      </div>
    </form>
  );
}
