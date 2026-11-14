import {
  FileAudioIcon,
  FileIcon,
  FileImageIcon,
  FileTextIcon,
  FileVideoIcon,
  type LucideIcon,
} from "lucide-react";

/** Kernel default (`BSK_MAX_ARTIFACT_BYTES`) — reject before we copy into the mock store. */
export const MAX_ARTIFACT_BYTES = 25 * 1024 * 1024;

export function formatBytes(n: number): string {
  if (n < 1024) return `${n} B`;
  if (n < 1024 * 1024) {
    const kb = n / 1024;
    return `${kb < 10 ? kb.toFixed(1) : Math.round(kb)} KB`;
  }
  return `${(n / (1024 * 1024)).toFixed(1)} MB`;
}

export function formatDuration(seconds: number): string {
  const s = Math.max(0, Math.round(seconds));
  const m = Math.floor(s / 60);
  const r = s % 60;
  return `${m}:${r.toString().padStart(2, "0")}`;
}

export function iconForContentType(contentType: string): LucideIcon {
  const ct = contentType.toLowerCase();
  if (ct.startsWith("audio/")) return FileAudioIcon;
  if (ct.startsWith("image/")) return FileImageIcon;
  if (ct.startsWith("video/")) return FileVideoIcon;
  if (
    ct.includes("pdf") ||
    ct.startsWith("text/") ||
    ct.includes("word") ||
    ct.includes("document")
  ) {
    return FileTextIcon;
  }
  return FileIcon;
}

export function extensionForMime(contentType: string): string {
  const ct = contentType.split(";")[0]?.trim().toLowerCase() ?? "";
  if (ct === "audio/webm") return "webm";
  if (ct === "audio/mp4" || ct === "audio/mpeg") return "m4a";
  if (ct === "audio/ogg") return "ogg";
  if (ct === "audio/wav" || ct === "audio/wave") return "wav";
  return "bin";
}

export async function sha256Hex(blob: Blob): Promise<string | undefined> {
  try {
    const digest = await crypto.subtle.digest("SHA-256", await blob.arrayBuffer());
    return [...new Uint8Array(digest)]
      .map((b) => b.toString(16).padStart(2, "0"))
      .join("");
  } catch {
    return undefined;
  }
}

export function dataUrlFor(content: string, contentType: string): string {
  return `data:${contentType};charset=utf-8,${encodeURIComponent(content)}`;
}

export function silentWavDataUrl(): string {
  // 44-byte empty WAV header — enough for <audio> to accept the URL.
  return "data:audio/wav;base64,UklGRiQAAABXQVZFZm10IBAAAAABAAEAESsAACJWAAACABAAZGF0YQAAAAA=";
}

export function objectUrlFor(blob: Blob): string {
  try {
    if (typeof URL !== "undefined" && typeof URL.createObjectURL === "function") {
      return URL.createObjectURL(blob);
    }
  } catch {
    /* jsdom's createObjectURL rejects some Blob implementations */
  }
  return `blob:mock-${blob.size}`;
}

export function revokeIfBlobUrl(url: string | null | undefined) {
  if (url && url.startsWith("blob:") && typeof URL !== "undefined" && URL.revokeObjectURL) {
    try {
      URL.revokeObjectURL(url);
    } catch {
      /* ignore */
    }
  }
}

/** Minimal silent WAV so seeded voice items have a playable object URL. */
export function silentWavBlob(seconds: number, sampleRate = 8000): Blob {
  const samples = Math.max(1, Math.floor(seconds * sampleRate));
  const dataSize = samples * 2;
  const buf = new ArrayBuffer(44 + dataSize);
  const view = new DataView(buf);
  writeAscii(view, 0, "RIFF");
  view.setUint32(4, 36 + dataSize, true);
  writeAscii(view, 8, "WAVE");
  writeAscii(view, 12, "fmt ");
  view.setUint32(16, 16, true);
  view.setUint16(20, 1, true);
  view.setUint16(22, 1, true);
  view.setUint32(24, sampleRate, true);
  view.setUint32(28, sampleRate * 2, true);
  view.setUint16(32, 2, true);
  view.setUint16(34, 16, true);
  writeAscii(view, 36, "data");
  view.setUint32(40, dataSize, true);
  return new Blob([buf], { type: "audio/wav" });
}

function writeAscii(view: DataView, offset: number, text: string) {
  for (let i = 0; i < text.length; i++) view.setUint8(offset + i, text.charCodeAt(i));
}

export function openArtifactUrl(url: string | null | undefined) {
  if (!url) return;
  window.open(url, "_blank", "noopener,noreferrer");
}

export function downloadArtifactUrl(
  url: string | null | undefined,
  filename: string | null | undefined,
) {
  if (!url) return;
  const a = document.createElement("a");
  a.href = url;
  a.download = filename?.trim() || "download";
  a.rel = "noopener";
  document.body.appendChild(a);
  a.click();
  a.remove();
}
