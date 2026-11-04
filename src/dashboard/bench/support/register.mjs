// Loaded with `tsx --import ./bench/support/register.mjs ...` so benchmarks can import the real
// `src/lib/*-api.ts` modules. Those import `$app/env/public`, which only SvelteKit's Vite plugin
// resolves; this hook points it at a stub instead.
import { registerHooks } from 'node:module';

const appEnvPublicStub = new URL('./app-env-public.ts', import.meta.url).href;

registerHooks({
	resolve(specifier, context, nextResolve) {
		return specifier === '$app/env/public' ? { url: appEnvPublicStub, shortCircuit: true } : nextResolve(specifier, context);
	}
});
