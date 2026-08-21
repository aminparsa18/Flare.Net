import { cn } from "@/lib/utils";
import { COMPARISON } from "@/lib/content";
import { TerminalWindow } from "./TerminalWindow";

export function ComparisonStrip() {
  return (
    <section id="compare" className="border-b border-border px-4 py-20 sm:px-6">
      <div className="mx-auto max-w-5xl">
        <p className="font-mono text-xs text-muted-foreground">{"// compare"}</p>
        <h2 className="mt-2 text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
          Three sessions, one question each.
        </h2>
        <p className="mt-2 max-w-2xl font-body text-sm text-muted-foreground sm:text-base">
          Seq is built by Datalust — a small independent company, unrelated
          to Datadog despite the similar name.
        </p>

        <div className="mt-8 grid gap-4 sm:grid-cols-3">
          {COMPARISON.map((target) => (
            <TerminalWindow
              key={target.name}
              title={target.name}
              className={cn(
                target.highlight &&
                  "border-primary/60 shadow-[0_0_0_1px_var(--primary)]",
              )}
            >
              <div className="space-y-3 p-4 font-mono text-xs sm:text-[13px]">
                {target.rows.map((row) => (
                  <div key={row.q}>
                    <p className="text-muted-foreground">
                      <span className="text-primary">$</span> {row.q}
                    </p>
                    <p
                      className={cn(
                        "pl-3 text-foreground/90",
                        target.highlight && "text-primary",
                      )}
                    >
                      → {row.a}
                    </p>
                  </div>
                ))}
              </div>
            </TerminalWindow>
          ))}
        </div>
      </div>
    </section>
  );
}
