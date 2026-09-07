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
  - A relative link to ANOTHER in-scope doc (e.g. a Chinese README linking to
    `docs/how-to/run-with-aspire.md`) gets repointed at that doc's own
    `<name>.<langcode>.md` translation instead of silently staying on the English
    original - otherwise every cross-doc link in every translated file would drop the
    reader back into English. This (and the matching cross-file anchor fix-up) is a
    SEPARATE pass over the already-written output, not part of translate_markdown()
    itself: it's pure regex/text work, no MT call, so it reruns on every invocation
    regardless of whether a doc's source hash changed - a translated file that's
    otherwise "up to date" still gets its links repaired. A link back to the file's
    OWN English source (a language-switcher's "[English](README.md)") is left alone
    on purpose: that's the one case where staying on the English original is correct.
    A link to a doc that's out of scope (docs-internal/, src/*/README.md, LICENSE,
    ...) is also left alone, same reasoning - there's no translation to point at.

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
    "ru": "ru",
    "fr": "fr",
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
# Keyed per-langcode (NOT shared across languages) - these are hand-picked
# translations for one specific target language, so a zh-CN entry must never be
# consulted while translating to ru/fr or it would silently emit Chinese text into a
# Russian/French doc. An empty/missing langcode here just means "no known mistranslations
# for this language yet, fall through to plain MT" - add entries as they turn up during
# review of that language's generated output, same as zh-CN's were.
OVERRIDES: dict[str, dict[str, str]] = {
    "zh-CN": {
        "Status": "当前状态",
        "Next": "接下来",
        "Auth": "身份验证",
        "Authentication": "身份验证",
        "Traces": "追踪",
        "Views": "视图",
        "License": "许可证",
        "Local development": "本地开发",
    },
    "ru": {
        # "Ingestion" (as in the Dashboard's Ingestion page / README feature-table
        # row) came back "Проглатывание" - literally "swallowing" - rather than the
        # data-pipeline sense; observed in README.md's feature table and repeated
        # verbatim as a heading in docs/explanation/architecture.md.
        "Ingestion": "Приём данных",
        # "Reference" (the Diátaxis doc-category name in docs/README.md) came back
        # "Ссылка", which means "link/hyperlink" in Russian, not "reference
        # documentation" - a false-friend translation of a different sense of the
        # English word.
        "Reference": "Справочник",
    },
}

BULLET_RE = re.compile(r"^(\s*(?:[-*+]|\d+\.)\s+)(.*)$")
EMPHASIS_RE = re.compile(r"^(\*\*|\*|__|_)(.+)\1$")


def lookup_override(text: str, langcode: str) -> str | None:
    """Matches OVERRIDES[langcode] against `text` with a surrounding list-bullet
    prefix and/or **bold**/_italic_ wrapper stripped first, then reapplies whichever
    wrapper it found - so both "- Traces" and "**Auth**" hit the same plain-word key."""
    lang_overrides = OVERRIDES.get(langcode)
    if not lang_overrides:
        return None
    stripped = text.strip()
    if stripped in lang_overrides:
        return lang_overrides[stripped]
    m = EMPHASIS_RE.match(stripped)
    if m and m.group(2) in lang_overrides:
        return f"{m.group(1)}{lang_overrides[m.group(2)]}{m.group(1)}"
    return None

MARKER_RE = re.compile(r"source-sha256:([0-9a-f]+)")
HEADING_RE = re.compile(r"^(#{1,6})\s+(.+?)\s*$")
TABLE_ROW_RE = re.compile(r"^\s*\|.*\|\s*$")
TABLE_SEP_CELL_RE = re.compile(r"^\s*:?-+:?\s*$")
FRAGMENT_LINK_RE = re.compile(r"(\(#)([\w-]+)(\))")

# A markdown link OR image: group(1) is "!" for an image (never a cross-doc-link
# rewrite target), group(2) the link text, group(3) the raw target (path + optional
# "#anchor"). Deliberately the same shape protect()'s PROTECT_RE matches - these
# functions run on already-translated, already-restored text, not the protected form.
LINK_RE = re.compile(r"(!?)\[([^\]]*)\]\(([^)]+)\)")
# Same, but ONLY a link whose target already ends "<something>.<langcode>.md#anchor" -
# used by rewrite_cross_doc_anchors(), which runs after rewrite_cross_doc_links() has
# already repointed the path half.
CROSS_DOC_ANCHOR_RE = re.compile(r"(!?)\[([^\]]*)\]\(([^)#]+\.md)#([\w-]+)\)")

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


