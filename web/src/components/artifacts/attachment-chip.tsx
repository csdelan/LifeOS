import { DownloadIcon, ExternalLinkIcon, RotateCwIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  downloadArtifactUrl,
  formatBytes,
  formatDuration,
  openArtifactUrl,
} from "@/lib/artifacts";
import type { ArtifactRecord } from "@/lib/production-ui-types";
import { useArtifactBytesUrl } from "@/lib/queries";
import { cn } from "@/lib/utils";
import { ContentTypeIcon } from "@/components/artifacts/content-type-icon";
import { RecordedAudio } from "@/components/artifacts/recorded-audio";

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
  if (compact) {
    const meta = attachmentMeta(artifact);
    return (
      <span
        className={cn(
          "mt-1 inline-flex max-w-full items-center gap-1 rounded-md bg-muted px-1.5 py-0.5 text-[0.6875rem] text-muted-foreground",
          className,
        )}
      >
        <ContentTypeIcon contentType={artifact.contentType} className="size-3 shrink-0" aria-hidden />
        <span className="truncate">{meta.duration ? meta.text : meta.name}</span>
      </span>
    );
  }
  return <AttachmentCard artifact={artifact} className={className} />;
}

/**
 * The file with its Open and Download buttons, and a player for audio. Its bytes are fetched
 * with the session's token through `artifactBytesUrl` and kept as an object URL, as a meal's
 * photo is: the record never carries a URL for them (CAP-2 / CAP-4).
 */
function AttachmentCard({ artifact, className }: { artifact: ArtifactRecord; className?: string }) {
  const { name, text, isAudio } = attachmentMeta(artifact);
  const bytes = useArtifactBytesUrl(artifact.hasBytes ? artifact.id : null);
  const url = bytes.data ?? null;
  const gone = !artifact.hasBytes || bytes.data === null;
  return (
    <div className={cn("rounded-lg border bg-card p-3", className)}>
      <div className="flex items-start gap-2">
        <ContentTypeIcon contentType={artifact.contentType} className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden />
        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium">{name}</p>
          <p className="text-[0.6875rem] text-muted-foreground">
            {text}
            {bytes.isError ? " · Couldn't load it" : gone ? " · Its bytes aren't stored" : null}
          </p>
        </div>
        <div className="flex shrink-0 gap-1">
          {bytes.isError ? (
            <Button
              type="button"
              variant="outline"
              size="sm"
              aria-label={`Retry loading ${name}`}
              onClick={() => void bytes.refetch()}
            >
              <RotateCwIcon />
              Retry
            </Button>
          ) : (
            <>
              <Button
                type="button"
                variant="outline"
                size="sm"
                disabled={!url}
                aria-label={`Open ${name}`}
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
            </>
          )}
        </div>
      </div>
      {isAudio && url ? <RecordedAudio className="mt-2 w-full" src={url} label={`Play ${name}`} /> : null}
    </div>
  );
}

function attachmentMeta(artifact: ArtifactRecord) {
  const isAudio = artifact.contentType.startsWith("audio/");
  const duration =
    isAudio && artifact.durationSeconds != null ? formatDuration(artifact.durationSeconds) : null;
  return {
    name: artifact.filename || "Attachment",
    isAudio,
    duration,
    text: [duration, formatBytes(artifact.byteSize)].filter(Boolean).join(" · "),
  };
}
