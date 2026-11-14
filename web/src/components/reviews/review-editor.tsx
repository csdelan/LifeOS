import { useEffect, useState } from "react";
import { MicIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { VoiceCaptureDialog } from "@/components/create/voice-capture-dialog";
import { useDashboard } from "@/lib/queries";
import { obsidianUrl, reviewDay, reviewSpan } from "@/lib/reviews";
import type { ReviewBody, ReviewDoc } from "@/lib/production-ui-types";
import { useWriteAction } from "@/lib/write-action";

export function ReviewEditor({
  doc,
  onDirty,
  onSave,
  onComplete,
}: {
  doc: ReviewDoc;
  onDirty: (v: boolean) => void;
  onSave: (body: ReviewBody) => Promise<void>;
  onComplete: () => Promise<void>;
}) {
  // The page keys this by the review, so another review starts from its own choice.
  const [keepFocus, setKeepFocus] = useState(doc.keepFocus);
  const saving = useWriteAction({ report: "toast" });
  const busy = saving.pending;
  const [dictating, setDictating] = useState(false);
  const { data: dash } = useDashboard();

  const dirty = keepFocus !== doc.keepFocus;

  useEffect(() => {
    onDirty(dirty);
    return () => onDirty(false);
  }, [dirty, onDirty]);

  const body: ReviewBody = { keepFocus };
  const name = `${doc.kind === "daily" ? "Daily" : "Weekly"} review, ${reviewDay(doc.date)}`;

  function run(steps: () => Promise<void>, failure: string) {
    void saving.run(steps, { fallback: failure, onDone: () => onDirty(false) });
  }

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
        <div className="flex items-center justify-between rounded-xl bg-card p-4 ring-1 ring-foreground/10">
          <div>
            <p className="type-scale-section text-attention">Primary focus</p>
            <p className="font-heading text-xl">{dash?.primaryFocus.title ?? "—"}</p>
            <p className="text-xs text-muted-foreground">Keeping it is an explicit choice.</p>
          </div>
          <label className="flex min-h-11 items-center gap-2 text-sm md:min-h-0">
            <Switch
              id="keep-focus"
              checked={keepFocus}
              onCheckedChange={setKeepFocus}
              aria-label="Keep primary focus"
            />
            Keep focus
          </label>
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
          onClick={() => run(() => onSave(body), "The review didn't save.")}
        >
          Save
        </Button>
        {doc.status === "Completed" ? null : (
          <Button
            disabled={busy}
            onClick={() =>
              run(
                async () => {
                  if (dirty) await onSave(body);
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
        <div className="space-y-3 rounded-xl bg-card p-4 ring-1 ring-foreground/10">
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
