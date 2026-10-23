/**
 * The mock's Unsplash (cover-images plan §3.9): eight landscape scenes drawn as SVG and bundled
 * with the app, each with a made-up photographer and a few keywords. The mock never touches
 * the network. Search returns the scenes whose keywords match the words, or all of them, six
 * to a page so "More photos" has something to load.
 */
import cityNight from "@/lib/mock/covers/city-night.svg";
import desertDunes from "@/lib/mock/covers/desert-dunes.svg";
import forestPath from "@/lib/mock/covers/forest-path.svg";
import lakeMist from "@/lib/mock/covers/lake-mist.svg";
import meadow from "@/lib/mock/covers/meadow.svg";
import mountainDawn from "@/lib/mock/covers/mountain-dawn.svg";
import oceanCalm from "@/lib/mock/covers/ocean-calm.svg";
import workbench from "@/lib/mock/covers/workbench.svg";
import { clampPosition, coverColor, coverQuery, DEFAULT_POSITION_Y, MAX_QUERY } from "@/lib/cover";
import type { CoverPhoto, CoverRead, CoverSearchRead } from "@/lib/production-ui-types";

export interface MockScene {
  id: string;
  url: string;
  color: string;
  alt: string;
  photographer: string;
  keywords: string[];
}

export const MOCK_SCENE_WIDTH = 1600;
export const MOCK_SCENE_HEIGHT = 900;
export const MOCK_PAGE_SIZE = 6;
const REFERRAL = "utm_source=lifeos&utm_medium=referral";
/** The made-up photographers have no Unsplash profiles, so their links go to Unsplash itself. */
export const MOCK_UNSPLASH_LINK = `https://unsplash.com/?${REFERRAL}`;

export const MOCK_SCENES: readonly MockScene[] = [
  {
    id: "mock-mountain-dawn",
    url: mountainDawn,
    color: "#f0a35e",
    alt: "A mountain ridge at dawn",
    photographer: "Ada Hollis",
    keywords: ["mountain", "ridge", "dawn", "sunrise", "hills", "climb", "run", "walk", "outdoors"],
  },
  {
    id: "mock-ocean-calm",
    url: oceanCalm,
    color: "#5b8db8",
    alt: "A calm sea under a pale sky",
    photographer: "Teo Marin",
    keywords: ["ocean", "sea", "calm", "water", "beach", "horizon", "blue", "swim"],
  },
  {
    id: "mock-forest-path",
    url: forestPath,
    color: "#3f6b4a",
    alt: "A path through a pine forest",
    photographer: "Ines Vale",
    keywords: ["forest", "trees", "path", "trail", "green", "nature", "health", "body", "steward"],
  },
  {
    id: "mock-desert-dunes",
    url: desertDunes,
    color: "#d4a056",
    alt: "Desert dunes in late light",
    photographer: "Omar Reyes",
    keywords: ["desert", "dunes", "sand", "warm", "travel", "journey"],
  },
  {
    id: "mock-city-night",
    url: cityNight,
    color: "#24305e",
    alt: "City lights at night",
    photographer: "Mina Cho",
    keywords: ["city", "night", "lights", "work", "ship", "software", "lifeos", "production"],
  },
  {
    id: "mock-meadow",
    url: meadow,
    color: "#8fbf5a",
    alt: "A wildflower meadow",
    photographer: "Lars Brandt",
    keywords: ["meadow", "flowers", "spring", "family", "home", "father", "joy"],
  },
  {
    id: "mock-lake-mist",
    url: lakeMist,
    color: "#9fb4c4",
    alt: "A lake in morning mist",
    photographer: "Noor Haddad",
    keywords: ["lake", "mist", "morning", "sleep", "rest", "quiet", "calm"],
  },
  {
    id: "mock-workbench",
    url: workbench,
    color: "#8a5a3c",
    alt: "Tools on a workbench",
    photographer: "Sam Okafor",
    keywords: ["workshop", "tools", "craft", "build", "desk", "project", "make", "web"],
  },
];

