#!/usr/bin/env python3
"""Build the docfx documentation site from docs/ (published to GitHub Pages).

docs/ is written to read well on GitHub, so it can't be fed to docfx as-is. This
script stages a copy per language into artifacts/docs-site/src/<lang>/ and then
runs docfx on each one:

- One site per language: English at the site root, translations under /ru/, /fr/,
  /zh-CN/. A translated page `foo.ru.md` is staged as `foo.md`, so every language
  site has the same URLs (the language picker in docs/site/template relies on it).
  A page with no translation falls back to the English text.
- docs/README.<lang>.md becomes the landing page, and its "All pages" list is
  turned into the navigation: each **bold** group is a navbar section with its own
  toc.yml, and each *italic* subgroup is a sidebar group within it. There is still
  one index to maintain, and scripts/check-docs-links.py already requires every
  page to be on it.
- A relative link that points outside docs/ (an ADR, a source file, a migration)
  is rewritten to its GitHub URL, because the site only holds docs/. A link to a
  docs/ folder goes to the landing page.

Usage:
  python3 scripts/build-docs-site.py                  # stage + build into artifacts/docs-site/_site
  python3 scripts/build-docs-site.py --strict         # docfx warnings are errors (what CI runs)
  python3 scripts/build-docs-site.py --lang en        # one language only (faster local iteration)
  python3 scripts/build-docs-site.py --stage-only     # stage the sources, skip docfx
  dotnet docfx serve artifacts/docs-site/_site        # preview
"""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
DOCS = REPO / "docs"
OUT = REPO / "artifacts" / "docs-site"
GITHUB = "https://github.com/aminparsa18/Flare.Net"
SITE_URL = "https://aminparsa18.github.io/Flare.Net/"

