"use client";

import { useEffect, useState } from "react";
import { Star } from "lucide-react";
import { Button } from "@/components/ui/button";
import { GithubMark } from "@/components/icons/GithubMark";
import { REPO_SLUG, REPO_URL } from "@/lib/content";

function formatStars(n: number): string {
  if (n < 1000) return String(n);
  return `${(n / 1000).toFixed(1).replace(/\.0$/, "")}k`;
}

export function GithubStarsButton({
  size = "default",
}: {
  size?: "default" | "lg";
}) {
  const [stars, setStars] = useState<number | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetch(`https://api.github.com/repos/${REPO_SLUG}`)
      .then((res) => (res.ok ? res.json() : null))
      .then((data) => {
        if (!cancelled && data && typeof data.stargazers_count === "number") {
          setStars(data.stargazers_count);
        }
      })
      .catch(() => {
        /* stay label-only if the GitHub API is unreachable */
      });
    return () => {
      cancelled = true;
    };
  }, []);

  return (
    <Button
      nativeButton={false}
      render={<a href={REPO_URL} target="_blank" rel="noopener noreferrer" />}
      variant="outline"
      size={size === "lg" ? "lg" : "default"}
      className="gap-2 border-border bg-transparent hover:bg-secondary"
    >
      <GithubMark className="size-4" />
      Star on GitHub
      {stars !== null && (
        <span className="ml-1 inline-flex items-center gap-1 rounded-md bg-secondary px-1.5 py-0.5 text-xs tabular-nums text-muted-foreground">
          <Star className="size-3" aria-hidden="true" />
          {formatStars(stars)}
        </span>
      )}
    </Button>
  );
}
