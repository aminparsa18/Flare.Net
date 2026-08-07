# syntax=docker/dockerfile:1

# Build context is src/dashboard itself (unlike the two .NET Dockerfiles, this project
# has no repo-root-level shared build files it needs) - see docker-compose.yml.

FROM node:22-alpine AS build
WORKDIR /app

# package*.json first for layer caching - `npm ci` only reruns when dependencies change.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

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

ENTRYPOINT ["node", "build/index.js"]