# (code, file suffix, output sub-folder). Codes must match docs/site/template/public/main.js.
LANGS = [
    ("en", "", ""),
    ("ru", ".ru", "ru"),
    ("fr", ".fr", "fr"),
    ("zh-CN", ".zh-CN", "zh-CN"),
]
LANG_SUFFIX_RE = re.compile(r"\.(ru|fr|zh-CN)\.md$")
SECTIONS = ["tutorials", "how-to", "reference", "explanation"]
ASSET_EXTS = {".webp", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico"}

# [text](target) / ![alt](target "title"); excludes nested parens, which docs/ doesn't use in links.
LINK_RE = re.compile(r"(!?\[[^\]]*\]\()([^)\s]+)((?:\s+\"[^\"]*\")?\))")
FENCE_RE = re.compile(r"^\s*(```|~~~)")


def english_pages() -> list[Path]:
    """Every English page under docs/, relative to docs/ (the language-neutral page set)."""
    pages = []
    for p in sorted(DOCS.rglob("*.md")):
        rel = p.relative_to(DOCS)
        if rel.parts[0] == "site" or LANG_SUFFIX_RE.search(p.name) or rel.name == "README.md":
            continue
        pages.append(rel)
    return pages


def rewrite_target(target: str, source_rel: Path) -> str:
    """Rewrite one link target found in docs/<source_rel> for the staged site."""
    if re.match(r"^[a-z][a-z0-9+.-]*:", target, re.I) or target.startswith(("#", "/")):
        return target
    path, _, anchor = target.partition("#")
    anchor = f"#{anchor}" if anchor else ""
    resolved = (DOCS / source_rel.parent / path).resolve()

    try:
        in_docs = resolved.relative_to(DOCS)
    except ValueError:
        # Outside docs/: link to the file or folder on GitHub.
        try:
            repo_rel = resolved.relative_to(REPO).as_posix()
        except ValueError:
            return target
        kind = "tree" if resolved.is_dir() or path.endswith("/") else "blob"
        return f"{GITHUB}/{kind}/main/{repo_rel}{anchor}"

    rel_to_page = lambda p: Path(*([".."] * len(source_rel.parent.parts)), p).as_posix()  # noqa: E731
    if resolved.is_dir():
        return rel_to_page("index.md")
    name = in_docs.name
    if name.endswith(".md"):
        name = LANG_SUFFIX_RE.sub(".md", name)
        if name == "README.md" and in_docs.parent == Path("."):
            return rel_to_page("index.md") + anchor
        target_rel = in_docs.with_name(name)
        return rel_to_page(target_rel.as_posix()) + anchor
    return target


def rewrite_markdown(text: str, source_rel: Path) -> str:
    # Rewrite whole prose chunks between code fences, not single lines: a link's text
    # often wraps onto the next line ("[structural\nquery](find-traces-by-structure.md)").
    chunks: list[tuple[bool, list[str]]] = [(False, [])]
    in_fence = False
    for line in text.splitlines(keepends=True):
        if FENCE_RE.match(line):
            in_fence = not in_fence
            chunks.append((True, [line]) if in_fence else (False, []))
            if not in_fence:
                chunks[-2][1].append(line)
            continue
        chunks[-1][1].append(line)
    sub = lambda m: m.group(1) + rewrite_target(m.group(2), source_rel) + m.group(3)  # noqa: E731
    return "".join("".join(lines) if fence else LINK_RE.sub(sub, "".join(lines)) for fence, lines in chunks)


def parse_index(readme: str) -> list[dict]:
    """Turn the index's "All pages" section into [{name, items: [...]}] groups (one per bold heading)."""
    lines = readme.splitlines()
    headings = [i for i, l in enumerate(lines) if l.startswith("## ")]
    if len(headings) < 2:
        sys.exit("docs README: expected an '## All pages' section as the second '##' heading")
    start = headings[1] + 1
    end = next((i for i in headings if i > headings[1]), len(lines))

    groups: list[dict] = []
    sub: dict | None = None
    for line in lines[start:end]:
        if m := re.fullmatch(r"\*\*(.+?)\*\*\s*", line):
            groups.append({"name": m.group(1).strip(), "items": []})
            sub = None
        elif m := re.fullmatch(r"\*([^*].*?)\*\s*", line):
            sub = {"name": m.group(1).strip(), "items": []}
            groups[-1]["items"].append(sub)
        elif m := re.match(r"- \[(.+?)\]\(([^)#]+)\)", line):
            item = {"name": m.group(1).strip(), "href": LANG_SUFFIX_RE.sub(".md", m.group(2))}
            (sub or groups[-1])["items"].append(item)
    return groups


def yaml_str(s: str) -> str:
    return json.dumps(s, ensure_ascii=False)  # JSON strings are valid YAML scalars


def write_toc(path: Path, items: list[dict], prefix: str, indent: int = 0) -> list[str]:
    lines = []
    pad = "  " * indent
    for it in items:
        lines.append(f"{pad}- name: {yaml_str(it['name'])}")
        if "href" in it:
            href = it["href"][len(prefix):] if it["href"].startswith(prefix) else it["href"]
            lines.append(f"{pad}  href: {yaml_str(href)}")
        if it.get("items"):
            lines.append(f"{pad}  items:")
            lines.extend(write_toc(path, it["items"], prefix, indent + 2))
    if indent == 0:
        path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return lines


def stage(code: str, suffix: str, outdir: str) -> Path:
    dest = OUT / "src" / code
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True)

    for rel in english_pages():
        translated = DOCS / rel.with_name(rel.stem + suffix + ".md")
        src = translated if translated.exists() else DOCS / rel
        target = dest / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(rewrite_markdown(src.read_text(encoding="utf-8"), rel), encoding="utf-8")

    readme_path = DOCS / f"README{suffix}.md"
    readme = readme_path.read_text(encoding="utf-8")
    (dest / "index.md").write_text(rewrite_markdown(readme, Path("README.md")), encoding="utf-8")

    for p in DOCS.rglob("*"):
        rel = p.relative_to(DOCS)
        if p.is_file() and p.suffix.lower() in ASSET_EXTS and rel.parts[0] != "site":
            (dest / rel).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(p, dest / rel)
    # The dashboard's brand mark (its favicon.svg is the unmodified Svelte scaffold, not Flare's).
    for name in ("logo.png", "favicon.ico"):
        shutil.copy2(REPO / "src" / "dashboard" / "static" / name, dest / name)

    # Navigation: root toc.yml is the navbar (one entry per Diátaxis section), each
    # section folder's toc.yml is that section's sidebar.
    groups = parse_index(readme)
    listed = {i["href"] for g in groups for i in iter_items(g["items"])}
    expected = {p.as_posix() for p in english_pages()}
    if listed != expected:
        missing, extra = sorted(expected - listed), sorted(listed - expected)
        sys.exit(f"{readme_path.name}: 'All pages' list doesn't match docs/ (missing: {missing}, unknown: {extra})")
    nav = []
    for group in groups:
        hrefs = [i["href"] for i in iter_items(group["items"])]
        section = hrefs[0].split("/")[0] if hrefs else ""
        if section not in SECTIONS or any(not h.startswith(section + "/") for h in hrefs):
            sys.exit(f"{readme_path.name}: group '{group['name']}' must only list pages under one of {SECTIONS}")
        write_toc(dest / section / "toc.yml", group["items"], section + "/")
        nav.append({"name": group["name"], "href": f"{section}/"})
    write_toc(dest / "toc.yml", nav, "")

    # The template, plus a token.json with this language's UI labels ("Search", "In this
    # article", ...) from docs/site/tokens.json. docfx merges a partial token.json over the
    # built-in English one, so a label missing there stays English.
    template = OUT / "template" / code
    if template.exists():
        shutil.rmtree(template)
    shutil.copytree(DOCS / "site" / "template", template)
    tokens = json.loads((DOCS / "site" / "tokens.json").read_text(encoding="utf-8")).get(code)
    if tokens:
        (template / "token.json").write_text(json.dumps(tokens, ensure_ascii=False, indent=2), encoding="utf-8")

    config = {
        "build": {
            "content": [{"files": ["**/*.{md,yml}"], "exclude": ["obj/**"]}],
            "resource": [{"files": [f"**/*{ext}" for ext in sorted(ASSET_EXTS)]}],
            "output": str((OUT / "_site" / outdir).resolve()),
            "template": ["default", "modern", str(template.resolve())],
            "globalMetadata": {
                "_appName": "Flare",
                "_appTitle": "Flare",
                "_appLogoPath": "logo.png",
                "_appFaviconPath": "favicon.ico",
                "_appFooter": f'Flare is MIT-licensed. <a href="{GITHUB}">Source on GitHub</a>.',
                "_lang": code,
                "_enableSearch": True,
                # Pages are built from a staged copy, so docfx's "Edit this page" would point
                # at artifacts/; the language picker script adds a correct one instead.
                "_disableContribution": True,
                "pdf": False,
            },
            "sitemap": {"baseUrl": SITE_URL + (outdir + "/" if outdir else "")},
            "markdownEngineProperties": {"alerts": {"TIP": "alert alert-info"}},
        }
    }
    (dest / "docfx.json").write_text(json.dumps(config, indent=2, ensure_ascii=False), encoding="utf-8")
    return dest


