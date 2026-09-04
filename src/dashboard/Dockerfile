# syntax=docker/dockerfile:1

# Build context is the repo root (not this directory), unlike before MemoryPack TypeScript
# codegen existed - this needs Flare.Api's own source available to a .NET-SDK build stage,
# same reasoning src/Flare.Api/Dockerfile's own header comment gives for its repo-root
# context (Directory.Build.props/.Packages.props/global.json + ProjectReferences). See
# docker-compose.yml.

# ---- Stage 1: MemoryPack TypeScript codegen - needs the .NET SDK, not Node -----------
# Mirrors src/Flare.Api/Dockerfile's own restore/copy layering. Only builds Flare.Api (a
# `dotnet build`, not `publish` - nothing here runs the API, this stage exists purely for
# its side effect: MemoryPackGenerator_TypeScriptOutputDirectory writes
# src/dashboard/src/lib/generated/memorypack/*.ts as part of compiling Flare.Api.Model's
# [GenerateTypeScript] types - see ADR-0016 and Flare.Api.csproj's own comment on that
# MSBuild property).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS codegen
WORKDIR /source

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/Flare.Api/Flare.Api.csproj src/Flare.Api/
COPY src/Flare.ServiceDefaults/Flare.ServiceDefaults.csproj src/Flare.ServiceDefaults/
COPY src/Flare.Identity/Flare.Identity.csproj src/Flare.Identity/
RUN dotnet restore src/Flare.Api/Flare.Api.csproj

COPY src/Flare.Api/ src/Flare.Api/
COPY src/Flare.ServiceDefaults/ src/Flare.ServiceDefaults/
COPY src/Flare.Identity/ src/Flare.Identity/
RUN dotnet build src/Flare.Api/Flare.Api.csproj -c Release --no-restore

# ---- Stage 2: the actual SvelteKit build - Node only, no .NET SDK here on purpose -----
FROM node:22-alpine AS build
WORKDIR /app

# package*.json first for layer caching - `npm ci` only reruns when dependencies change.
COPY src/dashboard/package.json src/dashboard/package-lock.json ./
RUN npm ci

COPY src/dashboard/ .
# The codegen stage's output, copied in directly - this stage never runs `dotnet` itself.
COPY --from=codegen /source/src/dashboard/src/lib/generated ./src/lib/generated

# `npx vite build` directly, not `npm run build` - deliberately bypasses the `prebuild`
# npm lifecycle hook (predev/prebuild/precheck all shell out to `dotnet build` against
# Flare.Api - see package.json and ADR-0016). This stage has no .NET SDK on purpose (the
# codegen stage above already produced the files `prebuild` would otherwise regenerate),
# so running it here would fail exactly the way it originally did in CI: `npm run build`
# invoking a nonexistent `dotnet` binary (exit 127) - confirmed live via a real
# `docker-publish.yml` run before this multi-stage split existed.
RUN npx vite build

# `npm ci --omit=dev` after the build (not before): svelte-kit sync / vite build need
# the devDependencies (svelte, vite, the adapter, tailwind, etc.) to run, but the
# adapter-node output in build/ is plain Node - only `dependencies` (currently none
# beyond what's bundled) are needed at runtime.
RUN npm ci --omit=dev

FROM node:22-alpine AS final
WORKDIR /app
ENV NODE_ENV=production
COPY --from=build /app/build ./build
COPY --from=build /app/node_modules ./node_modules
COPY --from=build /app/package.json ./package.json

# adapter-node's own defaults (see https://svelte.dev/docs/kit/adapter-node) - listens
# on 0.0.0.0:3000 unless PORT/HOST are overridden.
ENV PORT=3000
EXPOSE 3000

# Same reasoning as Flare.Api/Dockerfile's HEALTHCHECK comment - baked in here too so
# `.State.Health` is populated regardless of orchestrator, matching Aspire's own
# .WithHttpHealthCheck("/") for this resource. No bash/`/dev/tcp` trick needed here:
# node:22-alpine's busybox includes wget, unlike the .NET images above.
HEALTHCHECK --interval=5s --timeout=5s --retries=20 CMD wget -q --spider http://127.0.0.1:3000/ || exit 1

ENTRYPOINT ["node", "build/index.js"]
