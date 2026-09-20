import { useRef, useState } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { AttachmentPicker, oversizedFile } from "@/components/artifacts/attachment-picker";
import { CaptureKindTabs } from "@/components/create/capture-kind-tabs";
import { formatBytes, MAX_ARTIFACT_BYTES } from "@/lib/artifacts";
import { useCreateSubject, useWrites } from "@/lib/queries";
import { deriveTitle } from "@/lib/capture";
import { todayIso } from "@/lib/dates";
import type { CaptureKind } from "@/lib/production-ui-types";

export function QuickCaptureDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const [kind, setKind] = useState<CaptureKind>("Note");
  const [text, setText] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);
  const writes = useWrites();
  const create = useCreateSubject();
  const [pending, setPending] = useState(false);
  const box = useRef<HTMLTextAreaElement>(null);

  function reset() {
    setText("");
    setKind("Note");
    setFile(null);
    setError(null);
  }

  async function submit() {
    const body = text.trim();
    if (oversizedFile(file)) {
      setError(`That file is larger than ${formatBytes(MAX_ARTIFACT_BYTES)}.`);
      return;
    }
    if (!body && !file) {
      setError("Write something first, or attach a file.");
      return;
    }
    setError(null);
    setPending(true);
    try {
      if (file) {
        await writes.captureDocument({ file, description: body, type: kind });
      } else if (kind === "Note") {
        await writes.capture(body);
      } else if (kind === "Idea") {
        await create.mutateAsync({
          type: "Idea",
          title: deriveTitle(body),
          attrs: { description: body },
        });
      } else {
        await create.mutateAsync({
          type: "Problem",
          title: deriveTitle(body),
          attrs: { description: body, date_identified: todayIso() },
        });
      }
      reset();
      onOpenChange(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Capture failed. Retry without retyping.");
    } finally {
      setPending(false);
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) reset();
        onOpenChange(next);
      }}
    >
      <DialogContent className="sm:max-w-md" showCloseButton={false}>
        <DialogHeader>
          <DialogTitle>Capture</DialogTitle>
          <DialogDescription>
            Lands in Inbox. Enter submits. Escape discards. Ctrl+Enter for a new line.
          </DialogDescription>
        </DialogHeader>
        <CaptureKindTabs value={kind} onChange={setKind} disabled={pending} />
        <Textarea
          ref={box}
          autoFocus
          rows={5}
          value={text}
          placeholder={
            kind === "Note" ? "A note…" : kind === "Idea" ? "An idea…" : "A problem…"
          }
          aria-label={kind === "Note" ? "Note" : kind === "Idea" ? "Idea" : "Problem"}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
              e.preventDefault();
              const el = e.currentTarget;
              const start = el.selectionStart;
              const end = el.selectionEnd;
              const next = `${text.slice(0, start)}\n${text.slice(end)}`;
              setText(next);
              requestAnimationFrame(() => {
                el.selectionStart = el.selectionEnd = start + 1;
              });
              return;
            }
            if (e.key === "Enter" && !e.shiftKey) {
              e.preventDefault();
              void submit();
            }
          }}
        />
        <AttachmentPicker
          file={file}
          onChange={(next) => {
            setFile(next);
            if (oversizedFile(next)) {
              setError(`That file is larger than ${formatBytes(MAX_ARTIFACT_BYTES)}.`);
            } else if (error?.includes("larger than")) {
              setError(null);
            }
          }}
          disabled={pending}
        />
        {error ? <p className="text-xs text-destructive">{error}</p> : null}
        <div className="flex justify-end gap-2">
          <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
            Discard
          </Button>
          <Button type="button" onClick={() => void submit()} disabled={pending}>
            {pending ? "Capturing…" : "Capture"}
          </Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
