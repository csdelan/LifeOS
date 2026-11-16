import { useEffect, useRef } from "react";

/**
 * A player for a recording (CAP-2) that shows its length. Chrome's MediaRecorder writes WebM with
 * no duration in it, so its player said 0:00 and couldn't seek until it had played to the end: a
 * phone's reflection did, in review and in the Inbox. Asked to seek far past the end, it reads the
 * file through and learns the length, and the player then goes back to the start.
 */
export function RecordedAudio({
  src,
  label,
  className,
}: {
  src: string;
  label: string;
  className?: string;
}) {
  const ref = useRef<HTMLAudioElement>(null);

  useEffect(() => {
    const audio = ref.current;
    if (!audio) return;
    let probing = false;
    const onMetadata = () => {
      if (audio.duration !== Infinity) return;
      probing = true;
      audio.currentTime = Number.MAX_SAFE_INTEGER;
    };
    const onDuration = () => {
      if (!probing || !Number.isFinite(audio.duration)) return;
      probing = false;
      audio.currentTime = 0;
    };
    audio.addEventListener("loadedmetadata", onMetadata);
    audio.addEventListener("durationchange", onDuration);
    if (audio.readyState >= HTMLMediaElement.HAVE_METADATA) onMetadata();
    return () => {
      audio.removeEventListener("loadedmetadata", onMetadata);
      audio.removeEventListener("durationchange", onDuration);
    };
  }, [src]);

  return <audio ref={ref} className={className} controls src={src} preload="metadata" aria-label={label} />;
}
