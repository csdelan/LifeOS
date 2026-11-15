/**
 * The colours a card or a detail's section can take, by what it holds (index.css, --tone-teal):
 * teal is the item and its purpose, and the default; blue when things happen and how they
 * connect; amber rewards and money; violet what repeats and reflection; green health and
 * growth; ember what needs attention; rose people. A surface says its tone with `data-tone`.
 */
export const TONES = ["teal", "blue", "amber", "violet", "green", "ember", "rose"] as const;

export type Tone = (typeof TONES)[number];
