import { measurementKeys } from "@/lib/features/measurements/keys";
import { meQueryKey } from "@/lib/me";

/**
 * Every cached read's key. A write refreshes a read by its key (`lib/write-effects.ts`), and a
 * key nested under another (`["inbox", "resolved"]` under `["inbox"]`) refreshes with it.
 */
export const qk = {
  ...measurementKeys,
  me: meQueryKey,
  subjects: ["subjects"] as const,
  subject: (id: string) => ["subject", id] as const,
  listItem: (id: string) => ["list-item", id] as const,
  relations: (id: string) => ["relations", id] as const,
  tags: (id: string) => ["tags", id] as const,
  tagUniverse: ["tag-universe"] as const,
  journal: (id: string) => ["journal", id] as const,
  history: (id: string) => ["history", id] as const,
  inbox: ["inbox"] as const,
  // Nested under ["inbox"], so every Inbox invalidation refreshes it too.
  inboxResolved: ["inbox", "resolved"] as const,
  artifact: (id: string) => ["artifact", id] as const,
  files: (id: string) => ["files", id] as const,
  areas: ["areas"] as const,
  people: ["people"] as const,
  // Every Connected agents read sits under ["agents"], so one invalidation refreshes them all.
  agents: ["agents"] as const,
  agentsOverview: ["agents", "overview"] as const,
  agentGrants: ["agents", "grants"] as const,
  agentCalls: (clientId: string) => ["agents", "calls", clientId] as const,
  habits: ["habits"] as const,
  occurrences: ["occurrences"] as const,
  timers: ["timers"] as const,
  // Every repeating-Task read sits under ["task-series"], so a status change refreshes them all.
  taskSeries: ["task-series"] as const,
  taskSeriesHistory: (id: string) => ["task-series", "history", id] as const,
  wallet: ["wallet"] as const,
  domains: ["domains"] as const,
  // Nested under ["domains"], so every domains invalidation refreshes the chart too.
  domainDays: (domain: string, days: number) => ["domains", "days", domain, days] as const,
  domainReport: (days: number) => ["domains", "report", days] as const,
  exceptionDays: (days: number) => ["domains", "exceptions", days] as const,
  food: ["food"] as const,
  foodDay: (date: string) => ["food", "day", date] as const,
  foodDays: (from: string, to: string) => ["food", "days", from, to] as const,
  foodRecent: ["food", "recent"] as const,
  // Every Activity read sits under ["activity"], so a sync, a connect or a note refreshes them all.
  activity: ["activity"] as const,
  activitySummary: ["activity", "summary"] as const,
  activityDays: (from: string, to: string) => ["activity", "days", from, to] as const,
  activityWorkouts: (from: string, to: string) => ["activity", "workouts", from, to] as const,
  activityTags: (from: string, to: string) => ["activity", "tags", from, to] as const,
  activityDay: (date: string) => ["activity", "day", date] as const,
  ouraStatus: ["activity", "oura"] as const,
  // Every Environment read sits under ["environment"], so a rating or a space write refreshes them all.
  environment: ["environment"] as const,
  environmentDays: (days: number) => ["environment", "days", days] as const,
  spaces: ["environment", "spaces"] as const,
  spaceRatings: (space: string, days: number) => ["environment", "ratings", space, days] as const,
  spacePhotos: (space: string, shot: string) => ["environment", "photos", space, shot] as const,
  ledger: ["ledger"] as const,
  rewards: ["rewards"] as const,
  bountyDefaults: ["bounty-defaults"] as const,
  coverSearch: (query: string) => ["cover-search", query] as const,
  reviews: ["reviews"] as const,
  recap: ["recap"] as const,
  dashboard: ["dashboard"] as const,
  // Under the dashboard, so every subject write that refreshes Focus refreshes these too.
  appointments: (on: string) => ["dashboard", "appointments", on] as const,
  edges: (archived: boolean) => ["edges", archived] as const,
};
