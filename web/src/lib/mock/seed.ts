import { addDays, nowIso, slugify, todayIso, startOfWeekSunday, endOfWeekSaturday } from "@/lib/dates";
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
  ReviewDoc,
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
  reviews: ReviewDoc[];
  primaryFocus: { title: string; subjectId: string; kind: "goal" };
  objectives: { id: string; title: string; subjectId?: string }[];
}

function isoAgo(hours: number): string {
  return new Date(Date.now() - hours * 3_600_000).toISOString();
}

function makeUrn(type: SubjectType, title: string, short: string): string {
  return `urn:bsk:${type.toLowerCase()}:${slugify(title)}-${short}`;
}

function weekday(iso: string): number {
  const [y, m, d] = iso.split("-").map(Number);
  return new Date(y, m - 1, d).getDay();
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
    due: s.due,
    targetDate: s.targetDate,
    expectedCadence: s.expectedCadence,
    nextReviewAt: s.nextReviewAt,
    createdAt: s.createdAt,
    archived: s.archived,
    area: s.area,
    areaName: s.areaName,
    ...extra,
  };
}

function areaOf(a: AreaRow) {
  return { area: a.urn, areaName: a.name };
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
      notes: "Training, sleep, food. If this slips, everything else gets louder.",
      createdAt: isoAgo(800),
    },
    {
      id: "area-family",
      urn: "urn:bsk:area:family-a10002",
      name: "Family",
      description: "Being present with the people I love.",
      notes: "Maya, Jonah, Mom. Presence over logistics — but logistics make presence possible.",
      createdAt: isoAgo(800),
    },
    {
      id: "area-dev",
      urn: "urn:bsk:area:dev-career-a10003",
      name: "Dev Career",
      description: "Building tools that compound.",
      notes: "LifeOS is the current craft. Deep work in the morning block.",
      createdAt: isoAgo(800),
    },
    {
      id: "area-trading",
      urn: "urn:bsk:area:trading-a10004",
      name: "Trading",
      description: "A durable practice, not a thrill.",
      notes: "Defined risk, written process, no heroics.",
      createdAt: isoAgo(800),
    },
  ];
  const [health, family, dev, trading] = areas;

  const values: SubjectListItem[] = [
    item({
      id: "value-steward",
      short: "v10001",
      type: "Value",
      title: "Steward of my health",
      ...areaOf(health),
      tags: "health,identity,body",
      createdAt: isoAgo(900),
    }),
    item({
      id: "value-father",
      short: "v10002",
      type: "Value",
      title: "Present with my family",
      ...areaOf(family),
      tags: "family,identity,presence",
      createdAt: isoAgo(880),
    }),
    item({
      id: "value-craft",
      short: "v10003",
      type: "Value",
      title: "Craftsman of systems",
      ...areaOf(dev),
      tags: "craft,identity,career",
      createdAt: isoAgo(860),
    }),
    item({
      id: "value-trader",
      short: "v10004",
      type: "Value",
      title: "I take only defined risk",
      ...areaOf(trading),
      tags: "trading,identity,risk",
      createdAt: isoAgo(840),
    }),
    item({
      id: "value-honest",
      short: "v10005",
      type: "Value",
      title: "I tell myself the truth",
      tags: "identity,honesty,journal",
      createdAt: isoAgo(820),
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
      ...areaOf(health),
      tags: "health,endurance,running",
    }),
    item({
      id: "goal-sleep",
      short: "g10006",
      type: "Goal",
      title: "Sleep through the night",
      status: "Active",
      targetDate: t(28),
      ...areaOf(health),
      tags: "health,sleep,recovery",
    }),
    item({
      id: "goal-lifeos",
      short: "g10002",
      type: "Goal",
      title: "Ship LifeOS production UI",
      status: "Active",
      targetDate: t(40),
      ...areaOf(dev),
      tags: "lifeos,focus,career",
    }),
    item({
      id: "goal-camp",
      short: "g10003",
      type: "Goal",
      title: "Family camping weekend",
      status: "New",
      targetDate: t(20),
      ...areaOf(family),
      tags: "family,outdoors,kids",
    }),
    item({
      id: "goal-dinners",
      short: "g10007",
      type: "Goal",
      title: "Sunday table, every week",
      status: "Active",
      targetDate: t(90),
      ...areaOf(family),
      tags: "family,presence,food",
    }),
    item({
      id: "goal-journal",
      short: "g10004",
      type: "Goal",
      title: "Trading journal discipline",
      status: "Active",
      targetDate: t(90),
      ...areaOf(trading),
      tags: "trading,craft,journal",
    }),
    item({
      id: "goal-archived",
      short: "g10005",
      type: "Goal",
      title: "Learn Italian (paused)",
      status: "Abandoned",
      archived: true,
      ...areaOf(family),
      tags: "learning,family",
      createdAt: isoAgo(2000),
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
      ...areaOf(health),
      tags: "health,training,running",
    }),
    item({
      id: "proj-sleep",
      short: "p10006",
      type: "Project",
      title: "Wind-down environment",
      status: "Active",
      targetDate: t(21),
      ...areaOf(health),
      tags: "health,sleep,home",
    }),
    item({
      id: "proj-web",
      short: "p10002",
      type: "Project",
      title: "Production web UI — first pass",
      status: "Active",
      targetDate: t(14),
      ...areaOf(dev),
      tags: "lifeos,deep-work,prototype",
    }),
    item({
      id: "proj-camp",
      short: "p10003",
      type: "Project",
      title: "Camping logistics",
      status: "New",
      targetDate: t(18),
      ...areaOf(family),
      tags: "family,home,outdoors",
    }),
    item({
      id: "proj-dinners",
      short: "p10007",
      type: "Project",
      title: "Sunday dinner ritual",
      status: "Active",
      ...areaOf(family),
      tags: "family,food,presence",
    }),
    item({
      id: "proj-trades",
      short: "p10004",
      type: "Project",
      title: "Daily trade review",
      status: "Active",
      ...areaOf(trading),
      tags: "trading,process",
    }),
    item({
      id: "proj-playbook",
      short: "p10008",
      type: "Project",
      title: "Written risk playbook",
      status: "Active",
      targetDate: t(35),
      ...areaOf(trading),
      tags: "trading,risk,process",
    }),
    item({
      id: "proj-standalone",
      short: "p10005",
      type: "Project",
      title: "Kitchen drawer rebuild",
      status: "New",
      ...areaOf(family),
      tags: "home,kitchen",
    }),
    item({
      id: "proj-archived",
      short: "p10009",
      type: "Project",
      title: "Newsletter reboot",
      status: "Abandoned",
      archived: true,
      ...areaOf(dev),
      tags: "career,writing",
      createdAt: isoAgo(1600),
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
      ...areaOf(health),
      tags: "health,endurance,running",
    }),
    item({
      id: "task-shoes",
      short: "t10002",
      type: "Task",
      title: "Buy race shoes",
      status: "Not started",
      due: t(-2),
      ...areaOf(health),
      tags: "health,gear",
    }),
    item({
      id: "task-nutrition",
      short: "t10003",
      type: "Task",
      title: "Write race-day nutrition plan",
      status: "In progress",
      due: t(5),
      ...areaOf(health),
      tags: "health,planning,nutrition",
    }),
    item({
      id: "task-phone",
      short: "t10010",
      type: "Task",
      title: "Park the phone in the kitchen at 9:30",
      status: "Not started",
      ...areaOf(health),
      tags: "health,sleep,home",
    }),
    item({
      id: "task-caffeine",
      short: "t10011",
      type: "Task",
      title: "Cut caffeine after 1pm this week",
      status: "In progress",
      due: t(4),
      ...areaOf(health),
      tags: "health,sleep,caffeine",
    }),
    item({
      id: "task-scaffold",
      short: "t10004",
      type: "Task",
      title: "Scaffold Focus screen",
      status: "In progress",
      scheduled: today,
      ...areaOf(dev),
      tags: "lifeos,deep-work",
    }),
    item({
      id: "task-types",
      short: "t10005",
      type: "Task",
      title: "Mirror production-ui-types.ts",
      status: "Completed",
      due: t(-1),
      ...areaOf(dev),
      tags: "lifeos",
    }),
    item({
      id: "task-graph",
      short: "t10012",
      type: "Task",
      title: "Ship the Map graph view",
      status: "In progress",
      due: t(3),
      scheduled: today,
      ...areaOf(dev),
      tags: "lifeos,deep-work,prototype",
    }),
    item({
      id: "task-campground",
      short: "t10006",
      type: "Task",
      title: "Reserve campground",
      status: "Not started",
      due: t(2),
      ...areaOf(family),
      tags: "family,outdoors",
    }),
    item({
      id: "task-callmom",
      short: "t10008",
      type: "Task",
      title: "Call Mom after dinner",
      status: "Not started",
      scheduled: today,
      ...areaOf(family),
      tags: "family",
    }),
    item({
      id: "task-menu",
      short: "t10013",
      type: "Task",
      title: "Plan Sunday menu with Jonah",
      status: "Not started",
      due: t(6),
      ...areaOf(family),
      tags: "family,food,kids",
    }),
    item({
      id: "task-trades",
      short: "t10007",
      type: "Task",
      title: "Review last week's trades",
      status: "Not started",
      due: today,
      ...areaOf(trading),
      tags: "trading,journal",
    }),
    item({
      id: "task-playbook",
      short: "t10014",
      type: "Task",
      title: "Write the three hard rules",
      status: "Waiting",
      due: t(8),
      ...areaOf(trading),
      tags: "trading,risk,planning",
    }),
    item({
      id: "task-orphan",
      short: "t10009",
      type: "Task",
      title: "Return library books",
      status: "Not started",
      due: t(3),
      tags: "home,books",
    }),
    item({
      id: "task-archived",
      short: "t10015",
      type: "Task",
      title: "Order Italian workbook",
      status: "Cancelled",
      archived: true,
      ...areaOf(family),
      tags: "learning",
      createdAt: isoAgo(1800),
    }),
  ];

  const extras: SubjectListItem[] = [
    item({
      id: "idea-desk",
      short: "i10001",
      type: "Idea",
      title: "Standing desk experiment",
      status: "New",
      ...areaOf(dev),
      tags: "health,home,career",
    }),
    item({
      id: "idea-cookbook",
      short: "i10002",
      type: "Idea",
      title: "Family cookbook",
      status: "New",
      ...areaOf(family),
      tags: "family,food",
    }),
    item({
      id: "idea-winddown",
      short: "i10003",
      type: "Idea",
      title: "Wind-down without screens",
      status: "Promoted",
      ...areaOf(health),
      tags: "health,sleep",
    }),
    item({
      id: "prob-sleep",
      short: "r10001",
      type: "Problem",
      title: "How do I sleep through the night?",
      status: "Working",
      ...areaOf(health),
      tags: "health,sleep",
    }),
    item({
      id: "dec-stack",
      short: "d10001",
      type: "Decision",
      title: "React + Vite for Production UI",
      status: "Implementing",
      ...areaOf(dev),
      tags: "lifeos,career",
    }),
    item({
      id: "dec-blazor",
      short: "d10002",
      type: "Decision",
      title: "Blazor for Production UI",
      status: "Closed",
      ...areaOf(dev),
      tags: "lifeos",
      createdAt: isoAgo(400),
    }),
    item({
      id: "cmt-inbox",
      short: "c10001",
      type: "Commitment",
      title: "Reach Inbox Zero each weekday",
      status: "Open",
      ...areaOf(dev),
      tags: "focus,process",
    }),
    item({
      id: "cns-kernel",
      short: "k10001",
      type: "Constraint",
      title: "Write path stays in LifeOs.Application",
      ...areaOf(dev),
      tags: "lifeos,architecture",
    }),
    item({
      id: "person-maya",
      short: "ppl001",
      type: "Person",
      title: "Maya",
      personKind: "human",
      ...areaOf(family),
      tags: "family,partner",
    }),
    item({
      id: "person-mom",
      short: "ppl002",
      type: "Person",
      title: "Mom",
      personKind: "human",
      ...areaOf(family),
      tags: "family",
    }),
    item({
      id: "person-jonah",
      short: "ppl004",
      type: "Person",
      title: "Jonah",
      personKind: "human",
      ...areaOf(family),
      tags: "family,kids",
    }),
    item({
      id: "person-sam",
      short: "ppl005",
      type: "Person",
      title: "Sam",
      personKind: "human",
      ...areaOf(health),
      tags: "health,running",
    }),
    item({
      id: "person-agent",
      short: "ppl003",
      type: "Person",
      title: "Cursor Grok",
      personKind: "ai",
      ...areaOf(dev),
      tags: "career,agents",
    }),
    item({
      id: "person-compass",
      short: "ppl006",
      type: "Person",
      title: "Compass",
      personKind: "ai",
      ...areaOf(dev),
      tags: "career,agents,planning",
    }),
    item({
      id: "appt-run",
      short: "ap1001",
      type: "Appointment",
      title: "Park loop with Sam",
      status: "Scheduled",
      due: today,
      scheduled: today,
      ...areaOf(health),
      tags: "health,running",
    }),
    item({
      id: "appt-dinner",
      short: "ap1002",
      type: "Appointment",
      title: "Family dinner",
      status: "Scheduled",
      due: today,
      scheduled: today,
      ...areaOf(family),
      tags: "family,food",
    }),
    item({
      id: "appt-camp",
      short: "ap1003",
      type: "Appointment",
      title: "Campground office call",
      status: "Scheduled",
      due: t(2),
      ...areaOf(family),
      tags: "family,outdoors",
    }),
    item({
      id: "appt-premarket",
      short: "ap1004",
      type: "Appointment",
      title: "Pre-market review",
      status: "Scheduled",
      due: t(1),
      ...areaOf(trading),
      tags: "trading,process",
    }),
  ];

  let subjects = [...values, ...goals, ...projects, ...tasks, ...extras];

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
  details["value-trader"].statement =
    "I'm the type of person who risks only what I've defined in writing.";
  details["value-honest"].statement =
    "I'm the type of person who tells myself the unflattering truth before anyone else has to.";
  details["goal-half"].scope = "Finish a half-marathon without injury, joyfully.";
  details["goal-sleep"].scope = "Wake once or not at all; feel recovered before the kettle boils.";
  details["goal-lifeos"].scope = "A design-forward production UI that a real person would live in.";
  details["goal-camp"].scope = "One unhurried weekend outside, with Jonah in the water.";
  details["goal-dinners"].scope = "Sunday evening at the table, phones away, every week.";
  details["goal-journal"].scope = "Every session written before the next one begins.";
  details["proj-web"].scope = "A look-and-feel prototype against mock data, tree-first, peek-not-route.";
  details["proj-sleep"].scope = "Make the last hour of the day boring on purpose.";
  details["proj-playbook"].scope = "Three hard rules on one page. If it isn't written, it isn't a rule.";
  details["dec-stack"].scope = "React + TypeScript + Vite. Kernel stays C#.";
  details["dec-blazor"].scope = "Superseded: sharing C# types was not worth the UI ceiling.";
  details["cns-kernel"].scope = "The browser never writes the store. Application services are the only write path.";
  details["prob-sleep"].scope = "3:40am wakeups, four nights out of seven. Caffeine and screens are the suspects.";
  details["idea-winddown"].scope = "Phone lives in the kitchen after 9:30. Paper book, dim lights.";
  details["appt-run"].scope = "Easy conversational pace. Sam at the north gate, 7:00.";
  details["appt-dinner"].scope = "Maya cooking; Jonah sets the table. No laptops.";

  function bag(id: string, attrs: Record<string, string>) {
    const d = details[id];
    if (!d) return;
    details[id] = { ...d, attributes: JSON.stringify(attrs) };
  }
  bag("value-steward", {
    statement: details["value-steward"].statement ?? "",
    why_it_matters: "If the body fails, every other identity gets louder.",
    notes: "Training, sleep, food.",
  });
  bag("value-father", {
    statement: details["value-father"].statement ?? "",
    why_it_matters: "Presence is the actual gift.",
  });
  bag("value-craft", {
    statement: details["value-craft"].statement ?? "",
    why_it_matters: "Tools that compound outlive a career.",
  });
  bag("value-trader", {
    statement: details["value-trader"].statement ?? "",
    why_it_matters: "Undefined risk is a story I tell myself.",
  });
  bag("value-honest", {
    statement: details["value-honest"].statement ?? "",
    why_it_matters: "The journal is where the unflattering truth lands first.",
  });
  bag("goal-half", {
    desired_end_state: details["goal-half"].scope ?? "",
    target_date: goals.find((g) => g.id === "goal-half")?.targetDate ?? "",
    description: "A joyful half, not a heroics half.",
    motivation: "Prove the body is an instrument, not a project.",
  });
  bag("goal-sleep", {
    desired_end_state: details["goal-sleep"].scope ?? "",
    target_date: goals.find((g) => g.id === "goal-sleep")?.targetDate ?? "",
    motivation: "Mornings are for craft, not recovery theater.",
  });
  bag("goal-lifeos", {
    desired_end_state: details["goal-lifeos"].scope ?? "",
    target_date: goals.find((g) => g.id === "goal-lifeos")?.targetDate ?? "",
    description: "Command center, not a wiki.",
    motivation: "I want to live in the tool I am building.",
  });
  bag("proj-web", {
    description: details["proj-web"].scope ?? "",
    start_date: t(-20),
    target_date: projects.find((p) => p.id === "proj-web")?.targetDate ?? "",
    notes: "Tree-first, peek-not-route.",
  });
  bag("proj-base", {
    description: "Aerobic base, easy long runs, no heroics.",
    start_date: t(-40),
    target_date: projects.find((p) => p.id === "proj-base")?.targetDate ?? "",
  });
  bag("task-longrun", {
    description: "Easy conversational pace with Sam.",
    due: today,
    scheduled: today,
    priority: "Medium",
    estimated_duration: "90m",
  });
  bag("task-shoes", {
    description: "Current pair is cooked.",
    due: t(-2),
    priority: "High",
  });
  bag("task-phone", {
    description: "The experiment fails if the phone is on the nightstand.",
    priority: "Medium",
  });
  bag("prob-sleep", {
    description: details["prob-sleep"].scope ?? "",
    date_identified: t(-30),
    impact: "Split nights wreck the morning deep-work block.",
  });
  bag("dec-stack", {
    description: details["dec-stack"].scope ?? "",
    decision_date: t(-25),
  });
  bag("idea-desk", { description: "Lower back after long coding days is getting loud." });
  bag("cmt-inbox", {
    description: "Triage until empty or 15 minutes, each weekday.",
    due: today,
    recurrence: JSON.stringify({ kind: "weekly", weekdays: [1, 2, 3, 4, 5] }),
  });
  bag("person-maya", {
    person_kind: "human",
    role: "partner",
    description: "Sees the whole board.",
  });
  bag("person-agent", {
    person_kind: "ai",
    role: "coding agent",
    contact: "cursor",
  });
  bag("appt-run", {
    date: today,
    start: "07:00",
    end: "08:00",
    location: "North gate",
    attendees: "person-sam",
    all_day: "false",
  });
  bag("appt-dinner", {
    date: today,
    start: "18:30",
    end: "20:00",
    location: "Home",
    attendees: "person-maya,person-jonah",
    all_day: "false",
  });

  const edges: CanonicalEdge[] = [
    // Health
    { fromId: "goal-half", relation: "serves", toId: "value-steward" },
    { fromId: "goal-sleep", relation: "serves", toId: "value-steward" },
    { fromId: "proj-base", relation: "results_in", toId: "goal-half" },
    { fromId: "proj-sleep", relation: "results_in", toId: "goal-sleep" },
    { fromId: "task-longrun", relation: "serves", toId: "proj-base" },
    { fromId: "task-shoes", relation: "serves", toId: "proj-base" },
    { fromId: "task-nutrition", relation: "serves", toId: "proj-base" },
    { fromId: "task-nutrition", relation: "serves", toId: "goal-half" },
    { fromId: "task-phone", relation: "serves", toId: "proj-sleep" },
    { fromId: "task-caffeine", relation: "serves", toId: "proj-sleep" },
    { fromId: "prob-sleep", relation: "serves", toId: "value-steward" },
    { fromId: "idea-winddown", relation: "serves", toId: "prob-sleep" },
    { fromId: "proj-sleep", relation: "results_in", toId: "idea-winddown" },
    // Family
    { fromId: "goal-camp", relation: "serves", toId: "value-father" },
    { fromId: "goal-dinners", relation: "serves", toId: "value-father" },
    { fromId: "proj-camp", relation: "results_in", toId: "goal-camp" },
    { fromId: "proj-dinners", relation: "results_in", toId: "goal-dinners" },
    { fromId: "task-campground", relation: "serves", toId: "proj-camp" },
    { fromId: "task-callmom", relation: "serves", toId: "goal-camp" },
    { fromId: "task-menu", relation: "serves", toId: "proj-dinners" },
    { fromId: "task-archived", relation: "serves", toId: "goal-archived" },
    // Dev career
    { fromId: "goal-lifeos", relation: "serves", toId: "value-craft" },
    { fromId: "proj-web", relation: "results_in", toId: "goal-lifeos" },
    { fromId: "proj-web", relation: "serves", toId: "dec-stack" },
    { fromId: "task-scaffold", relation: "serves", toId: "proj-web" },
    { fromId: "task-types", relation: "serves", toId: "proj-web" },
    { fromId: "task-graph", relation: "serves", toId: "proj-web" },
    { fromId: "task-graph", relation: "serves", toId: "goal-lifeos" },
    { fromId: "cmt-inbox", relation: "serves", toId: "value-craft" },
    { fromId: "dec-stack", relation: "results_in", toId: "goal-lifeos" },
    { fromId: "dec-stack", relation: "serves", toId: "cns-kernel" },
    { fromId: "dec-stack", relation: "supersedes", toId: "dec-blazor" },
    { fromId: "goal-lifeos", relation: "serves", toId: "value-honest" },
    // Trading
    { fromId: "goal-journal", relation: "serves", toId: "value-trader" },
    { fromId: "goal-journal", relation: "serves", toId: "value-honest" },
    { fromId: "proj-trades", relation: "results_in", toId: "goal-journal" },
    { fromId: "proj-playbook", relation: "results_in", toId: "goal-journal" },
    { fromId: "task-trades", relation: "serves", toId: "proj-trades" },
    { fromId: "task-playbook", relation: "serves", toId: "proj-playbook" },
  ];

  const tagsByItem: Record<string, string[]> = {};
  for (const s of subjects) {
    tagsByItem[s.id] = s.tags
      ? s.tags.split(",").map((tag) => tag.trim()).filter(Boolean)
      : [];
  }

  const journals: Record<string, JournalEntry[]> = {
    "goal-half": [
      {
        eventId: "j-1",
        occurredAt: isoAgo(48),
        content:
          "Week 4 long run felt easy at conversational pace. Keep the shoes search honest — current pair is cooked.",
      },
      {
        eventId: "j-1b",
        occurredAt: isoAgo(120),
        content: "Sam asked if Saturday can start at 7 instead of 6:30. Yes. Joy > heroics.",
      },
    ],
    "goal-lifeos": [
      {
        eventId: "j-4",
        occurredAt: isoAgo(4),
        content:
          "The graph has to be the same data as the outline or it will lie. Layered first, force as a second lens.",
      },
      {
        eventId: "j-4b",
        occurredAt: isoAgo(22),
        content: "Primary focus stays this until the first-pass UI is something I'd actually live in.",
      },
    ],
    "proj-web": [
      {
        eventId: "j-2",
        occurredAt: isoAgo(6),
        content:
          "First-pass UI should feel like a command center, not a wiki. Tree-first, peek-not-route. The graph has to be the same data as the outline or it will lie.",
      },
      {
        eventId: "j-2b",
        occurredAt: isoAgo(28),
        content: "Blazor would have shared types. It would also have capped the graph. Closed that door on purpose.",
      },
    ],
    "prob-sleep": [
      {
        eventId: "j-3",
        occurredAt: isoAgo(30),
        content: "Woke at 3:40 again. Caffeine after 1pm is still the likely culprit.",
      },
      {
        eventId: "j-3b",
        occurredAt: isoAgo(78),
        content: "Phone was on the nightstand. That's the experiment failing, not the body.",
      },
    ],
  };

  const history: Record<string, StatusHistoryEntry[]> = {
    "goal-half": [
      { id: "h-1", status: "New", occurredAt: isoAgo(900) },
      { id: "h-2", status: "Active", occurredAt: isoAgo(720) },
    ],
    "goal-lifeos": [
      { id: "h-7", status: "New", occurredAt: isoAgo(200) },
      { id: "h-8", status: "Active", occurredAt: isoAgo(160) },
    ],
    "goal-sleep": [
      { id: "h-9", status: "New", occurredAt: isoAgo(140) },
      { id: "h-10", status: "Active", occurredAt: isoAgo(90) },
    ],
    "task-scaffold": [
      { id: "h-3", status: "Not started", occurredAt: isoAgo(20) },
      { id: "h-4", status: "In progress", occurredAt: isoAgo(4) },
    ],
    "task-types": [
      { id: "h-5", status: "Not started", occurredAt: isoAgo(30) },
      { id: "h-6", status: "Completed", occurredAt: isoAgo(8) },
    ],
    "task-graph": [
      { id: "h-11", status: "Not started", occurredAt: isoAgo(16) },
      { id: "h-12", status: "In progress", occurredAt: isoAgo(3) },
    ],
    "prob-sleep": [
      { id: "h-13", status: "Open", occurredAt: isoAgo(200) },
      { id: "h-14", status: "Working", occurredAt: isoAgo(72) },
    ],
    "dec-stack": [
      { id: "h-15", status: "Open", occurredAt: isoAgo(180) },
      { id: "h-16", status: "Implementing", occurredAt: isoAgo(150) },
    ],
    "dec-blazor": [
      { id: "h-17", status: "Open", occurredAt: isoAgo(400) },
      { id: "h-18", status: "Cancelled", occurredAt: isoAgo(160) },
      { id: "h-19", status: "Closed", occurredAt: isoAgo(158) },
    ],
    "proj-web": [
      { id: "h-20", status: "New", occurredAt: isoAgo(170) },
      { id: "h-21", status: "Active", occurredAt: isoAgo(150) },
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
    "prob-sleep": [
      {
        kind: "observation",
        occurredAt: isoAgo(10),
        eventId: "e-2",
        content: "Woke at 3:38. Phone was not in the kitchen.",
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
      subjectUrn: extras.find((x) => x.id === "idea-cookbook")?.urn,
      subjectType: "Idea",
      subjectTitle: "Family cookbook",
    },
    {
      itemId: "inbox-3",
      itemKind: "subject",
      triagedAt: isoAgo(18),
      subjectUrn: extras.find((x) => x.id === "prob-sleep")?.urn,
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
    {
      itemId: "inbox-6",
      itemKind: "event",
      triagedAt: isoAgo(80),
      eventKind: "note",
      eventContent: "Refinance? Rates moved. Don't decide from a headline — park it.",
    },
    {
      itemId: "inbox-7",
      itemKind: "event",
      triagedAt: isoAgo(34),
      eventKind: "observation",
      eventContent: "Evening stretch has been unrecorded more nights than not. The cue isn't firing.",
    },
    {
      itemId: "inbox-8",
      itemKind: "event",
      triagedAt: isoAgo(52),
      eventKind: "note",
      eventContent: "Jonah asked to go fishing on the camping trip. Pack the cheap rods.",
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
      currentStreak: 0,
      lastState: "unrecorded",
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
      currentStreak: 0,
      lastState: "unrecorded",
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
    {
      id: "habit-journal",
      urn: "urn:bsk:habit:trade-journal-h10004",
      name: "Trade journal",
      cue: "Session closed",
      routine: "Write the plan vs. the tape, three lines minimum",
      reward: "Tomorrow's self has a chance",
      startDate: t(-60),
      allowsPartial: true,
      recurrence: "weekdays",
      archived: false,
      createdAt: isoAgo(500),
      currentStreak: 0,
      lastState: "unrecorded",
    },
  ];

  const occurrences: HabitOccurrenceRow[] = [
    ...habitDays(habits[0], today, 24, (ago) => {
      if (ago === 0) return "unrecorded";
      if (ago <= 12) return "followed";
      if (ago === 13) return "partial";
      if (ago === 14) return "not_followed";
      return ago % 6 === 0 ? "partial" : "followed";
    }),
    ...habitDays(habits[1], today, 24, (ago) => {
      if (ago === 0) return "unrecorded";
      if (ago <= 4) return "followed";
      if (ago === 5) return "not_followed";
      if (ago === 8) return "partial";
      return ago % 5 === 0 ? "not_followed" : "followed";
    }),
    ...habitDays(habits[2], today, 21, (ago) => {
      if (ago === 0) return "unrecorded";
      if (ago === 2) return "unrecorded";
      if (ago === 5) return "followed";
      if (ago === 9) return "partial";
      return ago % 3 === 0 ? "partial" : "not_followed";
    }),
    ...habitDays(
      habits[3],
      today,
      28,
      (ago) => {
        if (ago === 0) return "unrecorded";
        if (ago <= 3) return "followed";
        if (ago === 6) return "not_followed";
        if (ago === 11) return "partial";
        return ago % 7 === 0 ? "not_followed" : "followed";
      },
      { weekdaysOnly: true },
    ),
  ];

  for (const habit of habits) {
    const mine = occurrences
      .filter((o) => o.habitId === habit.id)
      .sort((a, b) => b.occurrenceDate.localeCompare(a.occurrenceDate));
    const todayRow = mine.find((o) => o.occurrenceDate === today);
    habit.lastState = todayRow?.state ?? mine[0]?.state ?? "unrecorded";
    let streak = 0;
    for (const row of mine) {
      if (row.occurrenceDate === today) continue;
      if (row.state === "followed") streak += 1;
      else break;
    }
    habit.currentStreak = streak;
  }

  const habitItems: SubjectListItem[] = habits.map((h, i) =>
    item({
      id: h.id,
      short: `h1000${i + 1}`,
      type: "Habit",
      title: h.name,
      archived: h.archived,
      createdAt: h.createdAt,
      ...areaOf(
        h.id === "habit-journal" ? trading : h.id === "habit-inbox" ? dev : health,
      ),
      tags: h.id === "habit-journal" ? "trading,journal" : "health,habit",
    }),
  );
  subjects = [...subjects, ...habitItems];
  for (const s of habitItems) {
    const h = habits.find((x) => x.id === s.id)!;
    details[s.id] = detailFrom(s, {
      attributes: JSON.stringify({
        cue: h.cue ?? "",
        routine: h.routine ?? "",
        reward: h.reward ?? "",
        start: h.startDate ?? "",
        end: h.endDate ?? "",
        allows_partial: h.allowsPartial ? "true" : "false",
        recurrence: h.recurrence ?? "daily",
      }),
    });
    tagsByItem[s.id] = s.tags ? s.tags.split(",").map((tag) => tag.trim()).filter(Boolean) : [];
  }
  edges.push(
    { fromId: "habit-mobility", relation: "serves", toId: "goal-half" },
    { fromId: "habit-mobility", relation: "serves", toId: "value-steward" },
    { fromId: "habit-inbox", relation: "serves", toId: "value-craft" },
    { fromId: "habit-stretch", relation: "serves", toId: "goal-sleep" },
    { fromId: "habit-journal", relation: "serves", toId: "goal-journal" },
  );

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
      role: "family",
      archived: false,
    },
    {
      id: "person-jonah",
      urn: "urn:bsk:person:jonah-ppl004",
      title: "Jonah",
      personKind: "human",
      role: "son",
      archived: false,
    },
    {
      id: "person-sam",
      urn: "urn:bsk:person:sam-ppl005",
      title: "Sam",
      personKind: "human",
      role: "running partner",
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
    {
      id: "person-compass",
      urn: "urn:bsk:person:compass-ppl006",
      title: "Compass",
      personKind: "ai",
      role: "planning agent",
      archived: false,
    },
  ];

  const associations: PersonAssociationRow[] = [
    {
      subjectId: "goal-camp",
      subjectUrn: goals.find((g) => g.id === "goal-camp")!.urn,
      subjectType: "Goal",
      subjectTitle: "Family camping weekend",
      role: "involves",
      personId: "person-maya",
      personUrn: people[0].urn,
      personName: "Maya",
    },
    {
      subjectId: "goal-camp",
      subjectUrn: goals.find((g) => g.id === "goal-camp")!.urn,
      subjectType: "Goal",
      subjectTitle: "Family camping weekend",
      role: "involves",
      personId: "person-jonah",
      personUrn: people[2].urn,
      personName: "Jonah",
    },
    {
      subjectId: "proj-web",
      subjectUrn: projects.find((p) => p.id === "proj-web")!.urn,
      subjectType: "Project",
      subjectTitle: "Production web UI — first pass",
      role: "assignee",
      personId: "person-agent",
      personUrn: people[4].urn,
      personName: "Cursor Grok",
    },
    {
      subjectId: "task-graph",
      subjectUrn: tasks.find((x) => x.id === "task-graph")!.urn,
      subjectType: "Task",
      subjectTitle: "Ship the Map graph view",
      role: "assignee",
      personId: "person-agent",
      personUrn: people[4].urn,
      personName: "Cursor Grok",
    },
    {
      subjectId: "task-longrun",
      subjectUrn: tasks.find((x) => x.id === "task-longrun")!.urn,
      subjectType: "Task",
      subjectTitle: "Long run Saturday",
      role: "involves",
      personId: "person-sam",
      personUrn: people[3].urn,
      personName: "Sam",
    },
    {
      subjectId: "appt-run",
      subjectUrn: extras.find((x) => x.id === "appt-run")!.urn,
      subjectType: "Appointment",
      subjectTitle: "Park loop with Sam",
      role: "attendee",
      personId: "person-sam",
      personUrn: people[3].urn,
      personName: "Sam",
    },
    {
      subjectId: "appt-dinner",
      subjectUrn: extras.find((x) => x.id === "appt-dinner")!.urn,
      subjectType: "Appointment",
      subjectTitle: "Family dinner",
      role: "attendee",
      personId: "person-maya",
      personUrn: people[0].urn,
      personName: "Maya",
    },
    {
      subjectId: "appt-dinner",
      subjectUrn: extras.find((x) => x.id === "appt-dinner")!.urn,
      subjectType: "Appointment",
      subjectTitle: "Family dinner",
      role: "attendee",
      personId: "person-jonah",
      personUrn: people[2].urn,
      personName: "Jonah",
    },
    {
      subjectId: "task-callmom",
      subjectUrn: tasks.find((x) => x.id === "task-callmom")!.urn,
      subjectType: "Task",
      subjectTitle: "Call Mom after dinner",
      role: "involves",
      personId: "person-mom",
      personUrn: people[1].urn,
      personName: "Mom",
    },
    {
      subjectId: "goal-lifeos",
      subjectUrn: goals.find((g) => g.id === "goal-lifeos")!.urn,
      subjectType: "Goal",
      subjectTitle: "Ship LifeOS production UI",
      role: "involves",
      personId: "person-compass",
      personUrn: people[5].urn,
      personName: "Compass",
    },
  ];

  const yesterday = addDays(today, -1);
  const weekStart = startOfWeekSunday(today);
  const weekEnd = endOfWeekSaturday(today);
  const reviews: ReviewDoc[] = [
    {
      id: `review-daily-${yesterday}`,
      kind: "daily",
      date: yesterday,
      weekStart,
      weekEnd,
      completedAt: isoAgo(18),
      missed: false,
      workedWell: "Easy long-run pace. The graph and the outline stayed the same data.",
      differently: "Phone was still on the nightstand. Park it before the kettle.",
      planning: "Keep LifeOS as primary focus. Finish Map graph. Long run stays easy.",
      keepFocus: true,
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
    reviews,
    primaryFocus: {
      title: "Ship LifeOS production UI",
      subjectId: "goal-lifeos",
      kind: "goal",
    },
    objectives: [
      { id: "obj-1", title: "Land the Map graph beside the outline", subjectId: "task-graph" },
      { id: "obj-2", title: "Clear Inbox before noon" },
      { id: "obj-3", title: "Long run, easy pace", subjectId: "task-longrun" },
    ],
  };
}

function habitDays(
  habit: HabitRow,
  today: string,
  span: number,
  stateAt: (daysAgo: number) => HabitOccurrenceRow["state"],
  opts?: { weekdaysOnly?: boolean },
): HabitOccurrenceRow[] {
  const rows: HabitOccurrenceRow[] = [];
  for (let ago = span - 1; ago >= 0; ago--) {
    const occurrenceDate = addDays(today, -ago);
    if (opts?.weekdaysOnly) {
      const day = weekday(occurrenceDate);
      if (day === 0 || day === 6) continue;
    }
    rows.push({
      habitId: habit.id,
      habitUrn: habit.urn,
      habitName: habit.name,
      occurrenceDate,
      state: stateAt(ago),
      allowsPartial: habit.allowsPartial,
    });
  }
  return rows;
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
