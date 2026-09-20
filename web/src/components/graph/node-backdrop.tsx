/**
 * Decorative SVG watermarks for graph nodes. One drawing per SubjectType.
 *
 * Customize: edit the matching function in TYPE_BACKDROPS. Art is
 * `currentColor` (the node's `--node-accent`) and sits behind the title.
 * Prefer the right half of the 280×84 viewBox so text stays readable.
 * Unique gradient/pattern ids use React `useId` so sibling nodes don't clash.
 */
import { useId, type ReactNode } from "react";
import type { SubjectType } from "@/lib/production-ui-types";

function Frame({ children }: { children: ReactNode }) {
  return (
    <svg
      viewBox="0 0 280 84"
      className="absolute inset-0 size-full"
      preserveAspectRatio="xMaxYMid slice"
      aria-hidden
    >
      {children}
    </svg>
  );
}

const TYPE_BACKDROPS: Record<SubjectType, (uid: string) => ReactNode> = {
  Value: () => (
    <>
      <polygon points="238,20 266,42 238,64 210,42" fill="currentColor" opacity="0.12" />
      <polygon points="238,20 266,42 238,42" fill="currentColor" opacity="0.16" />
      <polygon points="238,42 266,42 238,64" fill="currentColor" opacity="0.07" />
      <path
        d="M36 30a14 14 0 0 1 14-14M28 42a22 22 0 0 1 22-22"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.4"
        opacity="0.2"
      />
      <path
        d="M44 54c7-4 12-11 12-20"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.2"
        opacity="0.16"
      />
    </>
  ),
  Goal: () => (
    <>
      <circle cx="232" cy="42" r="36" fill="none" stroke="currentColor" strokeWidth="8" opacity="0.14" />
      <circle cx="232" cy="42" r="24" fill="none" stroke="currentColor" strokeWidth="6" opacity="0.2" />
      <circle cx="232" cy="42" r="12" fill="none" stroke="currentColor" strokeWidth="4" opacity="0.26" />
      <circle cx="232" cy="42" r="4.5" fill="currentColor" opacity="0.34" />
    </>
  ),
  Project: (uid) => (
    <>
      <defs>
        <pattern id={`${uid}-grid`} width="14" height="14" patternUnits="userSpaceOnUse">
          <path d="M14 0H0V14" fill="none" stroke="currentColor" strokeWidth="0.7" opacity="0.45" />
        </pattern>
      </defs>
      <rect width="280" height="84" fill={`url(#${uid}-grid)`} opacity="0.35" />
      <path d="M18 14h44l8 8H18z" fill="currentColor" opacity="0.16" />
      <rect x="200" y="18" width="58" height="48" rx="3" fill="none" stroke="currentColor" strokeWidth="1.2" opacity="0.22" />
    </>
  ),
  Task: () => (
    <>
      {[22, 36, 50, 64].map((y) => (
        <line
          key={y}
          x1="18"
          x2="262"
          y1={y}
          y2={y}
          stroke="currentColor"
          strokeWidth="1"
          opacity="0.12"
        />
      ))}
      <rect x="18" y="10" width="10" height="10" rx="2" fill="none" stroke="currentColor" strokeWidth="1.3" opacity="0.28" />
      <path d="M20.5 15.2 22.6 17.4 26.4 12.8" fill="none" stroke="currentColor" strokeWidth="1.3" opacity="0.3" />
    </>
  ),
  Problem: (uid) => (
    <>
      <defs>
        <pattern id={`${uid}-caution`} width="10" height="10" patternUnits="userSpaceOnUse" patternTransform="rotate(38)">
          <rect width="5" height="10" fill="currentColor" opacity="0.22" />
        </pattern>
      </defs>
      <polygon points="188,0 280,0 280,84 230,84" fill={`url(#${uid}-caution)`} opacity="0.55" />
      <polygon points="232,0 280,0 280,48" fill="currentColor" opacity="0.16" />
    </>
  ),
  Idea: () => (
    <>
      {[-48, -32, -16, 0, 16, 32, 48].map((deg) => {
        const rad = ((deg - 18) * Math.PI) / 180;
        const x2 = 248 + Math.cos(rad) * 70;
        const y2 = 18 + Math.sin(rad) * 70;
        return (
          <line
            key={deg}
            x1="248"
            y1="18"
            x2={x2}
            y2={y2}
            stroke="currentColor"
            strokeWidth="2"
            opacity="0.12"
          />
        );
      })}
      <circle cx="248" cy="18" r="10" fill="currentColor" opacity="0.16" />
      <circle cx="248" cy="18" r="4" fill="currentColor" opacity="0.28" />
    </>
  ),
  Decision: () => (
    <>
      <rect x="140" y="0" width="140" height="84" fill="currentColor" opacity="0.08" />
      <line x1="140" y1="10" x2="140" y2="74" stroke="currentColor" strokeWidth="1.2" opacity="0.22" />
      <line x1="112" y1="42" x2="168" y2="42" stroke="currentColor" strokeWidth="1.6" opacity="0.28" />
      <circle cx="112" cy="42" r="7" fill="none" stroke="currentColor" strokeWidth="1.4" opacity="0.28" />
      <circle cx="168" cy="42" r="7" fill="none" stroke="currentColor" strokeWidth="1.4" opacity="0.28" />
      <path d="M140 22v12" stroke="currentColor" strokeWidth="1.6" opacity="0.28" />
    </>
  ),
  Commitment: () => (
    <>
      <circle cx="226" cy="42" r="22" fill="none" stroke="currentColor" strokeWidth="3" opacity="0.16" />
      <circle cx="252" cy="42" r="22" fill="none" stroke="currentColor" strokeWidth="3" opacity="0.22" />
    </>
  ),
  Constraint: (uid) => (
    <>
      <defs>
        <pattern id={`${uid}-hatch`} width="8" height="8" patternUnits="userSpaceOnUse">
          <path d="M-1,1 l2,-2 M0,8 l8,-8 M7,9 l2,-2" stroke="currentColor" strokeWidth="0.8" opacity="0.55" />
        </pattern>
      </defs>
      <rect width="280" height="84" fill={`url(#${uid}-hatch)`} opacity="0.28" />
      <rect x="214" y="18" width="48" height="48" rx="2" fill="none" stroke="currentColor" strokeWidth="1.6" opacity="0.28" />
    </>
  ),
  Person: () => (
    <>
      <ellipse cx="236" cy="46" rx="30" ry="34" fill="currentColor" opacity="0.08" />
      <circle cx="236" cy="32" r="11" fill="currentColor" opacity="0.16" />
      <path d="M214 68c4-14 40-14 44 0" fill="currentColor" opacity="0.14" />
    </>
  ),
  Area: () => (
    <>
      <polygon points="236,8 244,42 236,76 228,42" fill="currentColor" opacity="0.14" />
      <polygon points="202,42 236,34 270,42 236,50" fill="currentColor" opacity="0.1" />
      <circle cx="236" cy="42" r="26" fill="none" stroke="currentColor" strokeWidth="1.2" opacity="0.2" />
      <circle cx="236" cy="42" r="3" fill="currentColor" opacity="0.28" />
    </>
  ),
  Habit: () => (
    <>
      <ellipse
        cx="236"
        cy="42"
        rx="34"
        ry="18"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.6"
        strokeDasharray="5 6"
        opacity="0.22"
      />
      <ellipse
        cx="236"
        cy="42"
        rx="18"
        ry="32"
        fill="none"
        stroke="currentColor"
        strokeWidth="1.4"
        strokeDasharray="4 5"
        opacity="0.16"
        transform="rotate(28 236 42)"
      />
      <circle cx="268" cy="42" r="3.5" fill="currentColor" opacity="0.3" />
    </>
  ),
  Appointment: () => (
    <>
      <rect x="198" y="12" width="64" height="58" rx="6" fill="none" stroke="currentColor" strokeWidth="1.4" opacity="0.22" />
      <rect x="198" y="12" width="64" height="14" fill="currentColor" opacity="0.2" />
      {[0, 1, 2].map((row) =>
        [0, 1, 2, 3].map((col) => (
          <circle
            key={`${row}-${col}`}
            cx={210 + col * 13}
            cy={38 + row * 10}
            r="1.7"
            fill="currentColor"
            opacity={row === 1 && col === 2 ? 0.4 : 0.16}
          />
        )),
      )}
    </>
  ),
  Season: () => (
    <>
      <path
        d="M250 14a28 28 0 1 0 0 56 22 22 0 1 1 0-56z"
        fill="currentColor"
        opacity="0.14"
      />
      <path
        d="M196 58c8-18 22-28 34-32-6 14-4 30 6 40-16 0-30-4-40-8z"
        fill="currentColor"
        opacity="0.12"
      />
    </>
  ),
};

export function NodeBackdrop({ type }: { type: SubjectType }) {
  const uid = useId().replace(/:/g, "");
  return <Frame>{TYPE_BACKDROPS[type](uid)}</Frame>;
}
