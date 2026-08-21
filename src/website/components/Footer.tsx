import { AUTHOR_NAME, AUTHOR_URL, REPO_URL } from "@/lib/content";
import { LevelBadge } from "./LevelBadge";
import { GithubStarsButton } from "./GithubStarsButton";

const STATUS_LINES: { label: string; href: string; text: string }[] = [
  { label: "source", href: REPO_URL, text: "github.com/aminparsa18/Flare.Net" },
  { label: "license", href: `${REPO_URL}/blob/main/LICENSE`, text: "MIT" },
  { label: "built-by", href: AUTHOR_URL, text: `${AUTHOR_NAME} → aminparsa.vercel.app` },
];

export function Footer() {
  return (
    <footer className="bg-bg-deep px-4 py-14 sm:px-6">
      <div className="mx-auto max-w-4xl">
        <div className="overflow-hidden rounded-lg border border-border bg-card/60 font-mono text-sm">
          {STATUS_LINES.map((line) => (
            <a
              key={line.label}
              href={line.href}
              target="_blank"
              rel="noopener noreferrer"
              className="flex flex-wrap items-baseline gap-x-3 gap-y-1 border-b border-border/60 px-4 py-3 transition-colors last:border-b-0 hover:bg-secondary/40"
            >
              <LevelBadge level="info" />
              <span className="shrink-0 text-muted-foreground">
                {line.label}:
              </span>
              <span className="text-foreground underline-offset-4 hover:underline">
                {line.text}
              </span>
            </a>
          ))}
        </div>

        <div className="mt-6 flex flex-col items-start justify-between gap-4 sm:flex-row sm:items-center">
          <p className="font-body text-xs text-muted-foreground">
            © {new Date().getFullYear()} Flare.Net · MIT licensed
          </p>
          <GithubStarsButton />
        </div>
      </div>
    </footer>
  );
}
