import { addDays, nowIso, slugify, todayIso } from "@/lib/dates";
import type {
  AreaRow,
  ConcerningEvent,
  HabitOccurrenceRow,
  HabitRow,
  InboxItem,
  JournalEntry,
  PersonAssociationRow,
  PersonRow,
  Relation,
  RelationEdge,
  StatusHistoryEntry,
  SubjectDetail,
  SubjectListItem,
  SubjectType,
  TagUniverseItem,
} from "@/lib/production-ui-types";
import { defaultStatus } from "@/lib/production-ui-types";

export interface CanonicalEdge {
  fromId: string;
  relation: Relation;
  toId: string;
}

export interface MockState {
  subjects: SubjectListItem[];
  details: Record<string, SubjectDetail>;
  edges: CanonicalEdge[];
  tagsByItem: Record<string, string[]>;
  journals: Record<string, JournalEntry[]>;
  history: Record<string, StatusHistoryEntry[]>;
  concerning: Record<string, ConcerningEvent[]>;
  inbox: InboxItem[];
  areas: AreaRow[];
  habits: HabitRow[];
  occurrences: HabitOccurrenceRow[];
  people: PersonRow[];
  associations: PersonAssociationRow[];
  primaryFocus: { title: string; subjectId: string; kind: "goal" };
  objectives: { id: string; title: string; subjectId?: string }[];
}

function isoAgo(hours: number): string {
  return new Date(Date.now() - hours * 3_600_000).toISOString();
}

function makeUrn(type: SubjectType, title: string, short: string): string {
  return `urn:bsk:${type.toLowerCase()}:${slugify(title)}-${short}`;
}

function item(
  partial: Omit<SubjectListItem, "archived" | "createdAt" | "urn"> & {
    archived?: boolean;
    createdAt?: string;
    urn?: string;
    short: string;
  },
): SubjectListItem {
  const { short, ...rest } = partial;
  return {
    archived: false,
    createdAt: isoAgo(240),
    urn: makeUrn(partial.type, partial.title, short),
    ...rest,
  };
}

function detailFrom(s: SubjectListItem, extra: Partial<SubjectDetail> = {}): SubjectDetail {
  return {
    id: s.id,
    urn: s.urn,
    type: s.type,
    title: s.title,
    status: s.status,
    due: s.due ?? s.targetDate,
    expectedCadence: s.expectedCadence,
    nextReviewAt: s.nextReviewAt,
    createdAt: s.createdAt,
    archived: s.archived,
    area: s.area,
    areaName: s.areaName,
    ...extra,
  };
}

