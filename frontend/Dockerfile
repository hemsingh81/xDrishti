# syntax=docker/dockerfile:1
# Builds the React app and serves it with Caddy (xd-proxy), which also reverse-proxies /api to xd-api.
ARG NODE_IMAGE=docker.io/library/node:24-alpine
ARG CADDY_IMAGE=docker.io/library/caddy:2.11.7-alpine

FROM ${NODE_IMAGE} AS build
WORKDIR /app
COPY package.json package-lock.json .npmrc ./
RUN --mount=type=cache,id=xd-npm,target=/root/.npm npm ci --no-audit
COPY . .
RUN npm run build

FROM ${CADDY_IMAGE} AS proxy
# We listen on 8080, so drop the binary's cap_net_bind_service: lets it run as non-root with no-new-privileges.
RUN apk add --no-cache libcap && setcap -r /usr/bin/caddy && apk del libcap
COPY --from=build /app/dist /srv
USER 65534:65534
# Caddyfile is mounted from deploy/caddy at runtime (config changes without rebuilding).
EXPOSE 8080
