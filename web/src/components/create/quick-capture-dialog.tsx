import { useRef } from "react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { useCreateSubject, useWrites } from "@/lib/queries";
import { deriveTitle } from "@/lib/capture";
import { todayIso } from "@/lib/dates";
import { cn } from "@/lib/utils";
import { useState } from "react";

const KINDS = ["Note", "Idea", "Problem"] as const;
type CaptureKind = (typeof KINDS)[number];

export function QuickCaptureDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const [kind, setKind] = useState<CaptureKind>("Note");
  const [text, setText] = useState("");
  const [error, setError] = useState<string | null>(null);
  const writes = useWrites();
  const create = useCreateSubject();
  const pending = create.isPending;
  const box = useRef<HTMLTextAreaElement>(null);

  async function submit() {
    const body = text.trim();
    if (!body) {
      setError("Write something first.");
      return;
    }
    setError(null);
    try {
      if (kind === "Note") {
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
      setText("");
      setKind("Note");
      onOpenChange(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Capture failed. Retry without retyping.");
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) {
          setText("");
          setKind("Note");
          setError(null);
        }
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
        <div className="flex gap-1" role="tablist" aria-label="Capture type">
          {KINDS.map((k) => (
            <button
              key={k}
              type="button"
              role="tab"
              aria-selected={kind === k}
              tabIndex={0}
              onClick={() => setKind(k)}
              className={cn(
                "rounded-md px-2.5 py-1 text-sm",
                kind === k ? "bg-primary/12 font-medium text-primary" : "text-muted-foreground hover:bg-muted",
              )}
            >
              {k}
            </button>
          ))}
        </div>
        <Textarea
          ref={box}
          autoFocus
          rows={5}
          value={text}
          placeholder={
            kind === "Note"
              ? "A note…"
              : kind === "Idea"
                ? "An idea…"
                : "A problem…"
          }
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
        {error ? <p className="text-xs text-destructive">{error}</p> : null}
        <div className="flex justify-end gap-2">
          <Button
            type="button"
            variant="outline"
            onClick={() => onOpenChange(false)}
          >
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
