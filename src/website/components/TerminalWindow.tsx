import { cn } from "@/lib/utils";

export function TerminalWindow({
  title,
  children,
  className,
  bodyClassName,
}: {
  title: string;
  children: React.ReactNode;
  className?: string;
  bodyClassName?: string;
}) {
  return (
    <div
      className={cn(
        "overflow-hidden rounded-lg border border-border bg-card shadow-[0_0_0_1px_rgba(0,0,0,0.2)]",
        className,
      )}
    >
      <div className="flex items-center gap-2 border-b border-border bg-bg-deep px-4 py-2.5">
        <span className="flex gap-1.5" aria-hidden="true">
          <span className="size-2.5 rounded-full bg-level-error/70" />
          <span className="size-2.5 rounded-full bg-level-warn/70" />
          <span className="size-2.5 rounded-full bg-primary/70" />
        </span>
        <span className="ml-1 truncate text-xs text-muted-foreground">
          {title}
        </span>
      </div>
      <div className={cn(bodyClassName)}>{children}</div>
    </div>
  );
}