export function createSeed(): MockState {
  const today = todayIso();
  const t = (offset: number) => addDays(today, offset);

  const areas: AreaRow[] = [
    {
      id: "area-health",
      urn: "urn:bsk:area:health-a10001",
      name: "Health",
      description: "Body as the instrument of a long life.",
      notes: "Training, sleep, food.",
      createdAt: isoAgo(800),
    },
    {
      id: "area-family",
      urn: "urn:bsk:area:family-a10002",
      name: "Family",
      description: "Being present with the people I love.",
      createdAt: isoAgo(800),
    },
    {
      id: "area-dev",
      urn: "urn:bsk:area:dev-career-a10003",
      name: "Dev Career",
      description: "Building tools that compound.",
      createdAt: isoAgo(800),
    },
    {
      id: "area-trading",
      urn: "urn:bsk:area:trading-a10004",
      name: "Trading",
      description: "A durable practice, not a thrill.",
      createdAt: isoAgo(800),
    },
  ];

  const values: SubjectListItem[] = [
    item({
      id: "value-steward",
      short: "v10001",
      type: "Value",
      title: "Steward of my health",
      area: areas[0].urn,
      areaName: "Health",
      tags: "health,identity",
    }),
    item({
      id: "value-father",
      short: "v10002",
      type: "Value",
      title: "Present father",
      area: areas[1].urn,
      areaName: "Family",
      tags: "family,identity",
    }),
    item({
      id: "value-craft",
      short: "v10003",
      type: "Value",
      title: "Craftsman of systems",
      area: areas[2].urn,
      areaName: "Dev Career",
      tags: "craft,identity",
    }),
  ];

  const goals: SubjectListItem[] = [
    item({
      id: "goal-half",
      short: "g10001",
      type: "Goal",
      title: "Run a half-marathon",
      status: "Active",
      targetDate: t(56),
      area: areas[0].urn,
      areaName: "Health",
      tags: "health,endurance",
    }),
    item({
      id: "goal-lifeos",
      short: "g10002",
      type: "Goal",
      title: "Ship LifeOS production UI",
      status: "Active",
      targetDate: t(40),
      area: areas[2].urn,
      areaName: "Dev Career",
      tags: "lifeos,focus",
    }),
    item({
      id: "goal-camp",
      short: "g10003",
      type: "Goal",
      title: "Family camping weekend",
      status: "New",
      targetDate: t(20),
      area: areas[1].urn,
      areaName: "Family",
      tags: "family,outdoors",
    }),
    item({
      id: "goal-journal",
      short: "g10004",
      type: "Goal",
      title: "Trading journal discipline",
      status: "Active",
      targetDate: t(90),
      area: areas[3].urn,
      areaName: "Trading",
      tags: "trading,craft",
    }),
    item({
      id: "goal-archived",
      short: "g10005",
      type: "Goal",
      title: "Learn Italian (paused)",
      status: "Abandoned",
      archived: true,
      area: areas[1].urn,
      areaName: "Family",
      tags: "learning",
    }),
  ];

  const projects: SubjectListItem[] = [
    item({
      id: "proj-base",
      short: "p10001",
      type: "Project",
      title: "12-week base build",
      status: "Active",
      targetDate: t(56),
      area: areas[0].urn,
      areaName: "Health",
      tags: "health,training",
    }),
    item({
      id: "proj-web",
      short: "p10002",
      type: "Project",
      title: "Production web UI — first pass",
      status: "Active",
      targetDate: t(14),
      area: areas[2].urn,
      areaName: "Dev Career",
      tags: "lifeos,deep-work",
    }),
    item({
      id: "proj-camp",
      short: "p10003",
      type: "Project",
      title: "Camping logistics",
      status: "New",
      targetDate: t(18),
      area: areas[1].urn,
      areaName: "Family",
      tags: "family,home",
    }),
    item({
      id: "proj-trades",
      short: "p10004",
      type: "Project",
      title: "Daily trade review",
      status: "Active",
      area: areas[3].urn,
      areaName: "Trading",
      tags: "trading",
    }),
    item({
      id: "proj-standalone",
      short: "p10005",
      type: "Project",
      title: "Kitchen drawer rebuild",
      status: "New",
      area: areas[1].urn,
      areaName: "Family",
      tags: "home",
    }),
  ];

  const tasks: SubjectListItem[] = [
    item({
      id: "task-longrun",
      short: "t10001",
      type: "Task",
      title: "Long run Saturday",
      status: "Not started",
      due: today,
      scheduled: today,
      area: areas[0].urn,
      areaName: "Health",
      tags: "health,endurance",
    }),
    item({
      id: "task-shoes",
      short: "t10002",
      type: "Task",
      title: "Buy race shoes",
      status: "Not started",
      due: t(-2),
      area: areas[0].urn,
      areaName: "Health",
      tags: "health",
    }),
    item({
      id: "task-nutrition",
      short: "t10003",
      type: "Task",
      title: "Write race-day nutrition plan",
      status: "In progress",
      due: t(5),
      area: areas[0].urn,
      areaName: "Health",
      tags: "health,planning",
    }),
    item({
      id: "task-scaffold",
      short: "t10004",
      type: "Task",
      title: "Scaffold Focus screen",
      status: "In progress",
      scheduled: today,
      area: areas[2].urn,
      areaName: "Dev Career",
      tags: "lifeos,deep-work",
    }),
    item({
      id: "task-types",
      short: "t10005",
      type: "Task",
      title: "Mirror production-ui-types.ts",
      status: "Completed",
      due: t(-1),
      area: areas[2].urn,
      areaName: "Dev Career",
      tags: "lifeos",
    }),
    item({
      id: "task-campground",
      short: "t10006",
      type: "Task",
      title: "Reserve campground",
      status: "Not started",
      due: t(2),
      area: areas[1].urn,
      areaName: "Family",
      tags: "family",
    }),
    item({
      id: "task-trades",
      short: "t10007",
      type: "Task",
      title: "Review last week's trades",
      status: "Not started",
      due: today,
      area: areas[3].urn,
      areaName: "Trading",
      tags: "trading",
    }),
    item({
      id: "task-callmom",
      short: "t10008",
      type: "Task",
      title: "Call Mom after dinner",
      status: "Not started",
      scheduled: today,
      area: areas[1].urn,
      areaName: "Family",
      tags: "family",
    }),
    item({
      id: "task-orphan",
      short: "t10009",
      type: "Task",
      title: "Return library books",
      status: "Not started",
      due: t(3),
      tags: "home",
    }),
  ];

  const extras: SubjectListItem[] = [
    item({
      id: "idea-desk",
      short: "i10001",
      type: "Idea",
      title: "Standing desk experiment",
      status: "New",
      area: areas[2].urn,
      areaName: "Dev Career",
      tags: "health,home",
    }),
    item({
      id: "idea-cookbook",
      short: "i10002",
      type: "Idea",
      title: "Family cookbook",
      status: "New",
      area: areas[1].urn,
      areaName: "Family",
      tags: "family",
    }),
    item({
      id: "prob-sleep",
      short: "r10001",
      type: "Problem",
      title: "How do I sleep through the night?",
      status: "Open",
      area: areas[0].urn,
      areaName: "Health",
      tags: "health,sleep",
    }),
    item({
      id: "dec-stack",
      short: "d10001",
      type: "Decision",
      title: "React + Vite for Production UI",
      status: "Implementing",
      area: areas[2].urn,
      areaName: "Dev Career",
      tags: "lifeos",
    }),
    item({
      id: "cmt-inbox",
      short: "c10001",
      type: "Commitment",
      title: "Reach Inbox Zero each weekday",
      status: "Open",
      area: areas[2].urn,
      areaName: "Dev Career",
      tags: "focus",
    }),
    item({
      id: "person-maya",
      short: "ppl001",
      type: "Person",
      title: "Maya",
      personKind: "human",
      area: areas[1].urn,
      areaName: "Family",
    }),
    item({
      id: "person-mom",
      short: "ppl002",
      type: "Person",
      title: "Mom",
      personKind: "human",
      area: areas[1].urn,
      areaName: "Family",
    }),
    item({
      id: "person-agent",
      short: "ppl003",
      type: "Person",
      title: "Cursor Grok",
      personKind: "ai",
      area: areas[2].urn,
      areaName: "Dev Career",
    }),
  ];

  const subjects = [...values, ...goals, ...projects, ...tasks, ...extras];

  const details: Record<string, SubjectDetail> = {};
  for (const s of subjects) {
    details[s.id] = detailFrom(s);
  }
  details["value-steward"].statement =
    "I'm the type of person who treats my body as the instrument of a long life.";
  details["value-father"].statement =
    "I'm the type of person who is fully there when I'm with my family.";
  details["value-craft"].statement =
    "I'm the type of person who builds tools that compound.";
  details["goal-half"].scope = "Finish a half-marathon without injury, joyfully.";
  details["proj-web"].scope = "A design-forward look-and-feel prototype against mock data.";

  const edges: CanonicalEdge[] = [
    { fromId: "goal-half", relation: "serves", toId: "value-steward" },
    { fromId: "goal-lifeos", relation: "serves", toId: "value-craft" },
    { fromId: "goal-camp", relation: "serves", toId: "value-father" },
    { fromId: "goal-journal", relation: "serves", toId: "value-craft" },
    { fromId: "proj-base", relation: "results_in", toId: "goal-half" },
    { fromId: "proj-web", relation: "results_in", toId: "goal-lifeos" },
    { fromId: "proj-camp", relation: "results_in", toId: "goal-camp" },
    { fromId: "proj-trades", relation: "results_in", toId: "goal-journal" },
    { fromId: "task-longrun", relation: "serves", toId: "proj-base" },
    { fromId: "task-shoes", relation: "serves", toId: "proj-base" },
    { fromId: "task-nutrition", relation: "serves", toId: "proj-base" },
    { fromId: "task-nutrition", relation: "serves", toId: "goal-half" },
    { fromId: "task-scaffold", relation: "serves", toId: "proj-web" },
    { fromId: "task-types", relation: "serves", toId: "proj-web" },
    { fromId: "task-campground", relation: "serves", toId: "proj-camp" },
    { fromId: "task-trades", relation: "serves", toId: "proj-trades" },
    { fromId: "task-callmom", relation: "serves", toId: "goal-camp" },
    { fromId: "cmt-inbox", relation: "serves", toId: "value-craft" },
    { fromId: "dec-stack", relation: "results_in", toId: "goal-lifeos" },
  ];

  const tagsByItem: Record<string, string[]> = {};
  for (const s of subjects) {
    tagsByItem[s.id] = s.tags
      ? s.tags.split(",").map((t) => t.trim()).filter(Boolean)
      : [];
  }

  const journals: Record<string, JournalEntry[]> = {
    "goal-half": [
      {
        eventId: "j-1",
        occurredAt: isoAgo(48),
        content: "Week 4 long run felt easy at conversational pace. Keep the shoes search honest — current pair is cooked.",
      },
    ],
    "proj-web": [
      {
        eventId: "j-2",
        occurredAt: isoAgo(6),
        content: "First-pass UI should feel like a command center, not a wiki. Tree-first, peek-not-route.",
      },
    ],
    "prob-sleep": [
      {
        eventId: "j-3",
        occurredAt: isoAgo(30),
        content: "Woke at 3:40 again. Caffeine after 1pm is still the likely culprit.",
      },
    ],
  };

  const history: Record<string, StatusHistoryEntry[]> = {
    "goal-half": [
      { id: "h-1", status: "New", occurredAt: isoAgo(900) },
      { id: "h-2", status: "Active", occurredAt: isoAgo(720) },
    ],
    "task-scaffold": [
      { id: "h-3", status: "Not started", occurredAt: isoAgo(20) },
      { id: "h-4", status: "In progress", occurredAt: isoAgo(4) },
    ],
    "task-types": [
      { id: "h-5", status: "Not started", occurredAt: isoAgo(30) },
      { id: "h-6", status: "Completed", occurredAt: isoAgo(8) },
    ],
  };

  const concerning: Record<string, ConcerningEvent[]> = {
    "goal-half": [
      {
        kind: "observation",
        occurredAt: isoAgo(26),
        eventId: "e-1",
        content: "Resting HR trending down this week.",
      },
    ],
  };

  const inbox: InboxItem[] = [
    {
      itemId: "inbox-1",
      itemKind: "event",
      triagedAt: isoAgo(2),
      eventKind: "note",
      eventContent: "Maybe a standing desk? Lower back after long coding days is getting loud.",
    },
    {
      itemId: "inbox-2",
      itemKind: "subject",
      triagedAt: isoAgo(10),
      subjectUrn: extras[1].urn,
      subjectType: "Idea",
      subjectTitle: "Family cookbook",
    },
    {
      itemId: "inbox-3",
      itemKind: "subject",
      triagedAt: isoAgo(18),
      subjectUrn: extras[2].urn,
      subjectType: "Problem",
      subjectTitle: "How do I sleep through the night?",
    },
    {
      itemId: "inbox-4",
      itemKind: "event",
      triagedAt: isoAgo(5),
      eventKind: "observation",
      eventContent: "Camping logistics has no next action and the weekend is 20 days out.",
    },
    {
      itemId: "inbox-5",
      itemKind: "event",
      triagedAt: isoAgo(1),
      eventKind: "note",
      eventContent: "Ask Maya if Sunday dinner can move so the long run isn't rushed.",
    },
  ];

  const habits: HabitRow[] = [
    {
      id: "habit-mobility",
      urn: "urn:bsk:habit:morning-mobility-h10001",
      name: "Morning mobility",
      cue: "Kettle boiled",
      routine: "8 minutes of hips, spine, shoulders",
      reward: "Coffee tastes earned",
      startDate: t(-80),
      allowsPartial: true,
      recurrence: "daily",
      archived: false,
      createdAt: isoAgo(800),
      currentStreak: 12,
      lastState: "followed",
    },
    {
      id: "habit-inbox",
      urn: "urn:bsk:habit:inbox-zero-pass-h10002",
      name: "Inbox Zero pass",
      cue: "After morning planning",
      routine: "Triage until empty or 15 minutes",
      reward: "A clear queue",
      startDate: t(-40),
      allowsPartial: true,
      recurrence: "daily",
      archived: false,
      createdAt: isoAgo(400),
      currentStreak: 4,
      lastState: "followed",
    },
    {
      id: "habit-stretch",
      urn: "urn:bsk:habit:evening-stretch-h10003",
      name: "Evening stretch",
      cue: "Lights dimmed",
      routine: "Hamstrings + breath, 5 minutes",
      allowsPartial: true,
      recurrence: "daily",
      archived: false,
      createdAt: isoAgo(200),
      currentStreak: 0,
      lastState: "unrecorded",
    },
  ];

  const occurrences: HabitOccurrenceRow[] = [
    {
      habitId: "habit-mobility",
      habitUrn: habits[0].urn,
      habitName: "Morning mobility",
      occurrenceDate: today,
      state: "unrecorded",
      allowsPartial: true,
    },
    {
      habitId: "habit-inbox",
      habitUrn: habits[1].urn,
      habitName: "Inbox Zero pass",
      occurrenceDate: today,
      state: "unrecorded",
      allowsPartial: true,
    },
    {
      habitId: "habit-stretch",
      habitUrn: habits[2].urn,
      habitName: "Evening stretch",
      occurrenceDate: today,
      state: "unrecorded",
      allowsPartial: true,
    },
  ];

  const people: PersonRow[] = [
    {
      id: "person-maya",
      urn: "urn:bsk:person:maya-ppl001",
      title: "Maya",
      personKind: "human",
      role: "partner",
      archived: false,
    },
    {
      id: "person-mom",
      urn: "urn:bsk:person:mom-ppl002",
      title: "Mom",
      personKind: "human",
      archived: false,
    },
    {
      id: "person-agent",
      urn: "urn:bsk:person:cursor-grok-ppl003",
      title: "Cursor Grok",
      personKind: "ai",
      role: "coding agent",
      archived: false,
    },
  ];

  const associations: PersonAssociationRow[] = [
    {
      subjectId: "goal-camp",
      subjectUrn: goals[2].urn,
      subjectType: "Goal",
      subjectTitle: goals[2].title,
      role: "involves",
      personId: "person-maya",
      personUrn: people[0].urn,
      personName: "Maya",
    },
    {
      subjectId: "proj-web",
      subjectUrn: projects[1].urn,
      subjectType: "Project",
      subjectTitle: projects[1].title,
      role: "assignee",
      personId: "person-agent",
      personUrn: people[2].urn,
      personName: "Cursor Grok",
    },
  ];

  return {
    subjects,
    details,
    edges,
    tagsByItem,
    journals,
    history,
    concerning,
    inbox,
    areas,
    habits,
    occurrences,
    people,
    associations,
    primaryFocus: {
      title: "Ship LifeOS production UI",
      subjectId: "goal-lifeos",
      kind: "goal",
    },
    objectives: [
      { id: "obj-1", title: "Land the Map outline with inline create", subjectId: "proj-web" },
      { id: "obj-2", title: "Clear Inbox before noon" },
      { id: "obj-3", title: "Long run, easy pace", subjectId: "task-longrun" },
    ],
  };
}

export function toRelationEdge(
  other: SubjectListItem,
  relation: Relation,
): RelationEdge {
  return {
    relation,
    urn: other.urn,
    type: other.type,
    subjectId: other.id,
    title: other.title,
  };
}

export function foldedStatus(type: SubjectType, status?: string | null): string {
  return status ?? defaultStatus(type);
}

export function tagUniverse(state: MockState): TagUniverseItem[] {
  const counts = new Map<string, number>();
  for (const tags of Object.values(state.tagsByItem)) {
    for (const tag of tags) {
      counts.set(tag, (counts.get(tag) ?? 0) + 1);
    }
  }
  return [...counts.entries()]
    .map(([tag, itemCount]) => ({ tag, itemCount }))
    .sort((a, b) => a.tag.localeCompare(b.tag));
}

export { nowIso };
