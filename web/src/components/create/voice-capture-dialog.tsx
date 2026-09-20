import { useEffect, useRef, useState } from "react";
import { MicIcon, SquareIcon } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { CaptureKindTabs } from "@/components/create/capture-kind-tabs";
import { revokeIfBlobUrl } from "@/lib/artifacts";
import { useWrites } from "@/lib/queries";
import type { CaptureKind } from "@/lib/production-ui-types";

type Phase = "idle" | "recording" | "review" | "error";

type SpeechCtor = new () => {
  continuous: boolean;
  interimResults: boolean;
  lang: string;
  onresult: ((ev: SpeechResultEvent) => void) | null;
  onerror: ((ev: { error?: string }) => void) | null;
  onend: (() => void) | null;
  start(): void;
  stop(): void;
  abort(): void;
};

type SpeechResultEvent = {
  resultIndex: number;
  results: ArrayLike<{ isFinal: boolean; 0: { transcript: string } }>;
};

function speechCtor(): SpeechCtor | undefined {
  const w = window as unknown as {
    SpeechRecognition?: SpeechCtor;
    webkitSpeechRecognition?: SpeechCtor;
  };
  return w.SpeechRecognition ?? w.webkitSpeechRecognition;
}

function pickRecorderMime(): string {
  const types = ["audio/webm;codecs=opus", "audio/webm", "audio/mp4", "audio/ogg"];
  if (typeof MediaRecorder === "undefined" || !MediaRecorder.isTypeSupported) return "";
  return types.find((t) => MediaRecorder.isTypeSupported(t)) ?? "";
}

