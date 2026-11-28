#!/usr/bin/env python3
"""Build the docfx documentation site from docs/ (published to GitHub Pages).

docs/ is written to read well on GitHub, so it can't be fed to docfx as-is. This
script stages a copy per language into artifacts/docs-site/src/<lang>/ and then
runs docfx on each one:

- One site per language: English at the site root, translations under /ru/, /fr/,
  /zh-CN/. A translated page `foo.ru.md` is staged as `foo.md`, so every language
  site has the same URLs (the language picker in docs/site/template relies on it).
  A page with no translation falls back to the English text.
- docs/README.<lang>.md's "All pages" list is turned into the navigation: each
  **bold** group is a navbar section with its own toc.yml and a generated landing
  page (<section>/index.md: the purpose from the quick-links table, then every page
  with its first paragraph as a blurb), and each *italic* subgroup is a sidebar group
  within it. There is still one index to maintain; the build fails if a language's
  index doesn't list every page.
- The home page is generated from docs/site/strings.json (hero, start-here cards,
  section cards), so the README's maintainer notes stay on GitHub.
- Every page gets a meta description (its first paragraph), a "Last updated" date
  (the last commit touching its source file, so CI needs full history), and, after
  docfx runs, canonical/hreflang links and Open Graph/Twitter tags for link previews.
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
import html
import json
import posixpath
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
DEMO_URL = "https://176-223-135-205.sslip.io"

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
        # A section folder (or one of its sub-folders) -> that section's landing page.
        if in_docs.parts and in_docs.parts[0] in SECTIONS:
            return rel_to_page(f"{in_docs.parts[0]}/index.md")
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


def parse_quick_links(readme: str) -> dict[str, str]:
    """The index's quick-links table: section folder -> its one-line purpose ("Learn by doing")."""
    purposes = {}
    for m in re.finditer(r"^\|\s*\[[^\]]+\]\(([a-z-]+)/\)\s*\|\s*([^|]+?)\s*\|", readme, re.M):
        purposes[m.group(1)] = m.group(2)
    return purposes


