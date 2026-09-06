import tailwindcss from '@tailwindcss/vite';
import adapter from '@sveltejs/adapter-node';
import { sveltekit } from '@sveltejs/kit/vite';
import { paraglideVitePlugin } from '@inlang/paraglide-js';
import { defineConfig } from 'vite';

export default defineConfig({
	plugins: [
		// Regenerates src/lib/paraglide/* (messages.ts, runtime.ts, server.ts) from
		// project.inlang/ + messages/*.json on every dev/build/preview - must run before
		// sveltekit() touches module resolution. `npm run codegen` (predev/prebuild/precheck)
		// regenerates the same output for `svelte-check`, which never runs Vite - see
		// package.json and src/dashboard/README.md's Internationalization section.
		paraglideVitePlugin({
			project: './project.inlang',
			outdir: './src/lib/paraglide',
			strategy: ['cookie', 'preferredLanguage', 'baseLocale']
		}),
		tailwindcss(),
		sveltekit({
			compilerOptions: {
				// Force runes mode for the project, except for libraries. Can be removed in svelte 6.
				runes: ({ filename }) => filename.split(/[/\\]/).includes('node_modules') ? undefined : true
			},

			// adapter-node, not adapter-auto: the docker-compose.yml v1 roadmap item is the
			// deployment target (see README.md's "Building" note). This has to be a real
			// Node server, not adapter-static - `$env/dynamic/public` (src/lib/api.ts's
			// PUBLIC_API_URL) is resolved per-request at runtime, which only a running
			// server can do; a static prerender would bake in whatever value happened to be
			// set at build time instead of at `docker compose up` time.
			adapter: adapter()
		})
	]
});
