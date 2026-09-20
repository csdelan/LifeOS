import { afterEach, describe, expect, it } from "vitest";
import { isLiveApi } from "@/lib/api-base";
import { isSupabaseConfigured } from "@/lib/supabase";

describe("runtime public config", () => {
  afterEach(() => {
    window.__LIFEOS_PUBLIC_CONFIG__ = {};
  });

  it("treats runtime apiBaseUrl as live mode", () => {
    window.__LIFEOS_PUBLIC_CONFIG__ = { apiBaseUrl: "/" };
    expect(isLiveApi(undefined)).toBe(true);
  });

  it("reads supabase public values from window config", () => {
    window.__LIFEOS_PUBLIC_CONFIG__ = {
      supabaseUrl: "https://runtime.supabase.co",
      supabaseAnonKey: "runtime-anon",
    };
    expect(isSupabaseConfigured()).toBe(true);
  });
});
