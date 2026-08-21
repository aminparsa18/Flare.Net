import { cn } from "@/lib/utils";
import type { Level } from "@/lib/content";

const LEVEL_LABEL: Record<Level, string> = {
  info: "Information",
  warn: "Warning",
  error: "Error",
};

const LEVEL_CLASS: Record<Level, string> = {
  info: "bg-secondary text-foreground",
  warn: "bg-level-warn/15 text-level-warn",
  error: "bg-level-error/15 text-level-error",
};

export function LevelBadge({
  level,
  className,
}: {
  level: Level;
  className?: string;
}) {
  return (
    <span
      className={cn(
        "inline-flex shrink-0 items-center rounded-md px-2 py-0.5 text-xs font-medium",
        LEVEL_CLASS[level],
        className,
      )}
    >
      {LEVEL_LABEL[level]}
    </span>
  );
}