export function sceneById(id: string): MockScene | undefined {
  return MOCK_SCENES.find((s) => s.id === id);
}

function matches(scene: MockScene, words: string[]): boolean {
  return words.some((w) => scene.keywords.some((k) => k.startsWith(w) || w.startsWith(k)));
}

function toPhoto(scene: MockScene): CoverPhoto {
  return {
    id: scene.id,
    thumbUrl: scene.url,
    color: scene.color,
    alt: scene.alt,
    width: MOCK_SCENE_WIDTH,
    height: MOCK_SCENE_HEIGHT,
    photographer: scene.photographer,
    photographerLink: MOCK_UNSPLASH_LINK,
  };
}

/** `GET /api/covers/search` against the scenes, with CoverService's refusals in its words. */
export function mockSearchScenes(query: string, page = 1): CoverSearchRead {
  const words = coverQuery(query);
  if (!words) throw new Error("Say what to search for, e.g. mountain sunrise.");
  if (query.trim().split(/\s+/).join(" ").length > MAX_QUERY) {
    throw new Error(`Search for at most ${MAX_QUERY} characters; got ${query.trim().length}.`);
  }
  if (!Number.isInteger(page) || page < 1 || page > 50) throw new Error(`Pages run from 1 to 50; got ${page}.`);
  const lower = words.toLowerCase().split(" ").filter((w) => w.length > 1);
  const found = MOCK_SCENES.filter((s) => matches(s, lower));
  const all = found.length > 0 ? found : MOCK_SCENES;
  const totalPages = Math.ceil(all.length / MOCK_PAGE_SIZE);
  return {
    configured: true,
    query: words,
    page,
    total: all.length,
    totalPages,
    unsplashLink: MOCK_UNSPLASH_LINK,
    photos: all.slice((page - 1) * MOCK_PAGE_SIZE, page * MOCK_PAGE_SIZE).map(toPhoto),
  };
}

/** A scene as `attributes.cover` stores it: snake_case, as `Cover.ToJson` writes it. */
export function storedCoverOf(scene: MockScene, positionY: number, setAt: string): Record<string, unknown> {
  return {
    source: "unsplash",
    photo_id: scene.id,
    url: scene.url,
    color: scene.color,
    width: MOCK_SCENE_WIDTH,
    height: MOCK_SCENE_HEIGHT,
    alt: scene.alt,
    photographer: scene.photographer,
    photographer_url: "https://unsplash.com/",
    photo_url: "https://unsplash.com/",
    position_y: clampPosition(positionY),
    set_at: setAt,
  };
}

function withReferral(url: string): string {
  return `${url}${url.includes("?") ? "&" : "?"}${REFERRAL}`;
}

/** A stored cover as the API reads it, links with the referral on; null when it's malformed. */
export function readStoredCover(raw: unknown): CoverRead | null {
  if (!raw || typeof raw !== "object" || Array.isArray(raw)) return null;
  const c = raw as Record<string, unknown>;
  if (typeof c.url !== "string" || !c.url || typeof c.photographer !== "string" || !c.photographer) return null;
  const position = typeof c.position_y === "number" ? c.position_y : DEFAULT_POSITION_Y;
  return {
    source: typeof c.source === "string" ? c.source : "unsplash",
    photoId: typeof c.photo_id === "string" ? c.photo_id : "",
    url: c.url,
    color: coverColor(c.color),
    width: typeof c.width === "number" ? c.width : MOCK_SCENE_WIDTH,
    height: typeof c.height === "number" ? c.height : MOCK_SCENE_HEIGHT,
    alt: typeof c.alt === "string" ? c.alt : null,
    photographer: c.photographer,
    photographerLink: withReferral(typeof c.photographer_url === "string" ? c.photographer_url : "https://unsplash.com/"),
    photoLink: withReferral(typeof c.photo_url === "string" ? c.photo_url : "https://unsplash.com/"),
    unsplashLink: MOCK_UNSPLASH_LINK,
    positionY: clampPosition(position),
    setAt: typeof c.set_at === "string" ? c.set_at : "",
  };
}
