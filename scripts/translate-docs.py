#!/usr/bin/env python3
"""Generates/refreshes Chinese (zh-CN) translations of Flare's user-facing docs.

No third-party dependencies (stdlib only, matching every other scripts/*.py in this
repo) - calls Google Translate's public, keyless web endpoint
(translate.googleapis.com/translate_a/single) directly via urllib. This is the exact
backend packages like `deep-translator`/`googletrans` wrap; calling it straight means no
`pip install` step for anyone running this. Trade-off, deliberately accepted: it's
unofficial (no SLA, could rate-limit or change shape without notice) rather than a paid/
keyed API - fine for an occasional, low-volume doc-translation run, not something to
build a hard dependency on.

Scope: README.md + everything under docs/ (the user-facing Diátaxis tree) - NOT
docs-internal/ (maintainer-only ADRs/investigations/planning, not meant for a
translated audience) and NOT src/*/README.md (contributor docs for one package,
already out of scope for check-docs-links.py's own validation, same reasoning here).

Output convention: `<name>.<langcode>.md` next to the source, e.g. README.zh-CN.md,
docs/tutorials/getting-started.zh-CN.md - so nothing looks unusual browsing the repo.

Change detection: each generated file's first line is an HTML-comment marker recording
the source file's content hash at generation time. A rerun only re-translates a file
whose current source hash no longer matches that marker - unchanged docs are skipped
entirely (no network calls), and a hand-edited translation is easy to spot (the marker
comment says not to).

What gets translated vs. left alone, within a source file:
  - Fenced code blocks (```/~~~) are skipped entirely, verbatim, fence lines included.
  - Inline code spans, raw HTML tags/comments, and whole link/image syntax
    (`[text](url)` / `![alt](url)`) are left untouched, INCLUDING the visible link
    text - splitting "translate the text but not the target" safely was judged not
    worth the corruption risk for a v1. Known limitation: link text stays in English
    inside an otherwise-Chinese doc.
  - A short glossary of brand/technical terms (ClickHouse, OTLP, .NET, Aspire, ...) is
    protected the same way, so machine translation can't mangle a proper noun.
  - Table rows (`| a | b |`) are translated cell-by-cell so the table structure
    survives.
  - Headings are translated, and any `(#anchor)` link elsewhere in the SAME file that
    pointed at a heading's original slug is rewritten to the translated heading's new
    slug (recomputed with the exact slugify() algorithm scripts/check-docs-links.py
    uses) - otherwise a translated heading would silently break its own in-page
    anchor links. See check-docs-links.py's own docstring for why that check exists;
    check_orphans() there also has a matching exclusion for `*.<langcode>.md` files
    (they mirror an already-indexed source page, not a new one needing its own entry).

Usage:
    python3 scripts/translate-docs.py                 # refresh everything in scope
    python3 scripts/translate-docs.py path/to/file.md  # just one file
    python3 scripts/translate-docs.py --check          # exit non-zero if anything is
                                                        # stale; translates nothing and
                                                        # makes no network calls - for
                                                        # an optional CI freshness gate
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent

# langcode -> Google Translate target code (currently identical, kept separate in case
# a future language needs a different Google code than its filename langcode).
LANGS = {
    "zh-CN": "zh-CN",
}

DOCS_ROOT = REPO_ROOT / "docs"
ROOT_README = REPO_ROOT / "README.md"

# Proper nouns / technical terms a plain MT engine is prone to mangling. Add to this
# list if a mistranslation shows up in a generated file - cheaper than switching engines.
GLOSSARY = [
    "Flare", "Flare.Net", "ClickHouse", "OpenTelemetry", "OTLP", ".NET", "Aspire",
    "SvelteKit", "Svelte", "Tailwind", "Docker", "GitHub", "GitHub Actions", "Redis",
    "Redis Streams", "MIT", "Seq", "Datadog", "Slack", "Telegram", "NuGet", "xUnit",
    "gRPC", "HTTP", "HTTPS", "WebSocket", "Entra ID", "Active Directory", "LDAP",
    "OIDC", "SQLite", "Drain", "ADR", "CLI", "API", "SDK", "JSON", "YAML", "SQL",
    "TypeScript", "MemoryPack", "AppHost", "Dockerfile", "docker compose",
    "docker-compose.yml", "Flare.Ingest", "Flare.Api", "Flare.Identity",
    "Flare.AppHost", "Flare.Cli", "Flare.ServiceDefaults", "HelloGitHub",
]

# Exact full-line/full-cell overrides for short, context-free words the MT engine
# picked a plausible but wrong sense for (checked empirically against a real README
# translation - e.g. "Status" as a lone heading came back "地位", social/hierarchical
# rank, rather than "current state of the project"). Only applied when the ENTIRE
# stripped line/cell equals a key here, never as a substring - add more as they turn up.
OVERRIDES = {
    "Status": "当前状态",
    "Next": "接下来",
    "Auth": "身份验证",
    "Authentication": "身份验证",
    "Traces": "追踪",
    "Views": "视图",
    "License": "许可证",
    "Local development": "本地开发",
}

BULLET_RE = re.compile(r"^(\s*(?:[-*+]|\d+\.)\s+)(.*)$")
EMPHASIS_RE = re.compile(r"^(\*\*|\*|__|_)(.+)\1$")


def lookup_override(text: str) -> str | None:
    """Matches OVERRIDES against `text` with a surrounding list-bullet prefix and/or
    **bold**/_italic_ wrapper stripped first, then reapplies whichever wrapper it
    found - so both "- Traces" and "**Auth**" hit the same plain-word key."""
    stripped = text.strip()
    if stripped in OVERRIDES:
        return OVERRIDES[stripped]
    m = EMPHASIS_RE.match(stripped)
    if m and m.group(2) in OVERRIDES:
        return f"{m.group(1)}{OVERRIDES[m.group(2)]}{m.group(1)}"
    return None

MARKER_RE = re.compile(r"source-sha256:([0-9a-f]+)")
HEADING_RE = re.compile(r"^(#{1,6})\s+(.+?)\s*$")
TABLE_ROW_RE = re.compile(r"^\s*\|.*\|\s*$")
TABLE_SEP_CELL_RE = re.compile(r"^\s*:?-+:?\s*$")
FRAGMENT_LINK_RE = re.compile(r"(\(#)([\w-]+)(\))")

# A distinctive all-caps alphanumeric token (not actual Unicode delimiter characters -
# Private-Use-Area brackets were the obvious first choice, but got silently stripped
# by this engine in testing, sometimes even duplicating the bare digit left behind and
# corrupting restoration). This ASCII form reliably survives as one opaque "unknown
# word" - translation may reorder it within the sentence (expected/harmless - restore()
# matches by index, not position) but never splits or mangles it.
PH_OPEN, PH_CLOSE = "ZZPH", "ZZ"

PROTECT_RE = re.compile(
    "|".join(
        [
            r"`[^`]+`",                          # inline code
            r"\[!\[[^\]]*\]\([^)]*\)\]\([^)]*\)",  # a linked badge: [![alt](img)](link) -
            # MUST come before the plain image/link patterns below - alternation tries
            # earlier alternatives first at a given position, and without this the
            # plain link pattern (`\[[^\]]*\]\([^)]*\)`) matches only up through the
            # INNER image's closing paren, leaving the outer "](link)" dangling as
            # ordinary text. Usually harmless (a dangling URL has nothing translatable
            # in it) but corrupted a real badge once the outer target happened to be an
            # English word ("](LICENSE)" -> translated to "](许可证)").
            r"!\[[^\]]*\]\([^)]*\)",             # a lone image
            r"\[[^\]]*\]\([^)]*\)",               # a lone link (text kept as-is, see docstring)
            r"<[^>]+>",                          # raw HTML tags/comments
        ]
        + [re.escape(term) for term in sorted(GLOSSARY, key=len, reverse=True)]
    )
)


def slugify(heading: str) -> str:
    """Copied verbatim from check-docs-links.py - keep the two in sync if either
    changes; a mismatch here silently breaks this script's anchor-rewrite step."""
    text = heading.strip().lower()
    text = re.sub(r"[`*_]", "", text)
    text = re.sub(r"[^\w\s-]", "", text)
    text = text.replace(" ", "-")
    return text