PLACEHOLDER_RE = re.compile(f"{PH_OPEN}(\\d+){PH_CLOSE}")

# At least one "real" (non-placeholder) letter anywhere - Unicode-aware so it
# recognizes letters from any script, not just ASCII.
LETTER_RE = re.compile(r"[^\W\d_]", re.UNICODE)


def protect(line: str) -> tuple[str, list[str]]:
    saved: list[str] = []

    def _sub(m: re.Match) -> str:
        saved.append(m.group(0))
        return f"{PH_OPEN}{len(saved) - 1}{PH_CLOSE}"

    return PROTECT_RE.sub(_sub, line), saved


def has_translatable_text(protected: str) -> bool:
    """False when `protected` is nothing but placeholder tokens and punctuation/
    whitespace once they're stripped out - e.g. a heading like "Active Directory
    (LDAP)" is ENTIRELY glossary terms, so protect() leaves only "ZZPH0ZZ (ZZPH1ZZ)".
    Sending that to the MT engine is pointless and, for at least one target script
    (observed with ru), actively harmful: Google Translate transliterated the bare
    placeholder tokens into Cyrillic look-alikes ("ЗЗПХ0ЗЗ"), which restore()'s
    ASCII-only regex then no longer matches - the placeholder leaks into the final
    doc verbatim instead of being replaced back. Skipping the call whenever there's
    no real text left to translate avoids the whole failure class, for every
    language, not just the one it was caught on."""
    return bool(LETTER_RE.search(PLACEHOLDER_RE.sub("", protected)))


def restore(text: str, saved: list[str]) -> str:
    def _sub(m: re.Match) -> str:
        return saved[int(m.group(1))]

    return PLACEHOLDER_RE.sub(_sub, text)


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
            # 1.2s, not the original 0.2s - a full multi-file run (dozens of files x
            # many lines/cells each) at 0.2s tripped this free endpoint's rate limit
            # (HTTP 429) partway through a real run; 1.2s is the conservative fix.
            time.sleep(1.2)  # be a polite citizen of a free, keyless, unofficial endpoint
            return leading + translated + trailing
        except urllib.error.HTTPError as e:
            if attempt == retries - 1:
                print(f"  ! translation failed, leaving line untranslated: {e}", file=sys.stderr)
                return text
            # A 429 means the endpoint wants a real cooldown, not the short generic
            # backoff below - retrying it in ~1-3s (as a plain URLError would) just
            # burns through the retry budget hitting the same wall again. Any other
            # HTTP error status falls back to the same generic backoff as a URLError.
            time.sleep(20.0 * (attempt + 1) if e.code == 429 else 1.5 * (attempt + 1))
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
    override = lookup_override(body, target)
    if override is not None:
        return prefix + override
    protected, saved = protect(body)
    if not has_translatable_text(protected):
        # Nothing left but placeholders/punctuation (e.g. a heading that's entirely
        # glossary terms, like "Active Directory (LDAP)") - skip the network call
        # and restore straight from the protected string. See has_translatable_text().
        return prefix + restore(protected, saved)
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


def extract_headings(text: str) -> list[str]:
    """Original (untranslated where called on a source file, translated where called
    on an output file) heading text, one entry per '#'-line, in document order -
    fence-tracked the same way translate_markdown() is so a '#' inside a code sample
    never counts. Used by derive_slug_map() to reconstruct a file's old-slug ->
    new-slug map straight from what's already on disk, without a fresh MT pass."""
    out: list[str] = []
    in_fence = False
    fence_marker = ""
    for line in text.split("\n"):
        fence_match = re.match(r"^\s*(```+|~~~+)", line)
        if fence_match:
            marker = fence_match.group(1)[:3]
            if not in_fence:
                in_fence, fence_marker = True, marker
            elif line.strip().startswith(fence_marker):
                in_fence = False
            continue
        if in_fence:
            continue
        m = HEADING_RE.match(line)
        if m:
            out.append(m.group(2))
    return out


def derive_slug_map(source_text: str, translated_text: str) -> dict[str, str]:
    """Pairs an English source's headings with its already-generated translation's
    headings, by document order (translation preserves heading count/order 1:1), to
    rebuild the same old-slug -> new-slug map translate_markdown() computes during a
    fresh translation - cheap enough (no MT call) to rerun on every invocation, even
    for a file whose source hash didn't change and so got no fresh translation."""
    slug_map: dict[str, str] = {}
    for en, tr in zip(extract_headings(source_text), extract_headings(translated_text)):
        old_slug, new_slug = slugify(en), slugify(tr)
        if old_slug != new_slug:
            slug_map[old_slug] = new_slug
    return slug_map


