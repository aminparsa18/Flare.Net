<script lang="ts">
	// Read-only code-view for exception.stacktrace values (span events, /errors occurrences,
	// log exception callouts) - replaces the plain `<pre>` blob those three call sites used to
	// render. CodeMirror 6, not Monaco: a multi-frame .NET/Java/Node stack trace is a handful
	// of KB at most, so full Monaco (a whole language-server-grade editor) would be a lot of
	// bundle for a read-only viewer - CM6's view+state core is a couple orders of magnitude
	// smaller and this only needs line numbers, wrapping, and a scrollable, read-only surface.
	//
	// No language grammar is pulled in - there's no such thing as a "stack trace grammar" that
	// covers every OTel SDK's exception.stacktrace format (.NET/Java/Node/Python/... all differ,
	// and OTel's semantic conventions don't specify one - it's just whatever the language's
	// native exception-to-string call produces). Instead this is a small hand-rolled tokenizer
	// tuned for the shapes this dashboard actually sees: .NET's `Exception.ToString()` format
	// primarily (`at Type.Method(args) in file:line N`), with a Java/Node fallback (`at
	// symbol(file:line)` - location inside the parens instead of after them). Anything that
	// doesn't match either shape is left unstyled rather than guessed at.
	import { EditorState, RangeSetBuilder } from '@codemirror/state';
	import { Decoration, type DecorationSet, EditorView, ViewPlugin, type ViewUpdate, drawSelection, highlightSpecialChars, lineNumbers } from '@codemirror/view';

	let { trace, maxHeight = '20rem', class: className = '' }: { trace: string; maxHeight?: string; class?: string } = $props();

	let container: HTMLDivElement | undefined = $state();
	let view: EditorView | undefined;

	const causedByLine = Decoration.line({ class: 'cm-stacktrace-caused-by' });
	const CAUSED_BY_RE = /^\s*(?:--->|caused by:|inner exception)/i;

	// A frame line's keyword prefix - "at" (.NET/Java/Node), "in"/"from" (seen in a few other
	// ecosystems' single-line frame formats). Anchored to the start of the line so it can never
	// match a header line's message text (e.g. "... lock in time.") mid-string.
	const FRAME_KEYWORD_RE = /^(\s*)(at|in|from)\s+/;
	// A header line: "<Namespace.>TypeException: message" or "...Error: message" - the first
	// line of an exception, and of each inner exception after a "Caused by"/"--->" separator.
	const HEADER_RE = /^(\s*)([A-Za-z_][\w.+]*(?:Exception|Error))(\s*:\s*)(.*)$/;

	const markDeco: Record<string, Decoration> = {
		keyword: Decoration.mark({ class: 'cm-stacktrace-keyword' }),
		symbol: Decoration.mark({ class: 'cm-stacktrace-symbol' }),
		args: Decoration.mark({ class: 'cm-stacktrace-args' }),
		location: Decoration.mark({ class: 'cm-stacktrace-location' }),
		exceptionType: Decoration.mark({ class: 'cm-stacktrace-exception-type' })
	};

	type Token = readonly [from: number, to: number, kind: keyof typeof markDeco];

	// `   at Namespace.Type.Method(ArgType arg, ...) in /path/File.cs:line 228` (args in
	// parens, location after " in ") vs `    at symbol (file.js:10:5)` (location IN the
	// parens, no args) - told apart by whether " in " follows the closing paren, since that's
	// the one part of the shape that actually differs between the two families.
	function tokenizeFrameLine(lineFrom: number, text: string): Token[] | null {
		const kw = FRAME_KEYWORD_RE.exec(text);
		if (!kw) return null;

		const tokens: Token[] = [[lineFrom + kw[1].length, lineFrom + kw[0].length, 'keyword']];
		const rest = text.slice(kw[0].length);
		const base = lineFrom + kw[0].length;

		const dotnet = /^(.+?)\(([^()]*)\)(\s+in\s+)(.+)$/.exec(rest);
		if (dotnet) {
			const [, method, args, inKeyword, location] = dotnet;
			let pos = base;
			tokens.push([pos, (pos += method.length), 'symbol']);
			pos += 1; // "("
			if (args) tokens.push([pos, (pos += args.length), 'args']);
			pos += 1; // ")"
			tokens.push([pos, (pos += inKeyword.length), 'keyword']);
			tokens.push([pos, pos + location.length, 'location']);
			return tokens;
		}

		const paren = /^(.+?)\(([^()]*)\)\s*$/.exec(rest);
		if (paren) {
			const [, symbol, inner] = paren;
			let pos = base;
			tokens.push([pos, (pos += symbol.length), 'symbol']);
			pos += 1; // "("
			if (inner) {
				const looksLikeLocation = /[:.]\d+(?::\d+)?$/.test(inner) || /\.\w+$/.test(inner);
				tokens.push([pos, pos + inner.length, looksLikeLocation ? 'location' : 'args']);
			}
			return tokens;
		}

		// No parens at all (e.g. a bare native-frame placeholder) - tint the rest as a symbol
		// rather than leave it unstyled, since it's still part of "who threw this."
		if (rest.trim()) tokens.push([base, lineFrom + text.length, 'symbol']);
		return tokens;
	}

	function tokenizeHeaderLine(lineFrom: number, text: string): Token[] | null {
		const m = HEADER_RE.exec(text);
		if (!m) return null;
		const [, lead, type] = m;
		const start = lineFrom + lead.length;
		return [[start, start + type.length, 'exceptionType']];
	}

	function buildDecorations(state: EditorState): DecorationSet {
		const builder = new RangeSetBuilder<Decoration>();
		for (let i = 1; i <= state.doc.lines; i++) {
			const line = state.doc.line(i);
			const text = line.text;
			if (CAUSED_BY_RE.test(text)) {
				builder.add(line.from, line.from, causedByLine);
				continue;
			}
			const tokens = tokenizeFrameLine(line.from, text) ?? tokenizeHeaderLine(line.from, text);
			for (const [from, to, kind] of tokens ?? []) {
				if (to > from) builder.add(from, to, markDeco[kind]);
			}
		}
		return builder.finish();
	}

	const stackTraceHighlighter = ViewPlugin.fromClass(
		class {
			decorations: DecorationSet;
			constructor(view: EditorView) {
				this.decorations = buildDecorations(view.state);
			}
			update(update: ViewUpdate) {
				if (update.docChanged) this.decorations = buildDecorations(update.state);
			}
		},
		{ decorations: (plugin) => plugin.decorations }
	);

	$effect(() => {
		if (!container) return;

		// var()-referencing theme (not resolved colors) so light/dark keeps working across the
		// .dark class toggle on <html> without re-creating the view - same trick the rest of
		// the dashboard gets for free from Tailwind, done by hand here since CodeMirror's theme
		// is CSS injected once at view creation, not live Tailwind classes. Built inside the
		// effect (not hoisted) so it re-reads the current `maxHeight` prop if the view is
		// re-created - a hoisted const would only ever see the value from first render.
		const theme = EditorView.theme({
			'&': { color: 'var(--foreground)', backgroundColor: 'var(--muted)', fontSize: '0.75rem' },
			'.cm-content': { fontFamily: 'var(--font-mono)', padding: '0.5rem 0', caretColor: 'transparent' },
			'.cm-line': { padding: '0 0.75rem' },
			'.cm-gutters': { backgroundColor: 'var(--muted)', color: 'var(--muted-foreground)', border: 'none' },
			'.cm-activeLineGutter': { backgroundColor: 'transparent' },
			'.cm-scroller': { overflow: 'auto', maxHeight },
			'&.cm-focused': { outline: 'none' },
			// The actual "split color" hierarchy: keyword/punctuation and the argument list are
			// boilerplate (dimmed), the symbol (what threw) stays at full contrast and a touch
			// heavier so it pops out of that dimmed noise, and the location (file:line - the one
			// piece of a frame you'd actually click or copy) gets `--chart-1`. NOT `--primary` -
			// this theme's `--primary`/`--foreground` are both achromatic near-black (see
			// layout.css), so `--primary` sat almost on top of the symbol color and didn't read
			// as a distinct token at all. `--chart-1` is the one blue this theme actually has,
			// otherwise reserved for data-viz - borrowing it here is deliberate: the location is
			// the one part of a frame that behaves like a reference, so it earns the one real
			// accent color available instead of yet another shade of gray.
			'.cm-stacktrace-keyword': { color: 'var(--muted-foreground)' },
			'.cm-stacktrace-symbol': { color: 'var(--foreground)', fontWeight: '500' },
			'.cm-stacktrace-args': { color: 'var(--muted-foreground)' },
			'.cm-stacktrace-location': { color: 'var(--chart-1)' },
			'.cm-stacktrace-exception-type': { color: 'var(--destructive)', fontWeight: '500' },
			'.cm-stacktrace-caused-by': { color: 'var(--destructive)', fontWeight: '500' }
		});

		view = new EditorView({
			state: EditorState.create({
				doc: trace,
				extensions: [
					lineNumbers(),
					highlightSpecialChars(),
					drawSelection(),
					EditorView.lineWrapping,
					EditorState.readOnly.of(true),
					EditorView.editable.of(false),
					stackTraceHighlighter,
					theme
				]
			}),
			parent: container
		});

		return () => view?.destroy();
	});
</script>

<div bind:this={container} class="overflow-hidden rounded-md border {className}"></div>
