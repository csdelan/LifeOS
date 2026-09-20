import { useTheme } from "next-themes";
import { MoonIcon, ShieldXIcon, SunIcon } from "lucide-react";
import { Button } from "@/components/ui/button";

function AuthChrome({ children }: { children: React.ReactNode }) {
  const { resolvedTheme, setTheme } = useTheme();
  return (
    <div className="flex min-h-svh flex-col bg-background text-foreground">
      <div className="flex justify-end p-3">
        <Button
          variant="ghost"
          size="icon"
          aria-label={resolvedTheme === "dark" ? "Switch to light theme" : "Switch to dark theme"}
          onClick={() => setTheme(resolvedTheme === "dark" ? "light" : "dark")}
        >
          {resolvedTheme === "dark" ? <SunIcon /> : <MoonIcon />}
        </Button>
      </div>
      <div className="flex flex-1 flex-col items-center justify-center px-6 pb-24">
        {children}
      </div>
    </div>
  );
}

function Mark() {
  return (
    <span className="flex size-12 items-center justify-center rounded-xl bg-primary font-heading text-2xl text-primary-foreground">
      ⌘
    </span>
  );
}

export function AuthSplash() {
  return (
    <AuthChrome>
      <Mark />
      <p className="mt-6 font-heading text-3xl">LifeOS</p>
      <p className="mt-2 text-sm text-muted-foreground">Checking your session…</p>
    </AuthChrome>
  );
}

export function SignInScreen({ onSignIn }: { onSignIn: () => Promise<void> }) {
  return (
    <AuthChrome>
      <Mark />
      <h1 className="mt-6 font-heading text-4xl">LifeOS</h1>
      <p className="mt-2 max-w-sm text-center text-sm text-muted-foreground">
        A private command center. Sign in with the Google account this instance allows.
      </p>
      <Button
        className="mt-8 min-w-52"
        size="lg"
        onClick={() => {
          void onSignIn();
        }}
      >
        Sign in with Google
      </Button>
    </AuthChrome>
  );
}

export function UnauthorizedScreen({
  email,
  onSignOut,
}: {
  email?: string;
  onSignOut: () => void;
}) {
  return (
    <AuthChrome>
      <ShieldXIcon className="size-10 text-destructive" aria-hidden />
      <h1 className="mt-6 font-heading text-3xl">This account isn&apos;t authorized</h1>
      <p className="mt-2 max-w-sm text-center text-sm text-muted-foreground">
        {email
          ? `${email} is signed in, but it is not on the allowlist for this LifeOS instance.`
          : "This Google account is not on the allowlist for this LifeOS instance."}
      </p>
      <Button className="mt-8" variant="outline" onClick={onSignOut}>
        Sign out
      </Button>
    </AuthChrome>
  );
}

export function AuthConfigError() {
  return (
    <AuthChrome>
      <Mark />
      <h1 className="mt-6 font-heading text-3xl">Missing Auth configuration</h1>
      <p className="mt-2 max-w-md text-center text-sm text-muted-foreground">
        Live mode needs <code className="font-mono text-xs">VITE_SUPABASE_URL</code> and{" "}
        <code className="font-mono text-xs">VITE_SUPABASE_ANON_KEY</code> (local Vite env, or{" "}
        <code className="font-mono text-xs">fly secrets set</code> on the API container).
      </p>
    </AuthChrome>
  );
}

export function AuthReachError({
  message,
  onRetry,
}: {
  message: string;
  onRetry: () => void;
}) {
  return (
    <AuthChrome>
      <Mark />
      <h1 className="mt-6 font-heading text-3xl">Can&apos;t reach the API</h1>
      <p className="mt-2 max-w-sm text-center text-sm text-muted-foreground">{message}</p>
      <Button className="mt-8" variant="outline" onClick={onRetry}>
        Try again
      </Button>
    </AuthChrome>
  );
}
