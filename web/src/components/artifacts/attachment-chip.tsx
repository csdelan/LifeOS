import { DownloadIcon, ExternalLinkIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  downloadArtifactUrl,
  formatBytes,
  formatDuration,
  openArtifactUrl,
} from "@/lib/artifacts";
import type { ArtifactRecord } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";
import { ContentTypeIcon } from "@/components/artifacts/content-type-icon";

export function AttachmentChip({
  artifact,
  compact,
  className,
}: {
  artifact: ArtifactRecord;
  /** List-row chip — no nested buttons (the row is already a button). */
  compact?: boolean;
  className?: string;
}) {
  const name = artifact.filename || "Attachment";
  const isAudio = artifact.contentType.startsWith("audio/");
  const meta = [
    isAudio && artifact.durationSeconds != null
      ? formatDuration(artifact.durationSeconds)
      : null,
    formatBytes(artifact.byteSize),
  ]
    .filter(Boolean)
    .join(" · ");

  if (compact) {
    return (
      <span
        className={cn(
          "mt-1 inline-flex max-w-full items-center gap-1 rounded-md bg-muted px-1.5 py-0.5 text-[0.6875rem] text-muted-foreground",
          className,
        )}
      >
        <ContentTypeIcon contentType={artifact.contentType} className="size-3 shrink-0" aria-hidden />
        <span className="truncate">{isAudio && artifact.durationSeconds != null ? meta : name}</span>
      </span>
    );
  }

  const url = artifact.bytesUrl;
  return (
    <div
      className={cn(
        "rounded-xl border bg-card p-3 ring-1 ring-foreground/10",
        className,
      )}
    >
      <div className="flex items-start gap-2">
        <ContentTypeIcon contentType={artifact.contentType} className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden />
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium">{name}</p>
          <p className="text-[0.6875rem] text-muted-foreground">{meta}</p>
        </div>
        <div className="flex shrink-0 gap-1">
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={!url}
            aria-label={isAudio ? `Open ${name}` : `Open ${name}`}
            onClick={() => openArtifactUrl(url)}
          >
            <ExternalLinkIcon />
            Open
          </Button>
          <Button
            type="button"
            variant="outline"
            size="sm"
            disabled={!url}
            aria-label={`Download ${name}`}
            onClick={() => downloadArtifactUrl(url, artifact.filename)}
          >
            <DownloadIcon />
            Download
          </Button>
        </div>
      </div>
      {isAudio && url ? (
        <audio
          className="mt-2 w-full"
          controls
          src={url}
          preload="metadata"
          aria-label={`Play ${name}`}
        />
      ) : null}
    </div>
  );
}
