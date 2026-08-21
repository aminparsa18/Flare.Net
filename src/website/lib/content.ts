// Static content for the intro page. Timestamps are fixed strings (not
// generated at render time) so server and client markup always match —
// this page never needs real wall-clock time, only the *look* of one.

export const REPO_URL = "https://github.com/aminparsa18/Flare.Net";
export const REPO_SLUG = "aminparsa18/Flare.Net";
export const AUTHOR_NAME = "Amin Parsa";
export const AUTHOR_URL = "https://aminparsa.vercel.app";

export type Level = "info" | "warn" | "error";

export interface BootLine {
  level: Level;
  service: string;
  message: string;
}

export const BOOT_LINES: BootLine[] = [
  { level: "info", service: "flare-net", message: "booting..." },
  { level: "info", service: "flare-net", message: "OTLP receiver listening on :4317 (grpc) / :4318 (http)" },
  { level: "info", service: "flare-net", message: "ClickHouse pipeline connected, ingest ready" },
  { level: "warn", service: "flare-net", message: "auth disabled — the Logs page is open the moment it's up" },
];

export interface Feature {
  time: string;
  level: Level;
  service: string;
  title: string;
  body: string;
}

export const FEATURES: Feature[] = [
  {
    time: "10:31:11.244",
    level: "info",
    service: "logs",
    title: "search · filter · live-tail",
    body: "A virtualized logs explorer that streams as it happens — filter by service, level, or free-text query without losing your place.",
  },
  {
    time: "10:31:07.612",
    level: "info",
    service: "traces",
    title: "trace & metric correlation",
    body: "Jump from a log line to the trace it came from, and from the trace to the metrics around it — one dashboard, not three tabs.",
  },
  {
    time: "10:31:04.338",
    level: "warn",
    service: "alerts",
    title: "threshold & query-based alert rules",
    body: "Set a rule once, get notified on breach — webhook, Slack, Telegram, or email. No separate alerting product to wire up.",
  },
  {
    time: "10:31:00.815",
    level: "info",
    service: "ingest",
    title: "OTLP straight in, no agent daemon",
    body: "Point any OpenTelemetry exporter at Flare's OTLP endpoint directly. No collector process to install, configure, or babysit.",
  },
  {
    time: "10:30:57.606",
    level: "error",
    service: "license",
    title: "fully open source (MIT), self-hosted",
    body: "Not source-available, not a free tier of a paid product — the whole thing is MIT-licensed and runs on your own infrastructure.",
  },
];

export interface ComparisonTarget {
  name: string;
  highlight?: boolean;
  rows: { q: string; a: string }[];
}

export const COMPARISON: ComparisonTarget[] = [
  {
    name: "seq",
    rows: [
      { q: "open-source?", a: "no — commercial (free: 1 user)" },
      { q: "hosting?", a: "self-hosted" },
      { q: "ingest?", a: "CLEF + OTLP (logs/traces/metrics)" },
    ],
  },
  {
    name: "datadog-logs",
    rows: [
      { q: "open-source?", a: "no — closed-source SaaS" },
      { q: "hosting?", a: "SaaS only" },
      { q: "ingest?", a: "OTLP via Datadog Agent daemon" },
    ],
  },
  {
    name: "flare.net",
    highlight: true,
    rows: [
      { q: "open-source?", a: "yes — MIT, no catch" },
      { q: "hosting?", a: "self-hosted" },
      { q: "ingest?", a: "OTLP direct, no agent required" },
    ],
  },
];

export interface QuickstartPath {
  label: string;
  command: string;
  note: string;
  docHref: string;
}

export const QUICKSTART_PATHS: QuickstartPath[] = [
  {
    label: "Standalone",
    command: "docker compose up",
    note: "Working defaults for every port and credential.",
    docHref: `${REPO_URL}/blob/main/docs/standalone.md`,
  },
  {
    label: ".NET Aspire",
    command: "dotnet add package Flare.Hosting.Aspire",
    note: "Flare joins your AppHost as a resource.",
    docHref: `${REPO_URL}/blob/main/docs/aspire-hosting.md`,
  },
  {
    label: "Global CLI",
    command: "dotnet tool install --global Flare.Cli && flare start",
    note: "One shared instance across every local project.",
    docHref: `${REPO_URL}/blob/main/docs/cli.md`,
  },
];