def protect(line: str) -> tuple[str, list[str]]:
    saved: list[str] = []

    def _sub(m: re.Match) -> str:
        saved.append(m.group(0))
        return f"{PH_OPEN}{len(saved) - 1}{PH_CLOSE}"

    return PROTECT_RE.sub(_sub, line), saved


def restore(text: str, saved: list[str]) -> str:
    def _sub(m: re.Match) -> str:
        return saved[int(m.group(1))]

    return re.sub(f"{PH_OPEN}(\\d+){PH_CLOSE}", _sub, text)


def google_translate(text: str, target: str, source: str = "en", retries: int = 3) -> str:
    stripped = text.strip()
    if not stripped:
        return text
    leading = text[: len(text) - len(text.lstrip())]
    trailing = text[len(text.rstrip()):]

    params = {"client": "gtx", "sl": source, "tl": target, "dt": "t", "q": stripped}
    url = "https://translate.googleapis.com/translate_a/single?" + urllib.parse.urlencode(params)
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})

    for attempt in range(retries):
        try:
            with urllib.request.urlopen(req, timeout=15) as resp:
                data = json.loads(resp.read().decode("utf-8"))
            translated = "".join(seg[0] for seg in data[0] if seg and seg[0])
            time.sleep(0.2)  # be a polite citizen of a free, keyless, unofficial endpoint
            return leading + translated + trailing
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError, IndexError) as e:
            if attempt == retries - 1:
                print(f"  ! translation failed, leaving line untranslated: {e}", file=sys.stderr)
                return text
            time.sleep(1.5 * (attempt + 1))
    return text  # unreachable, satisfies type checkers


