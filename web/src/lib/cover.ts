/**
 * Cover images (COVER-1..3, D15; cover-images plan §3.6): which subjects can have one, how a
 * stored Unsplash photo is sized for a box, where a banner is centred, and the words the
 * picker shows when a search finds nothing.
 */
import type { CoverPhoto, CoverRead, CoverSearchRead, SubjectType } from "@/lib/production-ui-types";

/** Identity statements, goals and projects have covers; nothing else does (D15). */
export const COVER_TYPES: readonly SubjectType[] = ["Value", "Goal", "Project"];

export const COVER_IMAGE_HOST = "images.unsplash.com";
export const DEFAULT_COVER_COLOR = "#808080";
export const DEFAULT_POSITION_Y = 50;
/** How far one Up or Down press moves a banner while repositioning. */
export const POSITION_STEP = 5;
/** The longest search the server takes. */
export const MAX_QUERY = 100;
/** The width a banner asks Unsplash for (1,800 at 2×): the desktop panel is at most 800 px. */
export const BANNER_WIDTH = 900;

export function canHaveCover(type: SubjectType | undefined | null): boolean {
  return !!type && COVER_TYPES.includes(type);
}

/** Whether `url` is an Unsplash image, which can be asked for at a size. */
export function isUnsplashImage(url: string): boolean {
  try {
    const parsed = new URL(url);
    return parsed.protocol === "https:" && parsed.hostname === COVER_IMAGE_HOST && parsed.port === "";
  } catch {
    return false;
  }
}

/**
 * The image at `width` CSS px: an Unsplash photo is asked for at that size (its `ixid` kept);
 * anything else (the mock's bundled scenes) is used as it is.
 */
export function coverSrc(url: string, width: number): string {
  if (!isUnsplashImage(url)) return url;
  const sized = new URL(url);
  sized.searchParams.set("w", String(Math.round(width)));
  sized.searchParams.set("q", "75");
  sized.searchParams.set("auto", "format");
  sized.searchParams.set("fit", "max");
  return sized.toString();
}

/** A 1× and 2× `srcset` for an Unsplash photo; none for anything else. */
export function coverSrcSet(url: string, width: number): string | undefined {
  if (!isUnsplashImage(url)) return undefined;
  return `${coverSrc(url, width)} 1x, ${coverSrc(url, width * 2)} 2x`;
}

/** A whole position from 0 (top) to 100 (bottom); the middle when it isn't a number. */
export function clampPosition(value: number): number {
  if (!Number.isFinite(value)) return DEFAULT_POSITION_Y;
  return Math.min(100, Math.max(0, Math.round(value)));
}

/** The CSS `object-position` that centres the banner at `positionY`. */
export function objectPosition(positionY: number): string {
  return `50% ${clampPosition(positionY)}%`;
}

/**
 * Where a key moves a banner while repositioning, or null for a key that doesn't. The photo
 * moves the way the arrow points, as it does under a finger: Up shows more of its lower part.
 */
export function keyPosition(positionY: number, key: string): number | null {
  switch (key) {
    case "ArrowUp":
      return clampPosition(positionY + POSITION_STEP);
    case "ArrowDown":
      return clampPosition(positionY - POSITION_STEP);
    case "PageUp":
      return clampPosition(positionY + POSITION_STEP * 4);
    case "PageDown":
      return clampPosition(positionY - POSITION_STEP * 4);
    case "Home":
      return 0;
    case "End":
      return 100;
    default:
      return null;
  }
}

/**
 * Where a drag of `dy` px (down is positive) leaves a banner that started at `startY`. The
 * photo follows the pointer: what moves is the part of it hidden above and below the box,
 * so a photo no taller than the box doesn't move at all.
 */
export function dragPosition(input: {
  startY: number;
  dy: number;
  boxWidth: number;
  boxHeight: number;
  imageWidth: number;
  imageHeight: number;
}): number {
  const { startY, dy, boxWidth, boxHeight, imageWidth, imageHeight } = input;
  if (imageWidth <= 0 || imageHeight <= 0 || boxWidth <= 0 || boxHeight <= 0) return clampPosition(startY);
  const scale = Math.max(boxWidth / imageWidth, boxHeight / imageHeight);
  const hidden = imageHeight * scale - boxHeight;
  if (hidden < 1) return clampPosition(startY);
  return clampPosition(startY - (dy / hidden) * 100);
}