export function VoiceCaptureDialog({
  open,
  onOpenChange,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}) {
  const [phase, setPhase] = useState<Phase>("idle");
  const [kind, setKind] = useState<CaptureKind>("Note");
  const [transcript, setTranscript] = useState("");
  const [keepAudio, setKeepAudio] = useState(false);
  const [forceKeep, setForceKeep] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [playbackUrl, setPlaybackUrl] = useState<string | null>(null);
  const [level, setLevel] = useState(0);
  const [pending, setPending] = useState(false);
  const writes = useWrites();
  const stopRef = useRef<HTMLButtonElement>(null);
  const transcriptRef = useRef<HTMLTextAreaElement>(null);
  const phaseRef = useRef<Phase>("idle");
  const playbackUrlRef = useRef<string | null>(null);
  const blobRef = useRef<Blob | null>(null);
  const durationRef = useRef(0);
  const stopSessionRef = useRef<() => void>(() => {});

  useEffect(() => {
    phaseRef.current = phase;
  }, [phase]);
  useEffect(() => {
    playbackUrlRef.current = playbackUrl;
  }, [playbackUrl]);

  useEffect(() => {
    if (!open) return;

    let cancelled = false;
    let stream: MediaStream | null = null;
    let recorder: MediaRecorder | null = null;
    let audioCtx: AudioContext | null = null;
    let recognition: InstanceType<SpeechCtor> | null = null;
    let raf = 0;
    const chunks: Blob[] = [];
    let finals = "";
    const startedAt = performance.now();

    function cleanupStream() {
      cancelAnimationFrame(raf);
      try {
        recognition?.abort();
      } catch {
        /* ignore */
      }
      recognition = null;
      stream?.getTracks().forEach((t) => t.stop());
      stream = null;
      void audioCtx?.close();
      audioCtx = null;
    }

    stopSessionRef.current = () => {
      durationRef.current = (performance.now() - startedAt) / 1000;
      if (recorder && recorder.state !== "inactive") {
        recorder.stop();
        return;
      }
      const blob = blobRef.current ?? (chunks.length ? new Blob(chunks, { type: "audio/webm" }) : null);
      finishReview(blob);
    };

    function finishReview(blob: Blob | null) {
      cleanupStream();
      recorder = null;
      if (cancelled) {
        if (blob) revokeIfBlobUrl(URL.createObjectURL(blob));
        return;
      }
      blobRef.current = blob;
      if (blob) {
        revokeIfBlobUrl(playbackUrlRef.current);
        const url = URL.createObjectURL(blob);
        playbackUrlRef.current = url;
        setPlaybackUrl(url);
      }
      setPhase("review");
      requestAnimationFrame(() => transcriptRef.current?.focus());
    }

    async function start() {
      if (typeof MediaRecorder === "undefined" || !navigator.mediaDevices?.getUserMedia) {
        if (!cancelled) {
          setPhase("error");
          setError("This browser cannot record audio.");
        }
        return;
      }

      try {
        stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      } catch (e) {
        if (cancelled) return;
        const name = e instanceof DOMException ? e.name : "";
        setPhase("error");
        if (name === "NotAllowedError" || name === "PermissionDeniedError") {
          setError("Microphone access was denied. Allow it in the browser settings to record.");
        } else if (name === "NotFoundError") {
          setError("No microphone was found.");
        } else {
          setError("Could not start the microphone.");
        }
        return;
      }

      if (cancelled) {
        stream.getTracks().forEach((t) => t.stop());
        return;
      }

      const mime = pickRecorderMime();
      recorder = mime ? new MediaRecorder(stream, { mimeType: mime }) : new MediaRecorder(stream);
      recorder.ondataavailable = (ev) => {
        if (ev.data.size > 0) chunks.push(ev.data);
      };
      recorder.onstop = () => {
        const type = recorder?.mimeType || mime || "audio/webm";
        finishReview(new Blob(chunks, { type }));
      };
      recorder.start(200);
      setPhase("recording");
      requestAnimationFrame(() => stopRef.current?.focus());

      try {
        const Ctx =
          window.AudioContext ||
          (window as unknown as { webkitAudioContext?: typeof AudioContext }).webkitAudioContext;
        if (Ctx && stream) {
          audioCtx = new Ctx();
          const source = audioCtx.createMediaStreamSource(stream);
          const analyser = audioCtx.createAnalyser();
          analyser.fftSize = 256;
          source.connect(analyser);
          const data = new Uint8Array(analyser.frequencyBinCount);
          const tick = () => {
            analyser.getByteTimeDomainData(data);
            let sum = 0;
            for (const v of data) {
              const n = (v - 128) / 128;
              sum += n * n;
            }
            if (!cancelled) setLevel(Math.min(1, Math.sqrt(sum / data.length) * 4));
            raf = requestAnimationFrame(tick);
          };
          raf = requestAnimationFrame(tick);
        }
      } catch {
        /* meter is best-effort */
      }

      const Ctor = speechCtor();
      if (!Ctor) {
        setForceKeep(true);
        setKeepAudio(true);
        return;
      }
      try {
        const rec = new Ctor();
        recognition = rec;
        rec.continuous = true;
        rec.interimResults = true;
        rec.lang = navigator.language || "en-US";
        rec.onresult = (ev) => {
          let interim = "";
          for (let i = ev.resultIndex; i < ev.results.length; i++) {
            const piece = ev.results[i][0]?.transcript ?? "";
            if (ev.results[i].isFinal) finals += piece;
            else interim += piece;
          }
          if (!cancelled) setTranscript(`${finals}${interim}`);
        };
        rec.onerror = (ev) => {
          if (ev.error === "not-allowed" || ev.error === "service-not-allowed") {
            setForceKeep(true);
            setKeepAudio(true);
          }
        };
        rec.onend = () => {
          if (!cancelled && phaseRef.current === "recording") {
            try {
              rec.start();
            } catch {
              /* Chrome throws if we restart too fast */
            }
          }
        };
        rec.start();
      } catch {
        setForceKeep(true);
        setKeepAudio(true);
      }
    }

    setPhase("recording");
    setError(null);
    setTranscript("");
    setKind("Note");
    setKeepAudio(false);
    setForceKeep(false);
    setLevel(0);
    blobRef.current = null;
    void start();

    return () => {
      cancelled = true;
      if (recorder && recorder.state !== "inactive") {
        try {
          recorder.stop();
        } catch {
          /* ignore */
        }
      }
      cleanupStream();
    };
  }, [open]);

  function resetLocal() {
    revokeIfBlobUrl(playbackUrlRef.current);
    playbackUrlRef.current = null;
    setPlaybackUrl(null);
    blobRef.current = null;
    setTranscript("");
    setKind("Note");
    setKeepAudio(false);
    setForceKeep(false);
    setError(null);
    setPhase("idle");
    setLevel(0);
    setPending(false);
  }

  function discardAndClose() {
    resetLocal();
    onOpenChange(false);
  }

  async function submit() {
    const blob = blobRef.current;
    if (!blob) {
      setError("Nothing was recorded. Try again.");
      return;
    }
    const text = transcript.trim();
    const keep = forceKeep || keepAudio || !text;
    setError(null);
    setPending(true);
    try {
      await writes.captureVoice({
        audioBlob: blob,
        transcript: text,
        type: kind,
        keepAudio: keep,
        durationSeconds: durationRef.current || undefined,
      });
      resetLocal();
      onOpenChange(false);
    } catch (e) {
      setPending(false);
      setError(e instanceof Error ? e.message : "Capture failed. Retry without re-recording.");
    }
  }

  const recording = phase === "recording";
  const reviewing = phase === "review";
  const levelPct = Math.round(level * 100);

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) discardAndClose();
        else onOpenChange(true);
      }}
    >
      <DialogContent
        className="sm:max-w-md"
        showCloseButton={false}
        data-voice-capture=""
        onKeyDown={(e) => {
          if (e.key === "Enter" && recording && !e.ctrlKey && !e.metaKey && !e.shiftKey) {
            e.preventDefault();
            stopSessionRef.current();
          }
        }}
      >
        <DialogHeader>
          <DialogTitle>Voice capture</DialogTitle>
          <DialogDescription>
            {recording
              ? "Recording. Enter or Stop to review. Escape discards."
              : reviewing
                ? "Review the transcript, then capture. Escape discards."
                : "Lands in Inbox. Escape discards."}
          </DialogDescription>
        </DialogHeader>

        <div className="sr-only" role="status" aria-live="assertive" aria-atomic="true">
          {recording ? "Recording in progress" : reviewing ? "Review your capture" : (error ?? "")}
        </div>

        {phase === "error" ? <p className="text-sm text-destructive">{error}</p> : null}

        {recording ? (
          <div className="space-y-3">
            <div className="flex items-center gap-2">
              <span className="relative flex size-3" aria-hidden>
                <span className="absolute inline-flex size-full animate-ping rounded-full bg-attention opacity-60" />
                <span className="relative inline-flex size-3 rounded-full bg-attention" />
              </span>
              <MicIcon className="size-4" aria-hidden />
              <span className="text-sm font-medium">Recording</span>
            </div>
            <div>
              <p className="mb-1 text-[0.6875rem] text-muted-foreground">Microphone level</p>
              <div
                className="h-2 overflow-hidden rounded-full bg-muted"
                role="meter"
                aria-label="Microphone level"
                aria-valuemin={0}
                aria-valuemax={100}
                aria-valuenow={levelPct}
              >
                <div
                  className="h-full rounded-full bg-primary transition-[width] duration-75"
                  style={{ width: `${levelPct}%` }}
                />
              </div>
            </div>
            <div className="min-h-16 rounded-lg bg-muted/50 px-3 py-2 text-sm">
              {transcript ? (
                <p className="whitespace-pre-wrap">{transcript}</p>
              ) : forceKeep ? (
                <p className="text-muted-foreground">
                  Live transcript unavailable — audio will be kept.
                </p>
              ) : (
                <p className="text-muted-foreground">Listening…</p>
              )}
            </div>
            <Button ref={stopRef} type="button" onClick={() => stopSessionRef.current()}>
              <SquareIcon />
              Stop
            </Button>
          </div>
        ) : null}

        {reviewing ? (
          <div className="space-y-3">
            <CaptureKindTabs value={kind} onChange={setKind} disabled={pending} />
            {playbackUrl ? (
              <audio
                className="w-full"
                controls
                src={playbackUrl}
                preload="metadata"
                aria-label="Playback of recorded audio"
              />
            ) : null}
            <Textarea
              ref={transcriptRef}
              rows={5}
              value={transcript}
              onChange={(e) => setTranscript(e.target.value)}
              placeholder="Transcript…"
              aria-label="Transcript"
            />
            {forceKeep ? (
              <p className="text-xs text-muted-foreground">
                Live transcript unavailable — audio will be kept.
              </p>
            ) : null}
            <div className="flex items-center gap-2">
              <Checkbox
                id="keep-audio"
                checked={forceKeep || keepAudio}
                disabled={forceKeep || pending}
                onCheckedChange={(v) => setKeepAudio(v === true)}
              />
              <Label htmlFor="keep-audio" className="text-sm font-normal">
                Keep audio
              </Label>
            </div>
            {error ? <p className="text-xs text-destructive">{error}</p> : null}
            <div className="flex justify-end gap-2">
              <Button type="button" variant="outline" onClick={discardAndClose}>
                Discard
              </Button>
              <Button type="button" onClick={() => void submit()} disabled={pending}>
                {pending ? "Capturing…" : "Capture"}
              </Button>
            </div>
          </div>
        ) : null}

        {phase === "error" ? (
          <div className="flex justify-end">
            <Button type="button" variant="outline" onClick={discardAndClose}>
              Close
            </Button>
          </div>
        ) : null}
      </DialogContent>
    </Dialog>
  );
}
