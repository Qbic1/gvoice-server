# Reference copy of the deploy-root Dockerfile that builds the Angular client and
# bakes it into the Caddy image. It lives at the DEPLOY ROOT (next to
# docker-compose.yml), NOT inside a single repo, because it needs the client
# sources in ./client. See ../deployment.md for the expected directory layout.
#
# ── Dependencies stage (cached) ─────────────────────────────
FROM node:22-alpine AS deps
WORKDIR /app

# Only copy dependency manifests first so this layer is cached until they change.
COPY client/package.json client/package-lock.json ./
RUN npm ci

# ── Build stage ─────────────────────────────────────────────
FROM node:22-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY client/ .

# OPTIONAL — inject the TURN credential at build time instead of committing it to
# environment.ts. Pass `--build-arg TURN_CREDENTIAL=...` and uncomment the sed line.
# ARG TURN_CREDENTIAL=""
# RUN sed -i "s/credential: ''/credential: '${TURN_CREDENTIAL}'/g" src/environments/environment.ts

# Production build → dist/gvoice-client/browser (static) + /server (SSR, unused here).
RUN npm run build -- --configuration production

# ── Runtime stage (Caddy) ───────────────────────────────────
FROM caddy:latest
COPY Caddyfile /etc/caddy/Caddyfile
COPY --from=build /app/dist/gvoice-client/browser /srv/www