/** What a repositioning banner says it shows, for a screen reader. */
export function positionText(positionY: number): string {
  const y = clampPosition(positionY);
  if (y === 0) return "Showing the top of the photo";
  if (y === 100) return "Showing the bottom of the photo";
  return `Showing the photo ${y}% of the way down`;
}

/** "Photo by X on Unsplash": a thumbnail's tooltip, where there's no room for the links. */
export function creditText(cover: Pick<CoverRead, "photographer">): string {
  return `Photo by ${cover.photographer} on Unsplash`;
}

/** The picker's first search: the subject's title, spaces squeezed, cut to what the server takes. */
export function coverQuery(title: string): string {
  return title.trim().split(/\s+/).join(" ").slice(0, MAX_QUERY).trim();
}

/** What the picker says instead of photos, or null when there are photos to show. */
export function coverSearchMiss(read: CoverSearchRead | undefined): string | null {
  if (!read) return null;
  if (!read.configured) {
    return "Unsplash isn't set up on this server. It needs Unsplash__AccessKey in its settings.";
  }
  if (read.photos.length === 0) return `No photos for '${read.query}'. Try fewer or simpler words.`;
  return null;
}

/** The next page to ask for, or undefined when the last one is in. */
export function nextCoverPage(last: CoverSearchRead): number | undefined {
  return last.configured && last.page < last.totalPages && last.photos.length > 0 ? last.page + 1 : undefined;
}

const COLOR = /^#[0-9a-f]{6}$/i;

function num(value: unknown, fallback = 0): number {
  const n = typeof value === "number" ? value : Number(value);
  return Number.isFinite(n) ? n : fallback;
}

function str(value: unknown): string | null {
  return typeof value === "string" && value.trim() !== "" ? value : null;
}

/** A photo's colour as a box background: its own when it's `#rrggbb`, else grey. */
export function coverColor(color: unknown): string {
  return typeof color === "string" && COLOR.test(color) ? color.toLowerCase() : DEFAULT_COVER_COLOR;
}

/** A cover as the API sends it, numbers made numbers; null when it can't be shown. */
export function normalizeCover(raw: unknown): CoverRead | null {
  if (!raw || typeof raw !== "object") return null;
  const c = raw as Record<string, unknown>;
  const url = str(c.url);
  const photographer = str(c.photographer);
  if (!url || !photographer) return null;
  return {
    source: str(c.source) ?? "unsplash",
    photoId: str(c.photoId) ?? "",
    url,
    color: coverColor(c.color),
    width: num(c.width),
    height: num(c.height),
    alt: str(c.alt),
    photographer,
    photographerLink: str(c.photographerLink) ?? "",
    photoLink: str(c.photoLink) ?? "",
    unsplashLink: str(c.unsplashLink) ?? "",
    positionY: clampPosition(num(c.positionY, DEFAULT_POSITION_Y)),
    setAt: str(c.setAt) ?? "",
  };
}

/** A list item or detail with its cover normalized (or null). */
export function withNormalCover<T extends { cover?: CoverRead | null }>(raw: T): T {
  return { ...raw, cover: normalizeCover(raw.cover) };
}

function normalizePhoto(raw: unknown): CoverPhoto | null {
  if (!raw || typeof raw !== "object") return null;
  const p = raw as Record<string, unknown>;
  const id = str(p.id);
  const thumbUrl = str(p.thumbUrl);
  if (!id || !thumbUrl) return null;
  return {
    id,
    thumbUrl,
    color: coverColor(p.color),
    alt: str(p.alt),
    width: num(p.width),
    height: num(p.height),
    photographer: str(p.photographer) ?? "Unknown",
    photographerLink: str(p.photographerLink) ?? "",
  };
}

/** A search page as the API sends it, numbers made numbers and unusable photos left out. */
export function normalizeCoverSearch(raw: unknown): CoverSearchRead {
  const r = (raw && typeof raw === "object" ? raw : {}) as Record<string, unknown>;
  const photos = Array.isArray(r.photos) ? r.photos.map(normalizePhoto).filter((p): p is CoverPhoto => p !== null) : [];
  return {
    configured: r.configured === true,
    query: typeof r.query === "string" ? r.query : "",
    page: num(r.page, 1),
    total: num(r.total),
    totalPages: num(r.totalPages),
    unsplashLink: str(r.unsplashLink) ?? "https://unsplash.com/?utm_source=lifeos&utm_medium=referral",
    photos,
  };
}
