import { FootprintsIcon, Link2Icon, LockIcon, MoonIcon, DumbbellIcon } from "lucide-react";
import { Button } from "@/components/ui/button";
import type { OuraStatus } from "@/lib/production-ui-types";

const READS = [
  { icon: FootprintsIcon, text: "Your daily steps and activity" },
  { icon: DumbbellIcon, text: "Your workouts, so the ones you record count as exercise" },
  { icon: MoonIcon, text: "Your sleep and readiness scores" },
];

/**
 * Before connecting (ACTIVITY-1, activity plan §3.1, §3.7): what LifeOS will read from Oura,
 * that it's read-only, and Connect Oura. When the server has no Oura app it says what's missing
 * instead of offering a button that can't work.
 */
export function ConnectCard({
  status,
  busy,
  onConnect,
}: {
  status: OuraStatus;
  busy: boolean;
  onConnect: () => void;
}) {
  const configured = status.configured;
  return (
    <section
      aria-labelledby="connect-title"
      className="mx-auto max-w-lg space-y-4 rounded-xl bg-card p-5 ring-1 ring-foreground/10"
    >
      <h2 id="connect-title" className="font-heading text-xl">
        Connect Oura
      </h2>
      <p className="text-sm text-muted-foreground">
        LifeOS shows your steps, exercise, sleep and readiness from your Oura Ring. Connecting lets it read:
      </p>
      <ul className="space-y-2 text-sm">
        {READS.map(({ icon: Icon, text }) => (
          <li key={text} className="flex items-start gap-2">
            <Icon className="mt-0.5 size-4 shrink-0 text-primary" aria-hidden />
            {text}
          </li>
        ))}
      </ul>
      <p className="flex items-start gap-2 rounded-lg bg-muted p-3 text-sm">
        <LockIcon className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden />
        It's read-only: LifeOS never changes anything in Oura. The only thing you add here is a note on a workout,
        and that stays in LifeOS.
      </p>
      {configured ? (
        <Button className="min-h-11 w-full sm:w-auto" disabled={busy} onClick={onConnect}>
          <Link2Icon aria-hidden />
          {busy ? "Opening Oura…" : "Connect Oura"}
        </Button>
      ) : (
        <p role="status" className="text-sm text-muted-foreground">
          Oura isn't set up on this server yet: it needs <code>Oura__ClientId</code> and{" "}
          <code>Oura__ClientSecret</code> in its settings. Once they're there, reload this page.
        </p>
      )}
    </section>
  );
}
