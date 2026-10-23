import { useId } from "react";

/**
 * Where the night sky's stars sit, as x, y and radius in a 1200 × 320 sky anchored top right.
 * They keep clear of the words on a phone (which sees x 555 on) and a desktop (x 0 on), and of
 * the moon, so no star reads as a stray full stop.
 */
const STARS: readonly [number, number, number][] = [
  [330, 40, 1.2],
  [380, 110, 1.1],
  [430, 70, 1.3],
  [480, 26, 1.1],
  [520, 128, 1.2],
  [600, 20, 1.1],
  [700, 44, 1.4],
  [760, 18, 1.1],
  [850, 100, 1.6],
  [900, 40, 1.2],
  [960, 128, 1.2],
  [1100, 30, 1.5],
  [1150, 140, 1.2],
  [1180, 70, 1.8],
];

/** Pines on the middle hills: the tip's x and y, and the tree's height, in the 2400 × 160 hills. */
const PINES: readonly [number, number, number][] = [
  [2262, 40, 30],
  [2286, 52, 22],
  [2332, 38, 32],
  [2360, 50, 24],
  [1180, 78, 22],
  [1204, 70, 28],
  [600, 62, 20],
  [622, 56, 26],
];

/** The sky's stars, in the dark only; cropped to fit, as stars can be. */
function Stars() {
  return (
    <svg
      viewBox="0 0 1200 320"
      preserveAspectRatio="xMaxYMin slice"
      className="absolute inset-0 hidden size-full dark:block"
      style={{ fill: "var(--banner-sun)" }}
    >
      {STARS.map(([x, y, r]) => (
        <circle key={`${x}-${y}`} cx={x} cy={y} r={r} opacity={0.8} />
      ))}
    </svg>
  );
}

/** The dawn sun in its glow, or in the dark a crescent moon. Drawn under the hills, so it rises behind them. */
function Sun({ id }: { id: string }) {
  const glow = `${id}-glow`;
  const moon = `${id}-moon`;
  return (
    <svg viewBox="-70 -70 140 140" className="absolute top-3 right-[10%] size-24 md:top-4 md:size-28">
      <defs>
        <radialGradient id={glow}>
          <stop offset="0" style={{ stopColor: "var(--banner-glow)" }} />
          <stop offset="1" style={{ stopColor: "var(--banner-glow)", stopOpacity: 0 }} />
        </radialGradient>
        <mask id={moon}>
          <rect x="-70" y="-70" width="140" height="140" fill="white" />
          <circle cx="13" cy="-9" r="22" fill="black" />
        </mask>
      </defs>
      <circle r="70" fill={`url(#${glow})`} />
      <circle className="dark:hidden" r="27" style={{ fill: "var(--banner-sun)" }} />
      <circle className="hidden dark:inline" r="24" mask={`url(#${moon})`} style={{ fill: "var(--banner-sun)" }} />
    </svg>
  );
}

/**
 * Three ridges of teal hills, a trail of dotted steps winding up the nearest, and pines on the
 * middle one. Wider than any banner gets (15 to 1), it's anchored bottom right and cropped only
 * from the left: a phone sees the trail and the nearest pines, a wide screen the whole range.
 */
function Hills() {
  return (
    <svg
      viewBox="0 0 2400 160"
      preserveAspectRatio="xMaxYMax slice"
      className="absolute inset-x-0 bottom-0 h-[62%] w-full"
    >
      <path
        d="M0 80 C300 40 560 48 820 58 S1300 26 1620 40 S2100 22 2400 46 L2400 160 L0 160 Z"
        style={{ fill: "var(--banner-hill-far)" }}
      />
      <path
        d="M0 104 C260 84 520 78 780 90 S1240 110 1520 92 S1980 60 2400 76 L2400 160 L0 160 Z"
        style={{ fill: "var(--banner-hill-mid)" }}
      />
      <g style={{ fill: "var(--banner-tree)" }}>
        {PINES.map(([x, y, h]) => (
          <path key={`${x}-${y}`} d={`M${x} ${y} l${-h * 0.36} ${h} h${h * 0.72} Z`} />
        ))}
      </g>
      <path
        d="M0 138 C320 120 640 116 960 128 S1460 146 1740 122 S2160 100 2400 112 L2400 160 L0 160 Z"
        style={{ fill: "var(--banner-hill-near)" }}
      />
      <path
        d="M1930 160 C1960 148 2040 144 2080 132 S2130 108 2172 98 S2210 80 2232 70"
        fill="none"
        strokeWidth="5"
        strokeLinecap="round"
        strokeDasharray="0.5 13"
        style={{ stroke: "var(--banner-trail)" }}
      />
    </svg>
  );
}

/**
 * The banner's picture: a dawn over teal hills, or a moonlit night in the dark. Each part holds
 * its own place (the sun near the top right, the hills along the bottom), so it reads the same
 * on a phone and a wide screen. It's drawn from the page's tokens, so it follows the theme, and
 * it's decoration only: hidden from screen readers and dropped in high-contrast mode.
 */
function BannerScene() {
  // Kept to characters url(#…) takes as they are.
  const id = `banner${useId().replace(/[^\w-]/g, "")}`;
  return (
    <div
      data-banner-scene
      aria-hidden
      className="pointer-events-none absolute inset-0 -z-10"
      style={{ background: "linear-gradient(to bottom, var(--banner-sky-top), var(--banner-sky-bottom))" }}
    >
      <Stars />
      <Sun id={id} />
      <Hills />
    </div>
  );
}

/**
 * The Activity page's banner: the page's name over a drawn scene, with the sync's status and
 * Sync now (`children`) on a frosted strip so they read wherever the hills fall.
 */
export function ActivityBanner({ children }: { children?: React.ReactNode }) {
  return (
    <header className="relative isolate overflow-hidden rounded-xl bg-background ring-1 ring-foreground/10">
      <BannerScene />
      {/* The words keep to the top left, on the sky: where they reach a hill it's the far one,
          and the page's text reads on it at 4.5:1 or more. */}
      <div className="flex min-h-40 flex-col justify-between gap-4 p-4 sm:flex-row sm:items-end md:min-h-44 md:p-5">
        <div className="sm:self-start">
          <p className="type-scale-section text-primary">Oura</p>
          <h1 className="font-heading text-4xl leading-none md:text-5xl">Activity</h1>
          <p className="mt-1.5 max-w-xs text-sm text-foreground">Steps, exercise, sleep and readiness.</p>
        </div>
        {children ? (
          <div className="flex flex-wrap items-center gap-2 self-start rounded-lg bg-card/85 py-1 pr-1 pl-3 ring-1 ring-foreground/10 backdrop-blur-sm sm:self-end">
            {children}
          </div>
        ) : null}
      </div>
    </header>
  );
}
