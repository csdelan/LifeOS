import { generateHTML, generateJSON } from "@tiptap/core";
import StarterKit from "@tiptap/starter-kit";

/**
 * The journal's schema: what the editor can write, and all a stored entry may render as.
 * Anything else in an entry (a script, an event handler, a style, a javascript: link) has
 * no place in it and is dropped.
 */
export const journalExtensions = [
  StarterKit.configure({
    heading: { levels: [2, 3] },
    link: { openOnClick: false },
  }),
];

/**
 * True when an entry is HTML the editor wrote: it starts with one of the editor's block
 * tags. Plain text, including an agent's entry that mentions `<x>`, stays text.
 */
export function looksLikeHtml(content: string): boolean {
  return /^\s*<(p|h[1-6]|ul|ol|blockquote|pre|hr)[\s>/]/i.test(content);
}

/**
 * The entry re-rendered through the journal's schema, so it can be set as innerHTML.
 * Entries arrive from the API, and agents can write them through /mcp.
 */
export function sanitizeJournalHtml(html: string): string {
  return generateHTML(generateJSON(html, journalExtensions), journalExtensions);
}
