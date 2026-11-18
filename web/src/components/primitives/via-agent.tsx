import { BotIcon } from "lucide-react";
import type { AgentVia } from "@/lib/production-ui-types";
import { cn } from "@/lib/utils";

/**
 * "via Claude": the agent that wrote a row through `/mcp`, by the AI-agent Person it acts as
 * (agent-access plan §5.6 step 2.4). Nothing when Chris did.
 */
export function ViaAgent({ via, className }: { via?: AgentVia | null; className?: string }) {
  if (!via) return null;
  return (
    <span className={cn("whitespace-nowrap", className)}>
      <BotIcon aria-hidden className="mr-0.5 inline size-3 align-[-0.125em]" />
      via {via.name}
    </span>
  );
}
