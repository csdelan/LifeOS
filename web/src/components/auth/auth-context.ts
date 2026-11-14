import { createContext, useContext } from "react";
import type { Session, User } from "@supabase/supabase-js";

/** Who is signed in, from AuthProvider; apart from it, so the provider's file holds only components. */
export type AuthContextValue = {
  session: Session | null;
  user: User | null;
  ready: boolean;
  signInWithGoogle: () => Promise<void>;
  signOut: () => Promise<void>;
};

export const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within AuthProvider");
  }
  return ctx;
}
