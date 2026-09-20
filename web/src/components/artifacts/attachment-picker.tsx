import { useRef } from "react";
import { PaperclipIcon, XIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import { formatBytes, iconForContentType, MAX_ARTIFACT_BYTES } from "@/lib/artifacts";

export function AttachmentPicker({
  file,
  onChange,
  disabled,
  error,
}: {
  file: File | null;
  onChange: (file: File | null) => void;
  disabled?: boolean;
  error?: string | null;
}) {
  const input = useRef<HTMLInputElement>(null);
  const Icon = iconForContentType(file?.type ?? "");

  return (
    <div className="space-y-1.5">
      <input
        ref={input}
        type="file"
        className="sr-only"
        disabled={disabled}
        onChange={(e) => {
          const next = e.target.files?.[0] ?? null;
          e.target.value = "";
          onChange(next);
        }}
      />
      {file ? (
        <div className="flex items-center gap-2 rounded-lg border bg-muted/40 px-2 py-1.5">
          <Icon className="size-4 shrink-0 text-muted-foreground" aria-hidden />
          <div className="min-w-0 flex-1">
            <p className="truncate text-sm font-medium">{file.name}</p>
            <p className="text-[0.6875rem] text-muted-foreground">{formatBytes(file.size)}</p>
          </div>
          <Button
            type="button"
            variant="ghost"
            size="icon-sm"
            disabled={disabled}
            aria-label={`Remove ${file.name}`}
            onClick={() => onChange(null)}
          >
            <XIcon />
          </Button>
        </div>
      ) : (
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled}
          onClick={() => input.current?.click()}
        >
          <PaperclipIcon />
          Attach file
        </Button>
      )}
      {error ? <p className="text-xs text-destructive">{error}</p> : null}
    </div>
  );
}

export function oversizedFile(file: File | null): boolean {
  return !!file && file.size > MAX_ARTIFACT_BYTES;
}