def translate_line(line: str, target: str) -> str:
    # The bullet/indentation prefix is split off and reattached verbatim on BOTH
    # paths below, not just the override one - translating it along with the body
    # lost real nested-list indentation in testing (Google Translate normalizes
    # leading whitespace away), flattening a sub-list into its parent's level.
    bullet_m = BULLET_RE.match(line)
    prefix, body = (bullet_m.group(1), bullet_m.group(2)) if bullet_m else ("", line)
    override = lookup_override(body)
    if override is not None:
        return prefix + override
    protected, saved = protect(body)
    translated = google_translate(protected, target)
    return prefix + restore(translated, saved)


def translate_table_row(line: str, target: str) -> str:
    # Preserve leading/trailing pipe presence and whitespace shape; translate each
    # cell independently so `|` delimiters and header-separator rows survive intact.
    cells = line.split("|")
    for i, cell in enumerate(cells):
        stripped = cell.strip()
        if not stripped or TABLE_SEP_CELL_RE.match(stripped):
            continue  # separator row ("---", ":--:", ...) or empty edge cell
        pad_l = cell[: len(cell) - len(cell.lstrip())]
        pad_r = cell[len(cell.rstrip()):]
        cells[i] = pad_l + translate_line(stripped, target) + pad_r
    return "|".join(cells)


