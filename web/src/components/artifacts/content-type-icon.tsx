import { createElement } from "react";
import type { LucideProps } from "lucide-react";
import { iconForContentType } from "@/lib/artifacts";

/**
 * The icon for a file's kind. Each kind's icon is one of lucide's, made once, so it's drawn
 * through createElement rather than named as a component during render, which would look to
 * React like a new component each time.
 */
export function ContentTypeIcon({ contentType, ...props }: { contentType: string } & LucideProps) {
  return createElement(iconForContentType(contentType), props);
}