def page_description(text: str, rel: Path, titles: dict[str, str], limit: int = 180) -> str:
    """Plain-text first paragraph of a page (after its H1), shortened to ~limit characters.

    A link whose text is a file path ("see run-with-aspire.md") reads as the linked page's
    title from the docs index (`titles`: docs-relative path -> title) instead.
    """
    text = re.sub(r"<!--.*?-->", "", text, flags=re.S)
    para: list[str] = []
    seen_h1 = False
    for line in text.splitlines():
        s = line.strip()
        if not seen_h1:
            seen_h1 = s.startswith("# ")
            continue
        if not s:
            if para:
                break
            continue
        if s.startswith(("#", ">", "|", "!", "<", "```", "~~~", "- ", "* ", "1.")) and not para:
            continue
        para.append(s)
    out = " ".join(para)
    def link_text(m: re.Match) -> str:
        label, target = m.group(1), m.group(2).split("#")[0]
        if re.fullmatch(r"`?[\w./-]+\.md`?", label) and target:
            linked = posixpath.normpath(LANG_SUFFIX_RE.sub(".md", (rel.parent / target).as_posix()))
            return titles.get(linked, label)
        return label

    out = re.sub(r"!?\[([^\]]*)\]\(([^)]*)\)", link_text, out)  # links -> their text
    out = re.sub(r"[*_`]{1,3}([^*_`]+)[*_`]{1,3}", r"\1", out)  # emphasis / code
    out = re.sub(r"\s+", " ", out).strip()
    if len(out) > limit:
        cut = out[:limit]
        out = (cut.rsplit(" ", 1)[0] if " " in cut[limit // 2:] else cut).rstrip(",;:—- ") + "…"
    return out


def md_text(s: str) -> str:
    """Escape plain text for use inside generated Markdown."""
    return html.escape(s, quote=False).replace("*", r"\*").replace("[", r"\[").replace("]", r"\]")


def front_matter(meta: dict) -> str:
    return "---\n" + "".join(f"{k}: {json.dumps(v, ensure_ascii=False)}\n" for k, v in meta.items()) + "---\n\n"


def git_last_updated() -> dict[str, str]:
    """Repo-relative path -> date (YYYY-MM-DD) of the last commit touching it, for files under docs/."""
    log = subprocess.run(["git", "log", "--format=%x00%cs", "--name-only", "--", "docs"],
                         cwd=REPO, capture_output=True, text=True, check=True).stdout
    dates: dict[str, str] = {}
    current = ""
    for line in log.splitlines():
        if line.startswith("\x00"):
            current = line[1:]
        elif line and line not in dates:
            dates[line] = current
    return dates


def section_page(group: dict, section: str, purpose: str, descriptions: dict[str, str]) -> str:
    """A section's landing page: its purpose, then every page in it with a one-line description."""
    out = [front_matter({"description": purpose}), f"# {group['name']}\n\n", f"{md_text(purpose)}\n\n"]

    def pages(items: list[dict]) -> str:
        rows = []
        for it in items:
            href = it["href"][len(section) + 1:]
            desc = descriptions.get(it["href"], "")
            rows.append(f'<li><a href="{html.escape(href)}">{html.escape(it["name"])}</a>'
                        + (f"<p>{html.escape(desc)}</p>" if desc else "") + "</li>")
        return '<ul class="flare-pages">\n' + "\n".join(rows) + "\n</ul>\n"

    direct = [it for it in group["items"] if "href" in it]
    if direct:
        out.append(pages(direct))
    for sub in (it for it in group["items"] if "href" not in it):
        out.append(f"\n## {sub['name']}\n\n")
        out.append(pages(sub["items"]))
    return "".join(out)


SECTION_ICONS = {"tutorials": "mortarboard", "how-to": "tools", "reference": "journal-code", "explanation": "lightbulb"}
SCREENSHOT_LANG = {"en": "en", "ru": "ru", "fr": "en", "zh-CN": "ch"}  # docs/screenshots/*-<x>.webp variants


def home_page(code: str, s: dict, groups: list[dict], sections: list[str], purposes: dict[str, str]) -> str:
    e = lambda v: html.escape(v, quote=True)  # noqa: E731
    card = lambda href, icon, title, text: (  # noqa: E731
        f'<div class="col-md-6 col-lg-4"><a class="card h-100 flare-card" href="{href}"><div class="card-body">'
        f'<i class="bi bi-{icon}"></i><h3 class="h5 card-title">{e(title)}</h3>'
        f'<p class="card-text text-secondary">{e(text)}</p></div></a></div>')
    start = [
        card("tutorials/getting-started.md", "rocket-takeoff", s["cardTryTitle"], s["cardTryText"]),
        card("how-to/instrument/send-dotnet-telemetry.md", "broadcast", s["cardInstrumentTitle"], s["cardInstrumentText"]),
        card("how-to/deploy/run-standalone.md", "hdd-stack", s["cardDeployTitle"], s["cardDeployText"]),
    ]
    browse = [card(f"{sec}/index.md", SECTION_ICONS[sec], g["name"], purposes.get(sec, ""))
              for g, sec in zip(groups, sections)]
    browse_cols = [c.replace("col-md-6 col-lg-4", "col-md-6 col-lg-3") for c in browse]
    meta = {"title": s["homeTitle"], "description": s["tagline"], "_layout": "landing", "_disableToc": True,
            "_disableAffix": True, "_disableBreadcrumb": True, "_disableNextArticle": True}
    return front_matter(meta) + "\n".join([
        '<div class="flare-hero">',
        f'<h1>{e(s["homeTitle"])}</h1>',
        f'<p class="lead">{e(s["tagline"])}</p>',
        f'<p class="text-secondary">{e(s["pitch"])}</p>',
        '<p class="flare-hero-actions">',
        f'<a class="btn btn-primary btn-lg" href="tutorials/getting-started.md">{e(s["getStarted"])}</a>',
        f'<a class="btn btn-outline-secondary btn-lg" href="{DEMO_URL}">{e(s["liveDemo"])}</a>',
        f'<a class="btn btn-outline-secondary btn-lg" href="{GITHUB}"><i class="bi bi-github"></i> GitHub</a>',
        '</p>',
        '</div>',
        f'<h2>{e(s["startHere"])}</h2>',
        '<div class="row g-3">', *start, '</div>',
        f'<img class="flare-screenshot" src="screenshots/logs-{SCREENSHOT_LANG.get(code, "en")}.webp" alt="{e(s["screenshotAlt"])}">',
        f'<h2>{e(s["browse"])}</h2>',
        '<div class="row g-3">', *browse_cols, '</div>',
        "",
    ])


OG_LOCALE = {"en": "en_US", "ru": "ru_RU", "fr": "fr_FR", "zh-CN": "zh_CN"}


def add_head_tags(site: Path, code: str, outdir: str) -> None:
    """Add canonical/hreflang links and Open Graph/Twitter tags to every page of one language site.

    The docfx modern template has no hook for these, so they're added to the built HTML.
    """
    for page in site.rglob("*.html"):
        rel = page.relative_to(site).as_posix()
        if rel == "toc.html" or rel.endswith("/toc.html") or (outdir == "" and rel.split("/")[0] in {d for _, _, d in LANGS if d}):
            continue
        doc = page.read_text(encoding="utf-8")
        if 'property="og:title"' in doc or "</head>" not in doc:
            continue
        path = "" if rel == "index.html" else rel
        url = lambda d: SITE_URL + (d + "/" if d else "") + path  # noqa: E731
        title = re.search(r"<title>(.*?)</title>", doc, re.S)
        desc = re.search(r'<meta name="description" content="([^"]*)"', doc)
        tags = [f'<link rel="canonical" href="{url(outdir)}">']
        tags += [f'<link rel="alternate" hreflang="{c}" href="{url(d)}">' for c, _, d in LANGS]
        tags += [f'<link rel="alternate" hreflang="x-default" href="{url("")}">',
                 '<meta property="og:type" content="website">',
                 '<meta property="og:site_name" content="Flare">',
                 f'<meta property="og:locale" content="{OG_LOCALE[code]}">',
                 f'<meta property="og:url" content="{url(outdir)}">',
                 f'<meta property="og:image" content="{SITE_URL}social-preview.png">',
                 '<meta name="twitter:card" content="summary_large_image">']
        if title:
            t = title.group(1).strip()
            tags += [f'<meta property="og:title" content="{t}">', f'<meta name="twitter:title" content="{t}">']
        if desc:
            tags += [f'<meta property="og:description" content="{desc.group(1)}">',
                     f'<meta name="twitter:description" content="{desc.group(1)}">']
        page.write_text(doc.replace("</head>", "    " + "\n    ".join(tags) + "\n  </head>", 1), encoding="utf-8")


def yaml_str(s: str) -> str:
    return json.dumps(s, ensure_ascii=False)  # JSON strings are valid YAML scalars


def write_toc(path: Path, items: list[dict], prefix: str, indent: int = 0) -> list[str]:
    lines = []
    pad = "  " * indent
    for it in items:
        lines.append(f"{pad}- name: {yaml_str(it['name'])}")
        for key in ("href", "topicHref"):
            if key in it:
                href = it[key][len(prefix):] if it[key].startswith(prefix) else it[key]
                lines.append(f"{pad}  {key}: {yaml_str(href)}")
        if it.get("items"):
            lines.append(f"{pad}  items:")
            lines.extend(write_toc(path, it["items"], prefix, indent + 2))
    if indent == 0:
        path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return lines


def stage(code: str, suffix: str, outdir: str, updated: dict[str, str]) -> Path:
    dest = OUT / "src" / code
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True)
    all_strings = json.loads((DOCS / "site" / "strings.json").read_text(encoding="utf-8"))
    strings = {**all_strings["en"], **all_strings.get(code, {})}

    # Pages: each gets a description (its first paragraph: the meta description, the share
    # preview text and its blurb on the section page) and the date its source last changed.
    readme_path = DOCS / f"README{suffix}.md"
    readme = readme_path.read_text(encoding="utf-8")
    purposes = parse_quick_links(readme)
    groups = parse_index(readme)
    titles = {i["href"]: i["name"] for g in groups for i in iter_items(g["items"])}

    descriptions: dict[str, str] = {}
    for rel in english_pages():
        translated = DOCS / rel.with_name(rel.stem + suffix + ".md")
        src = translated if translated.exists() else DOCS / rel
        text = src.read_text(encoding="utf-8")
        descriptions[rel.as_posix()] = desc = page_description(text, rel, titles)
        body = rewrite_markdown(text, rel).rstrip() + "\n"
        if date := updated.get(src.relative_to(REPO).as_posix()):
            body += (f'\n<p class="flare-updated"><i class="bi bi-clock-history"></i> '
                     f'{html.escape(strings["lastUpdated"])}: <time datetime="{date}">{date}</time></p>\n')
        target = dest / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(front_matter({"description": desc} if desc else {}) + body, encoding="utf-8")

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
    listed = {i["href"] for g in groups for i in iter_items(g["items"])}
    expected = {p.as_posix() for p in english_pages()}
    if listed != expected:
        missing, extra = sorted(expected - listed), sorted(listed - expected)
        sys.exit(f"{readme_path.name}: 'All pages' list doesn't match docs/ (missing: {missing}, unknown: {extra})")
    nav, sections = [], []
    for group in groups:
        hrefs = [i["href"] for i in iter_items(group["items"])]
        section = hrefs[0].split("/")[0] if hrefs else ""
        if section not in SECTIONS or any(not h.startswith(section + "/") for h in hrefs):
            sys.exit(f"{readme_path.name}: group '{group['name']}' must only list pages under one of {SECTIONS}")
        sections.append(section)
        (dest / section / "index.md").write_text(
            section_page(group, section, purposes.get(section, ""), descriptions), encoding="utf-8")
        overview = {"name": strings["overview"], "href": f"{section}/index.md"}
        write_toc(dest / section / "toc.yml", [overview, *group["items"]], section + "/")
        nav.append({"name": group["name"], "href": f"{section}/", "topicHref": f"{section}/index.md"})
    write_toc(dest / "toc.yml", nav, "")

    # Home page, generated (docs/README*.md stays the GitHub index; its maintainer notes stay there).
    (dest / "index.md").write_text(home_page(code, strings, groups, sections, purposes), encoding="utf-8")

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

    updated = git_last_updated()
    for code, suffix, outdir in langs:
        dest = stage(code, suffix, outdir, updated)
        print(f"staged {code} -> {dest.relative_to(REPO)}")
        if args.stage_only:
            continue
        cmd = ["dotnet", "docfx", str(dest / "docfx.json")]
        if args.strict:
            cmd.append("--warningsAsErrors")
        if subprocess.run(cmd, cwd=REPO).returncode != 0:
            print(f"docfx failed for {code}", file=sys.stderr)
            return 1
        add_head_tags(OUT / "_site" / outdir if outdir else OUT / "_site", code, outdir)

    if not args.stage_only:
        (OUT / "_site" / ".nojekyll").write_text("")
        print(f"site -> {(OUT / '_site').relative_to(REPO)}  (preview: dotnet docfx serve {(OUT / '_site').relative_to(REPO)})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