def translate_markdown(source_text: str, target: str) -> tuple[str, dict[str, str]]:
    """Returns (translated_text, old_slug -> new_slug map) for this one file."""
    lines = source_text.split("\n")
    out: list[str] = []
    slug_map: dict[str, str] = {}
    in_fence = False
    fence_marker = ""
    para_buf: list[str] = []

    def flush_para() -> None:
        # A hard-wrapped paragraph (or a single list item whose text wraps across
        # several physical lines) is translated as ONE unit, not line-by-line.
        # This matters beyond just "one clean translation call": an inline code span
        # or link that the source wrapped across a line break (both delimiters exist,
        # just not on the same physical line) is invisible to protect()'s per-string
        # regexes unless the whole logical paragraph is joined first - confirmed live:
        # a paragraph with `` `dotnet tool install --global\nFlare.Cli` `` split like
        # that had the command text itself machine-translated, corrupting it.
        # translate_line()'s own bullet-prefix handling still applies here since a
        # list item's marker/indentation is on the buffer's first line, now at the
        # start of the joined string - nested lists keep their original indentation.
        if not para_buf:
            return
        # Keep the FIRST line's leading whitespace (that's what signals a nested
        # list's indent depth to translate_line()'s own BULLET_RE) - only strip its
        # trailing side, and strip continuation lines on both sides before joining.
        first = para_buf[0].rstrip()
        rest = [s.strip() for s in para_buf[1:]]
        joined = " ".join([first, *rest])
        out.append(translate_line(joined, target))
        para_buf.clear()

    for line in lines:
        fence_match = re.match(r"^\s*(```+|~~~+)", line)
        if fence_match:
            flush_para()
            marker = fence_match.group(1)[:3]
            if not in_fence:
                in_fence, fence_marker = True, marker
            elif line.strip().startswith(fence_marker):
                in_fence = False
            out.append(line)
            continue
        if in_fence:
            out.append(line)
            continue

        heading_match = HEADING_RE.match(line)
        if heading_match:
            flush_para()
            hashes, text = heading_match.groups()
            old_slug = slugify(text)
            translated_text = translate_line(text, target)
            new_slug = slugify(translated_text)
            if new_slug != old_slug:
                slug_map[old_slug] = new_slug
            out.append(f"{hashes} {translated_text}")
            continue

        if TABLE_ROW_RE.match(line):
            flush_para()
            out.append(translate_table_row(line, target))
            continue

        if not line.strip():
            flush_para()
            out.append(line)
            continue

        # A new list item starts a new logical unit even with no blank line before
        # it (very common: several one-line bullets back to back, or a bullet
        # immediately following its introducing sentence) - flush whatever was
        # accumulating first so items never get concatenated into one translation.
        if BULLET_RE.match(line) and para_buf:
            flush_para()
        para_buf.append(line)

    flush_para()
    text = "\n".join(out)

    def _rewrite_anchor(m: re.Match) -> str:
        old = m.group(2)
        return f"{m.group(1)}{slug_map.get(old, old)}{m.group(3)}"

    text = FRAGMENT_LINK_RE.sub(_rewrite_anchor, text)
    return text, slug_map


def source_hash(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()[:16]


def translated_path(source: Path, langcode: str) -> Path:
    return source.with_suffix(f".{langcode}{source.suffix}")


def existing_marker_hash(path: Path) -> str | None:
    if not path.exists():
        return None
    with path.open(encoding="utf-8") as f:
        first_line = f.readline()
    m = MARKER_RE.search(first_line)
    return m.group(1) if m else None


def discover_sources(explicit: list[Path]) -> list[Path]:
    if explicit:
        return explicit
    sources = [ROOT_README] if ROOT_README.exists() else []
    if DOCS_ROOT.is_dir():
        for p in sorted(DOCS_ROOT.rglob("*.md")):
            # Skip files that are themselves a generated translation (double
            # extension, e.g. "getting-started.zh-CN.md" -> stem contains a dot).
            if Path(p.stem).suffix:
                continue
            sources.append(p)
    return sources


def process_file(source: Path, langcode: str, google_code: str, check_only: bool) -> bool:
    """Returns True if the translation is (now) up to date."""
    out_path = translated_path(source, langcode)
    current_hash = source_hash(source)
    if existing_marker_hash(out_path) == current_hash:
        print(f"  up to date: {out_path.relative_to(REPO_ROOT)}")
        return True

    if check_only:
        print(f"  STALE: {out_path.relative_to(REPO_ROOT)}")
        return False

    print(f"  translating -> {out_path.relative_to(REPO_ROOT)}")
    translated, _ = translate_markdown(source.read_text(encoding="utf-8"), google_code)
    rel_source = source.relative_to(REPO_ROOT)
    marker = (
        f"<!-- Auto-translated ({langcode}) from {rel_source} · "
        f"source-sha256:{current_hash} · generated by scripts/translate-docs.py - "
        f"edit the source file and rerun the script, don't hand-edit this one. -->\n\n"
    )
    out_path.write_text(marker + translated, encoding="utf-8")
    return True


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="*", type=Path, help="specific source files (default: full scope)")
    parser.add_argument("--check", action="store_true", help="report staleness only, no network calls")
    args = parser.parse_args()

    sources = discover_sources([REPO_ROOT / p for p in args.paths])
    all_up_to_date = True
    for source in sources:
        print(f"{source.relative_to(REPO_ROOT)}:")
        for langcode, google_code in LANGS.items():
            ok = process_file(source, langcode, google_code, args.check)
            all_up_to_date = all_up_to_date and ok

    if args.check:
        print("OK - all translations up to date." if all_up_to_date else "Some translations are stale - run scripts/translate-docs.py.")
        return 0 if all_up_to_date else 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
