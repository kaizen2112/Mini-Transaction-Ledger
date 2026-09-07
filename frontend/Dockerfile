# Three-stage build on Next.js's standalone output, per docs/08-docker.md §4.
# Requires `output: 'standalone'` in next.config.ts.

FROM node:22-alpine AS deps
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci

FROM node:22-alpine AS build
WORKDIR /app
COPY --from=deps /app/node_modules ./node_modules
COPY . .

# NEXT_PUBLIC_* variables are inlined into the browser bundle at BUILD time,
# not read at container start. Setting this only under docker-compose.yml's
# `environment:` would do nothing for browser code — it has to arrive as a
# build ARG, which is why the compose file passes it under `build.args`.
ARG NEXT_PUBLIC_API_BASE_URL
ENV NEXT_PUBLIC_API_BASE_URL=$NEXT_PUBLIC_API_BASE_URL
RUN npm run build

FROM node:22-alpine AS final
WORKDIR /app
ENV NODE_ENV=production

# The standalone output traces only the dependencies actually used at
# runtime and emits a self-contained server.js, so the final image does not
# carry the full node_modules tree the build stage needed.
COPY --from=build /app/.next/standalone ./
COPY --from=build /app/.next/static ./.next/static
COPY --from=build /app/public ./public

EXPOSE 3000
USER node
CMD ["node", "server.js"]