def iter_items(items):
    for it in items:
        if "href" in it:
            yield it
        yield from iter_items(it.get("items", []))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--lang", action="append", help="only build these language codes (repeatable)")
    ap.add_argument("--strict", action="store_true", help="treat docfx warnings as errors")
    ap.add_argument("--stage-only", action="store_true", help="stage sources without running docfx")
    args = ap.parse_args()

    langs = [l for l in LANGS if not args.lang or l[0] in args.lang]
    if not args.stage_only and (OUT / "_site").exists() and not args.lang:
        shutil.rmtree(OUT / "_site")

    for code, suffix, outdir in langs:
        dest = stage(code, suffix, outdir)
        print(f"staged {code} -> {dest.relative_to(REPO)}")
        if args.stage_only:
            continue
        cmd = ["dotnet", "docfx", str(dest / "docfx.json")]
        if args.strict:
            cmd.append("--warningsAsErrors")
        if subprocess.run(cmd, cwd=REPO).returncode != 0:
            print(f"docfx failed for {code}", file=sys.stderr)
            return 1

    if not args.stage_only:
        (OUT / "_site" / ".nojekyll").write_text("")
        print(f"site -> {(OUT / '_site').relative_to(REPO)}  (preview: dotnet docfx serve {(OUT / '_site').relative_to(REPO)})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
