import Image from "next/image";
import { GithubStarsButton } from "./GithubStarsButton";
import { AUTHOR_NAME, AUTHOR_URL } from "@/lib/content";

const TABS = [
  { label: "Features", href: "#features" },
  { label: "Compare", href: "#compare" },
  { label: "Quickstart", href: "#quickstart" },
];

export function Nav() {
  return (
    <header className="sticky top-0 z-50 border-b border-border bg-bg-deep/90 backdrop-blur">
      <div className="mx-auto flex h-14 max-w-6xl items-center gap-4 px-4 sm:px-6">
        <a href="#top" className="flex shrink-0 items-center gap-2">
          <Image
            src="/images/logo.png"
            alt="Flare.Net"
            width={22}
            height={22}
            className="size-[22px]"
            priority
          />
          <span className="text-sm font-semibold tracking-tight">
            Flare.Net
          </span>
        </a>

        <nav className="hidden items-center gap-1 sm:flex">
          {TABS.map((tab) => (
            <a
              key={tab.href}
              href={tab.href}
              className="rounded-md px-3 py-1.5 text-sm text-muted-foreground transition-colors hover:bg-secondary hover:text-foreground"
            >
              {tab.label}
            </a>
          ))}
        </nav>

        <div className="ml-auto flex items-center gap-3">
          <a
            href={AUTHOR_URL}
            target="_blank"
            rel="noopener noreferrer"
            className="hidden text-xs text-muted-foreground underline-offset-4 transition-colors hover:text-foreground hover:underline md:inline"
          >
            built by {AUTHOR_NAME} →
          </a>
          <GithubStarsButton />
        </div>
      </div>
    </header>
  );
}
