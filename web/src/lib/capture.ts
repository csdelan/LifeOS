export function deriveTitle(text: string, max = 80): string {
  const line = text.trim().split(/\n/)[0] ?? "";
  if (!line) return "Untitled";
  if (line.length <= max) return line;
  return `${line.slice(0, max - 1).trimEnd()}…`;
}
