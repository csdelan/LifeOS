import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { TypeBadge } from "@/components/primitives/type-badge";
import type { SubjectType } from "@/lib/production-ui-types";

describe("TypeIcon", () => {
  it("draws a plain glyph for a type the web doesn't know, not a crash", () => {
    const { container } = render(<TypeBadge type={"Space" as SubjectType} />);
    expect(container).toHaveTextContent("Space");
    expect(container.querySelector("svg")).toBeInTheDocument();
  });
});
