"use client";

import { useEffect, useRef, useState } from "react";
import { Check, Copy } from "lucide-react";
import { TerminalWindow } from "./TerminalWindow";
import { LevelBadge } from "./LevelBadge";
import { GithubStarsButton } from "./GithubStarsButton";
import { BOOT_LINES } from "@/lib/content";

// boot lines + headline + subheadline + cta row
const STEP_COUNT = BOOT_LINES.length + 3;
const STEP_DELAY_MS = 220;

function CopyCommand({ command }: { command: string }) {
  const [copied, setCopied] = useState(false);
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | undefined>(
    undefined,
  );

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(command);
      setCopied(true);
      clearTimeout(timeoutRef.current);
      timeoutRef.current = setTimeout(() => setCopied(false), 1600);
    } catch {
      /* clipboard API unavailable — nothing to fall back to, stay quiet */
    }
  };

  return (
    <button
      type="button"
      onClick={copy}
      className="group inline-flex items-center gap-2 rounded-md border border-border bg-bg-deep px-4 py-2.5 font-mono text-sm text-foreground transition-colors hover:border-primary/50 cursor-pointer"
      aria-label={`Copy command: ${command}`}
    >
      <span className="text-primary">$</span>
      {command}
      {copied ? (
        <Check className="size-4 text-primary" aria-hidden="true" />
      ) : (
        <Copy
          className="size-4 text-muted-foreground group-hover:text-foreground"
          aria-hidden="true"
        />
      )}
    </button>
  );
}

export function Hero() {
  const [visible, setVisible] = useState(STEP_COUNT);

  useEffect(() => {
    const prefersReduced = window.matchMedia(
      "(prefers-reduced-motion: reduce)",
    ).matches;
    if (prefersReduced) return;

    // Every setVisible call happens inside this scheduled callback (never
    // synchronously in the effect body) — including the initial reset to 0.
    let step = 0;
    let timeoutId: ReturnType<typeof setTimeout>;
    const tick = () => {
      setVisible(step);
      step += 1;
      if (step <= STEP_COUNT) {
        timeoutId = setTimeout(tick, STEP_DELAY_MS);
      }
    };
    timeoutId = setTimeout(tick, 0);

    return () => clearTimeout(timeoutId);
  }, []);

  const bootDone = visible >= BOOT_LINES.length;
  const stepClass = (index: number) =>
    index < visible
      ? "opacity-100 translate-y-0"
      : "opacity-0 translate-y-1 pointer-events-none";

  return (
    <section
      id="top"
      className="border-b border-border bg-bg-deep px-4 pt-14 pb-20 sm:px-6 sm:pt-20 sm:pb-28"
    >
      <div className="mx-auto max-w-4xl">
        <TerminalWindow title="flare — zsh" className="bg-card/60">
          <div className="p-5 sm:p-8">
            <div className="space-y-1.5 font-mono text-xs sm:text-sm">
              {BOOT_LINES.map((line, i) => (
                <div
                  key={line.message}
                  className={`flex flex-wrap items-baseline gap-x-2 transition-all duration-300 ${stepClass(i)}`}
                >
                  <LevelBadge level={line.level} className="text-[10px]" />
                  <span className="text-muted-foreground">
                    {line.service}:
                  </span>
                  <span className="text-foreground/90">{line.message}</span>
                </div>
              ))}
              {!bootDone && (
                <span
                  className="inline-block h-3.5 w-1.5 animate-pulse bg-primary align-middle"
                  aria-hidden="true"
                />
              )}
            </div>

            <h1
              className={`mt-6 text-balance text-2xl font-semibold leading-snug tracking-tight text-foreground transition-all duration-300 sm:text-3xl ${stepClass(BOOT_LINES.length)}`}
            >
              <span className="text-muted-foreground">
                [INFO] flare-net:
              </span>{" "}
              A self-hosted, OpenTelemetry-native log dashboard for .NET.
            </h1>

            <p
              className={`mt-3 max-w-2xl text-pretty font-body text-base text-muted-foreground transition-all duration-300 sm:text-lg ${stepClass(BOOT_LINES.length + 1)}`}
            >
              Think Seq or Datadog Logs — but fully open source (MIT),
              self-hosted, and OTLP straight in with no agent daemon to
              install.
            </p>

            <div
              className={`mt-7 flex flex-col gap-3 transition-all duration-300 sm:flex-row sm:items-center ${stepClass(BOOT_LINES.length + 2)}`}
            >
              <CopyCommand command="docker compose up" />
              <GithubStarsButton size="lg" />
            </div>
          </div>
        </TerminalWindow>
      </div>
    </section>
  );
}
