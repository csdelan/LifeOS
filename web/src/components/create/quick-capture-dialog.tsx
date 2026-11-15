import { useRef, useState } from "react";
import {
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { FormDialog } from "@/components/primitives/form-dialog";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { AttachmentPicker } from "@/components/artifacts/attachment-picker";
import { CaptureKindTabs } from "@/components/create/capture-kind-tabs";
import { formatBytes, MAX_ARTIFACT_BYTES, oversizedFile } from "@/lib/artifacts";
import { useWrites } from "@/lib/queries";
import type { CaptureKind } from "@/lib/production-ui-types";
import { useWriteAction } from "@/lib/write-action";

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
  const writes = useWrites();
  // One capture at a time (fifth review R1): Enter held or pressed twice while the first is on
  // its way would otherwise capture twice.
  const { run, pending, error, setError } = useWriteAction({ fallback: "Capture failed. Retry without retyping." });
  // The id a typed capture is sent with, kept while the same text is retried, so a retry after
  // a lost answer finishes the first capture rather than making a second.
  const request = useRef<{ key: string; id: string } | null>(null);
  const box = useRef<HTMLTextAreaElement>(null);

  function reset() {
    setText("");
    setKind("Note");
    setFile(null);
    setError(null);
    request.current = null;
  }

  function requestId(key: string): string {
    if (request.current?.key !== key) request.current = { key, id: crypto.randomUUID() };
    return request.current.id;
  }

  async function submit() {
    if (pending) return;
    const body = text.trim();
    if (oversizedFile(file)) {
      setError(`That file is larger than ${formatBytes(MAX_ARTIFACT_BYTES)}.`);
      return;
    }
    if (!body && !file) {
      setError("Write something first, or attach a file.");
      return;
    }
    await run<unknown>(
      () =>
        file
          ? writes.captureDocument({ file, description: body, type: kind })
          : writes.capture(body, kind, requestId(`${kind}\n${body}`)),
      () => {
        reset();
        onOpenChange(false);
      },
    );
  }

  return (
    <FormDialog
      open={open}
      onOpenChange={(next) => {
        if (!next) reset();
        onOpenChange(next);
      }}
      className="sm:max-w-md"
      showCloseButton={false}
      escapeDiscards
    >
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
      {error ? (
        <p role="alert" className="text-xs text-destructive">
          {error}
        </p>
      ) : null}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" onClick={() => onOpenChange(false)}>
          Discard
        </Button>
        <Button type="button" onClick={() => void submit()} disabled={pending}>
          {pending ? "Capturing…" : "Capture"}
        </Button>
      </div>
    </FormDialog>
  );
}
