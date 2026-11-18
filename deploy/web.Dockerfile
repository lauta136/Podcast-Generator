# Caddy with the built web app inside. It serves the site, forwards /api to the API container, and gets the
# HTTPS certificate by itself. Built from the repository root:
#   docker build -f deploy/web.Dockerfile .

FROM --platform=$BUILDPLATFORM node:24-alpine AS build
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM caddy:2
COPY deploy/Caddyfile /etc/caddy/Caddyfile
COPY --from=build /web/dist /srv
