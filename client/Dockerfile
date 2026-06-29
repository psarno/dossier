FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json .
RUN npm ci
COPY . .
# API_BASE_URL is used at runtime by the SSR server, not at build time
RUN npm run build

FROM node:22-alpine AS final
WORKDIR /app
COPY --from=build /app/dist/dossier ./dist/dossier
COPY --from=build /app/package*.json .
RUN npm ci --omit=dev

EXPOSE 4000
ENV PORT=4000

CMD ["node", "dist/dossier/server/server.mjs"]
