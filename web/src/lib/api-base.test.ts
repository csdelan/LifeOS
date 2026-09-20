import { describe, expect, it } from "vitest";
import { apiBaseUrl, isLiveApi } from "@/lib/api-base";

describe("api-base", () => {
  it("treats unset and empty as mock mode", () => {
    expect(isLiveApi(undefined)).toBe(false);
    expect(isLiveApi("")).toBe(false);
  });

  it("treats / as live same-origin", () => {
    expect(isLiveApi("/")).toBe(true);
    expect(apiBaseUrl("/")).toBe(window.location.origin);
  });

  it("strips a trailing slash from an absolute origin", () => {
    expect(isLiveApi("http://localhost:5280/")).toBe(true);
    expect(apiBaseUrl("http://localhost:5280/")).toBe("http://localhost:5280");
  });
});
