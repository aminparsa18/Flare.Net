import { ArrowUpRight } from "lucide-react";
import { TerminalWindow } from "./TerminalWindow";
import { QUICKSTART_PATHS } from "@/lib/content";

export function Quickstart() {
  return (
    <section id="quickstart" className="border-b border-border px-4 py-20 sm:px-6">
      <div className="mx-auto max-w-4xl">
        <p className="font-mono text-xs text-muted-foreground">{"// quickstart"}</p>
        <h2 className="mt-2 text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
          Pick your path.
        </h2>

        <div className="mt-8 grid gap-4 sm:grid-cols-3">
          {QUICKSTART_PATHS.map((path) => (
            <TerminalWindow key={path.label} title={path.label}>
              <div className="flex h-full flex-col gap-3 p-4">
                <code className="block break-words rounded-md bg-bg-deep px-3 py-2 text-xs text-primary">
                  {path.command}
                </code>
                <p className="font-body text-sm text-muted-foreground">
                  {path.note}
                </p>
                <a
                  href={path.docHref}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="mt-auto inline-flex items-center gap-1 text-sm text-foreground underline-offset-4 hover:text-primary hover:underline"
                >
                  Docs
                  <ArrowUpRight className="size-3.5" aria-hidden="true" />
                </a>
              </div>
            </TerminalWindow>
          ))}
        </div>

        <p className="mt-6 font-body text-sm text-muted-foreground">
          Whichever path you pick, the dashboard comes up at{" "}
          <code className="text-foreground">localhost:7777</code>.
        </p>
      </div>
    </section>
  );
}
