---
name: svelte-code-writer
description: >-
  Write, review, or debug Svelte 5 / SvelteKit code using the official
  `@sveltejs/mcp` CLI for up-to-date documentation lookup and an autofixer
  that catches common issues (including rune usage like $state/$derived/$effect).
  USE FOR: writing new Svelte/SvelteKit components or routes, reviewing or
  debugging existing .svelte files, uncertain rune/syntax questions, validating
  code before finalizing it. INVOKES: `npx @sveltejs/mcp list-sections`,
  `npx @sveltejs/mcp get-documentation`, `npx @sveltejs/mcp svelte-autofixer`.
metadata:
  author: user
  version: "0.0.1"
---

# Svelte Code Writer

## CLI tools

You have access to `@sveltejs/mcp` CLI for Svelte-specific assistance. Use these commands via `npx`:

### List documentation sections

```bash
npx @sveltejs/mcp list-sections
```

Lists all available Svelte 5 and SvelteKit documentation sections with titles and paths.

### Get documentation

```bash
npx @sveltejs/mcp get-documentation "<section1>,<section2>,..."
```

Retrieves full documentation for specified sections. Use after `list-sections` to fetch relevant docs.

**Example:**

```bash
npx @sveltejs/mcp get-documentation "$state,$derived,$effect"
```

### Svelte autofixer

```bash
npx @sveltejs/mcp svelte-autofixer "<code_or_path>" [options]
```

Analyzes Svelte code and suggests fixes for common issues.

**Options:**

- `--async` - Enable async Svelte mode (default: false)
- `--svelte-version` - Target version: 4 or 5 (default: 5)

**Examples:**

```bash
# Analyze inline code (escape $ as \$)
npx @sveltejs/mcp svelte-autofixer '<script>let count = \$state(0);</script>'

# Analyze a file
npx @sveltejs/mcp svelte-autofixer ./src/lib/Component.svelte

# Target Svelte 4
npx @sveltejs/mcp svelte-autofixer ./Component.svelte --svelte-version 4
```

**Important:** When passing code with runes (`$state`, `$derived`, etc.) via the terminal, escape the `$` character as `\$` to prevent shell variable substitution.

## Workflow

1. **Uncertain about syntax?** Run `list-sections` then `get-documentation` for relevant topics
2. **Reviewing/debugging?** Run `svelte-autofixer` on the code to detect issues
3. **Always validate** - Run `svelte-autofixer` before finalizing any Svelte component
