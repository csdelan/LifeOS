import { useEffect, useState } from "react";
import { MicIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { VoiceCaptureDialog } from "@/components/create/voice-capture-dialog";
import { formatDay } from "@/lib/dates";
import { focusWords } from "@/lib/features/focus/describe";
import { useDashboard } from "@/lib/queries";
import { obsidianUrl, reviewDay, reviewSpan } from "@/lib/reviews";
import type { ReviewBody, ReviewDoc } from "@/lib/production-ui-types";
import { useWriteAction } from "@/lib/write-action";

/** The picker's value for *No focus*; an Active Goal's is its id, and none picked yet is "". */
const NO_FOCUS = "none";

export function ReviewEditor({
  doc,
  onDirty,
  onSave,
  onChooseFocus,
  onComplete,
}: {
  doc: ReviewDoc;
  onDirty: (v: boolean) => void;
  onSave: (body: ReviewBody) => Promise<void>;
  /** Makes a Goal the Primary focus, or none, in this review (focus plan G1). */
  onChooseFocus: (goalId: string | null) => Promise<{ written: boolean }>;
  onComplete: () => Promise<void>;
}) {
  // The page keys this by the review, so another review starts from its own choice.
  const [keepFocus, setKeepFocus] = useState(doc.keepFocus);
  const [pick, setPick] = useState("");
  const saving = useWriteAction({ report: "toast" });
  const busy = saving.pending;
  const [dictating, setDictating] = useState(false);
  const { data: dash } = useDashboard();

  // Keep focus off and a Goal (or No focus) picked: Save chooses it, and the kernel turns this
  // review's Keep focus off. Off with nothing picked saves the switch alone, as it always has.
  const choosing = !keepFocus && pick !== "";
  const dirty = keepFocus !== doc.keepFocus || choosing;

  useEffect(() => {
    onDirty(dirty);
    return () => onDirty(false);
  }, [dirty, onDirty]);

  const body: ReviewBody = { keepFocus };
  const name = `${doc.kind === "daily" ? "Daily" : "Weekly"} review, ${reviewDay(doc.date)}`;

  function run(steps: () => Promise<void>, failure: string) {
    void saving.run(steps, { fallback: failure, onDone: () => onDirty(false) });
  }

  async function save() {
    if (!choosing) return onSave(body);
    const { written } = await onChooseFocus(pick === NO_FOCUS ? null : pick);
    // Written, the review's Keep focus is off now; the focus already held, nothing changed.
    setPick("");
    setKeepFocus(written ? false : doc.keepFocus);
  }

  const focus = dash?.primaryFocus;
  const words = focus ? focusWords(focus) : null;

  return (
    <div className="mt-8 space-y-8">
      <ReviewReflectionSection doc={doc} onDictate={() => setDictating(true)} />
      <VoiceCaptureDialog
        open={dictating}
        onOpenChange={setDictating}
        about={{
          id: doc.id,
          title: "Dictate reflection",
          description: `For the ${name}. The nightly sweep files it in your notes vault and links it here. Escape discards.`,
        }}
      />

      <section className="space-y-4">
        <h2 className="font-heading text-xl">Planning {reviewSpan(doc.planStart, doc.planEnd)}</h2>
        <div data-tone="violet" className="surface space-y-4 p-4">
          <div className="flex items-center justify-between gap-4">
            <div>
              <p className="type-scale-section text-attention">Primary focus</p>
              <p className="font-heading text-xl">{focus?.title ?? "—"}</p>
              {words?.lapsed ? <p className="text-xs text-attention">{words.lapsed}</p> : null}
              <p className="text-xs text-muted-foreground">
                {words?.source ?? "Keeping it is an explicit choice."}
              </p>
            </div>
            <label className="flex min-h-11 shrink-0 items-center gap-2 text-sm md:min-h-0">
              <Switch
                id="keep-focus"
                checked={keepFocus}
                onCheckedChange={setKeepFocus}
                aria-label="Keep primary focus"
              />
              Keep focus
            </label>
          </div>
          {keepFocus ? null : (
            <div className="space-y-1.5">
              <label htmlFor="new-focus" className="text-sm font-medium">
                New focus
              </label>
              <Select value={pick} onValueChange={setPick}>
                <SelectTrigger id="new-focus" className="w-full sm:w-96">
                  <SelectValue placeholder="Choose an Active Goal" />
                </SelectTrigger>
                <SelectContent>
                  {(dash?.activeGoals ?? []).map((g) => (
                    <SelectItem key={g.id} value={g.id}>
                      {g.title}
                      {g.targetDate ? ` · by ${formatDay(g.targetDate)}` : ""}
                    </SelectItem>
                  ))}
                  <SelectItem value={NO_FOCUS}>No focus</SelectItem>
                </SelectContent>
              </Select>
              <p className="text-xs text-muted-foreground">
                Save makes it the focus until a review changes it. With no focus, Focus suggests the
                Active Goal with the nearest target date.
              </p>
            </div>
          )}
        </div>
        {dash?.objectives.length ? (
          <ul className="space-y-1 text-sm">
            {dash.objectives.map((o) => (
              <li key={o.id}>· {o.title}</li>
            ))}
          </ul>
        ) : null}
      </section>

      <div className="flex gap-2 pb-10">
        <Button
          variant="outline"
          disabled={!dirty || busy}
          onClick={() => run(save, "The review didn't save.")}
        >
          Save
        </Button>
        {doc.status === "Completed" ? null : (
          <Button
            disabled={busy}
            onClick={() =>
              run(
                async () => {
                  if (dirty) await save();
                  await onComplete();
                },
                "The review didn't complete.",
              )
            }
          >
            {doc.missed ? "Complete it late" : "Complete review"}
          </Button>
        )}
      </div>
    </div>
  );
}

/**
 * The review's answers (reviews plan §4.1): Chris dictates them, an agent files them as a note in
 * his notes vault and links it here. Once linked, its summary and a link that opens it in Obsidian.
 */
function ReviewReflectionSection({ doc, onDictate }: { doc: ReviewDoc; onDictate: () => void }) {
  const reflection = doc.reflection;
  const plan =
    doc.kind === "daily"
      ? `The plan for ${reviewDay(doc.planStart)}`
      : `The plan for ${reviewSpan(doc.planStart, doc.planEnd)}: the next Projects and Tasks`;
  return (
    <section className="space-y-4" aria-labelledby="review-reflection">
      <h2 id="review-reflection" className="font-heading text-xl">
        Reflection
      </h2>
      {reflection ? (
        <div data-tone="violet" className="surface space-y-3 p-4">
          <p className="text-sm whitespace-pre-wrap">{reflection.summary}</p>
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <a
              href={obsidianUrl(reflection)}
              className="inline-flex min-h-11 items-center text-sm font-medium text-primary hover:underline md:min-h-0"
            >
              Open in Obsidian
            </a>
            <span className="text-xs break-all text-muted-foreground">{reflection.path}</span>
          </div>
        </div>
      ) : (
        <p className="rounded-lg bg-muted/60 p-3 text-sm text-muted-foreground">
          Dictate your reflection. The nightly sweep files it in your notes vault and links it here,
          with its summary.
        </p>
      )}
      <div className="text-sm">
        <p className="text-muted-foreground">Talk about:</p>
        <ul className="mt-1 list-disc space-y-0.5 pl-5">
          <li>What worked well?</li>
          <li>What do I want to do differently?</li>
          <li>{plan}</li>
        </ul>
      </div>
      <Button type="button" variant={reflection ? "outline" : "default"} onClick={onDictate}>
        <MicIcon />
        {reflection ? "Dictate more" : "Dictate reflection"}
      </Button>
    </section>
  );
}
