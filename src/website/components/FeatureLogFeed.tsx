import { TerminalWindow } from "./TerminalWindow";
import { LevelBadge } from "./LevelBadge";
import { FEATURES } from "@/lib/content";

export function FeatureLogFeed() {
  return (
    <section id="features" className="border-b border-border px-4 py-20 sm:px-6">
      <div className="mx-auto max-w-4xl">
        <p className="font-mono text-xs text-muted-foreground">{"// features"}</p>
        <h2 className="mt-2 text-2xl font-semibold tracking-tight text-foreground sm:text-3xl">
          Everything a log dashboard needs — nothing it shouldn&apos;t.
        </h2>

        <TerminalWindow title="tail -f flare.log" className="mt-8">
          <div>
            {FEATURES.map((feature) => (
              <div
                key={feature.title}
                className="border-b border-border/60 px-4 py-4 last:border-b-0 sm:px-6"
              >
                <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1.5 text-sm">
                  <span className="shrink-0 text-muted-foreground tabular-nums">
                    {feature.time}
                  </span>
                  <LevelBadge level={feature.level} />
                  <span className="shrink-0 text-muted-foreground">
                    {feature.service}
                  </span>
                  <span className="font-medium text-foreground">
                    {feature.title}
                  </span>
                </div>
                <p className="mt-1.5 pl-0 font-body text-sm leading-relaxed text-muted-foreground sm:pl-[calc(13ch+0.75rem)]">
                  {feature.body}
                </p>
              </div>
            ))}
          </div>
        </TerminalWindow>
      </div>
    </section>
  );
}
