#!/usr/bin/env python3
"""Lightweight documentation link checker for Flare.Net.

No external dependencies (stdlib only), so it needs nothing installed to
run locally or in CI. Checks two things, per
docs-internal/README.md's validation guidance:

1. Every relative Markdown link in the scanned tree resolves to a real
   file (and, if it carries a `#fragment`, a real heading in that file).
2. Every page under docs/{tutorials,how-to,reference,explanation}/ is
   linked from docs/README.md's own index, so nothing is orphaned.

Scope matches docs-internal/README.md's own recommendation: docs/,
docs-internal/, README.md, CONTRIBUTING.md. Project READMEs under src/*
are intentionally out of scope - see the migration's own "out of scope"
notes for why (they're contributor docs for one package, not part of the
Diátaxis/ADR/investigation system this checks).

Usage:
    python3 scripts/check-docs-links.py

Exit code is non-zero if any broken link or orphaned page is found.
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent

SCAN_ROOTS = [
    REPO_ROOT / "docs",
    REPO_ROOT / "docs-internal",
]
SCAN_FILES = [
    REPO_ROOT / "README.md",
    REPO_ROOT / "CONTRIBUTING.md",
]

LINK_RE = re.compile(r"\[[^\]]*\]\(([^)]+)\)")
HEADING_RE = re.compile(r"^(#{1,6})\s+(.+?)\s*$", re.MULTILINE)


def slugify(heading: str) -> str:
    """Match GitHub's own Markdown heading-anchor algorithm (github-slugger):
    lowercase, drop punctuation (keeping word chars/spaces/hyphens), then
    replace each individual space with a hyphen - deliberately NOT
    collapsing runs of spaces, since GitHub doesn't either (e.g. a
    "Publishing / deploying" heading anchors as '#publishing--deploying',
    double hyphen, not single)."""
    text = heading.strip().lower()
    text = re.sub(r"[`*_]", "", text)  # strip common inline markdown
    text = re.sub(r"[^\w\s-]", "", text)  # drop punctuation
    text = text.replace(" ", "-")
    return text


def markdown_files(roots: list[Path]) -> list[Path]:
    files: list[Path] = []
    for root in roots:
        if root.is_dir():
            files.extend(sorted(root.rglob("*.md")))
    return files


def headings_in(path: Path) -> set[str]:
    try:
        text = path.read_text(encoding="utf-8")
    except OSError:
        return set()
    return {slugify(m.group(2)) for m in HEADING_RE.finditer(text)}


def check_links(files: list[Path]) -> list[str]:
    problems: list[str] = []
    for source in files:
        text = source.read_text(encoding="utf-8")
        for match in LINK_RE.finditer(text):
            target = match.group(1).strip()

            # Skip external links, mailto, and bare-anchor links (those
            # point within the same doc; a full heading-anchor check for
            # the current file is covered separately if needed).
            if target.startswith(("http://", "https://", "mailto:", "#")):
                continue
            # Skip image-only asset references embedded as links (rare)
            # and reference-style placeholders.
            if target.startswith("<") or " " in target:
                continue

            path_part, _, fragment = target.partition("#")
            if not path_part:
                continue  # pure-fragment, already skipped above

            resolved = (source.parent / path_part).resolve()
            rel_source = source.relative_to(REPO_ROOT)

            if not resolved.exists():
                problems.append(
                    f"{rel_source}: link to '{target}' - "
                    f"'{path_part}' does not exist"
                )
                continue

            if fragment and resolved.suffix == ".md":
                if slugify(fragment) not in headings_in(resolved):
                    rel_target = resolved.relative_to(REPO_ROOT)
                    problems.append(
                        f"{rel_source}: link to '{target}' - "
                        f"no '#{fragment}' heading found in {rel_target}"
                    )
    return problems


def check_orphans() -> list[str]:
    problems: list[str] = []
    index = REPO_ROOT / "docs" / "README.md"
    if not index.exists():
        return problems
    index_text = index.read_text(encoding="utf-8")

    for sub in ("tutorials", "how-to", "reference", "explanation"):
        d = REPO_ROOT / "docs" / sub
        if not d.is_dir():
            continue
        for page in sorted(d.glob("*.md")):
            if page.name == "README.md":
                continue
            rel = f"{sub}/{page.name}"
            # A page is "linked" if its relative path (or bare filename,
            # for links written without the subfolder prefix) appears
            # anywhere in the index.
            if rel not in index_text and page.name not in index_text:
                problems.append(f"docs/{rel} is not linked from docs/README.md")
    return problems


def main() -> int:
    files = markdown_files(SCAN_ROOTS) + [f for f in SCAN_FILES if f.exists()]
    link_problems = check_links(files)
    orphan_problems = check_orphans()

    if not link_problems and not orphan_problems:
        print(f"OK - checked {len(files)} files, no broken links or orphaned pages.")
        return 0

    if link_problems:
        print(f"Broken links ({len(link_problems)}):")
        for p in link_problems:
            print(f"  {p}")
    if orphan_problems:
        print(f"Orphaned pages ({len(orphan_problems)}):")
        for p in orphan_problems:
            print(f"  {p}")
    return 1


if __name__ == "__main__":
    sys.exit(main())