def rewrite_cross_doc_links(text: str, source: Path, langcode: str, in_scope: set[Path]) -> str:
    """Repoints a relative link at another in-scope doc to that doc's own
    <name>.<langcode>.md translation - see the module docstring for the full
    rationale. Runs on an already-translated file's content, so `text` here has
    ordinary, unprotected `[label](url)` syntax to match directly."""
    own_source = source.resolve()

    def _sub(m: re.Match) -> str:
        bang, label, url = m.groups()
        if bang:
            return m.group(0)  # an image, never a doc link
        path_part, sep, anchor = url.partition("#")
        if not path_part or not path_part.endswith(".md"):
            return m.group(0)
        if re.match(r"^[a-zA-Z][a-zA-Z0-9+.-]*:", path_part):
            return m.group(0)  # http(s):, mailto:, ... - not a relative repo path
        if any(path_part.endswith(f".{lc}.md") for lc in LANGS):
            return m.group(0)  # already points at an explicit locale on purpose
        target = (source.parent / path_part).resolve()
        if target == own_source or target not in in_scope:
            return m.group(0)
        new_path_part = path_part[: -len(".md")] + f".{langcode}.md"
        return f"{bang}[{label}]({new_path_part}{sep}{anchor})"

    return LINK_RE.sub(_sub, text)


def rewrite_cross_doc_anchors(
    text: str, langcode: str, out_dir: Path, slug_maps: dict[tuple[Path, str], dict[str, str]]
) -> str:
    """Second pass, run only after every file's slug map is known: fixes up the
    `#anchor` half of a link rewrite_cross_doc_links() already repointed at a sibling
    translation - the target heading's slug is usually different once translated, so
    the anchor copied verbatim from the English link would otherwise silently 404
    inside the (now correctly-language) page it points to. `slug_maps` is keyed by
    (english_source, langcode), NOT just english_source - a heading's translated slug
    is obviously language-specific, so looking it up without langcode would leak e.g.
    a Chinese slug into a French document's anchor."""
    suffix = f".{langcode}.md"

    def _sub(m: re.Match) -> str:
        bang, label, path_part, anchor = m.groups()
        if bang or not path_part.endswith(suffix):
            return m.group(0)
        target_source = (out_dir / (path_part[: -len(suffix)] + ".md")).resolve()
        slug_map = slug_maps.get((target_source, langcode))
        if not slug_map or anchor not in slug_map:
            return m.group(0)
        return f"{bang}[{label}]({path_part}#{slug_map[anchor]})"

    return CROSS_DOC_ANCHOR_RE.sub(_sub, text)


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


def relink_translations(sources: list[Path]) -> None:
    """Phase 2, run after every requested file's prose is up to date: repoints
    cross-doc links at sibling translations and repairs their anchors. Pure local
    text work (no MT, no network) so it always reruns, even for a file whose source
    hash didn't change and so got no fresh translation in phase 1 above - that's the
    only way an already-generated file (most of them, on a typical run) actually gets
    this fix applied. `in_scope` is always the FULL doc set, not just `sources` -
    `translate-docs.py path/to/one-file.md` should still recognize a link to some
    other, unrequested doc as in-scope and repoint it, even though that other doc's
    own translation isn't being (re)written this run.
    """
    all_sources = discover_sources([])
    in_scope = {s.resolve() for s in all_sources}

    # Slug maps are needed for every in-scope doc, not just the ones this run was
    # asked to (re)translate - a requested file can link to an unrequested one, and
    # that link's anchor still needs the unrequested doc's already-on-disk slug map.
    slug_maps: dict[tuple[Path, str], dict[str, str]] = {}
    for source in all_sources:
        source_text = source.read_text(encoding="utf-8")
        for langcode in LANGS:
            out_path = translated_path(source, langcode)
            if out_path.exists():
                slug_maps[(source.resolve(), langcode)] = derive_slug_map(
                    source_text, out_path.read_text(encoding="utf-8")
                )

    for source in sources:
        for langcode in LANGS:
            out_path = translated_path(source, langcode)
            if not out_path.exists():
                continue
            text = out_path.read_text(encoding="utf-8")
            new_text = rewrite_cross_doc_links(text, source, langcode, in_scope)
            new_text = rewrite_cross_doc_anchors(new_text, langcode, out_path.parent, slug_maps)
            if new_text != text:
                out_path.write_text(new_text, encoding="utf-8")
                print(f"  relinked: {out_path.relative_to(REPO_ROOT)}")


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

    relink_translations(sources)
    return 0


if __name__ == "__main__":
    sys.exit(main())
