import { describe, expect, it } from "vitest";
import { looksLikeHtml, sanitizeJournalHtml } from "./journal-html";

describe("sanitizeJournalHtml", () => {
  it("keeps what the editor writes", () => {
    const html =
      '<h2>Week</h2><p><strong>Good</strong> and <em>calm</em>, <a href="https://example.com">link</a></p><ul><li><p>one</p></li></ul>';
    const out = sanitizeJournalHtml(html);
    expect(out).toContain("<h2>Week</h2>");
    expect(out).toContain("<strong>Good</strong>");
    expect(out).toContain("<em>calm</em>");
    expect(out).toContain('href="https://example.com"');
    expect(out).toContain("<li><p>one</p></li>");
  });

  it("drops scripts, handlers, styles and javascript: links", () => {
    const out = sanitizeJournalHtml(
      '<p onclick="steal()" style="position:fixed">hi<script>steal()</script><img src=x onerror="steal()"><a href="javascript:steal()">x</a></p><iframe src="https://evil.example"></iframe>',
    );
    expect(out).not.toMatch(/script|onclick|onerror|style=|<img|<iframe|javascript:/i);
    expect(out).toContain("hi");
  });
});

describe("looksLikeHtml", () => {
  it("is the editor's HTML", () => {
    expect(looksLikeHtml("<p>hi</p>")).toBe(true);
    expect(looksLikeHtml("  <h2>Week</h2>")).toBe(true);
  });

  it("isn't plain text that mentions a tag", () => {
    expect(looksLikeHtml("Use <x> as the placeholder")).toBe(false);
    expect(looksLikeHtml("a < b and c > d")).toBe(false);
  });
});
