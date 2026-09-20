# syntax=docker/dockerfile:1
# check=skip=SecretsUsedInArgOrEnv
# Multi-stage: SPA (Vite) → API publish → non-root aspnet runtime serving both.
# Server-side secrets are runtime env only. VITE_SUPABASE_ANON_KEY is the public
# anon key (baked into the SPA on purpose) — never the service-role key.

# ---------- SPA ----------
FROM node:22-alpine AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
ARG VITE_SUPABASE_URL
ARG VITE_SUPABASE_ANON_KEY
ARG VITE_API_BASE_URL=/
ENV VITE_SUPABASE_URL=$VITE_SUPABASE_URL \
    VITE_SUPABASE_ANON_KEY=$VITE_SUPABASE_ANON_KEY \
    VITE_API_BASE_URL=$VITE_API_BASE_URL
RUN if [ -z "$VITE_SUPABASE_URL" ] || [ -z "$VITE_SUPABASE_ANON_KEY" ]; then \
      echo "VITE_SUPABASE_URL and VITE_SUPABASE_ANON_KEY build-args are required (public anon key, never the service role)."; \
      exit 1; \
    fi \
 && npm run build

# ---------- API publish ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/LifeOs.Domain/LifeOs.Domain.csproj src/LifeOs.Domain/
COPY src/LifeOs.Application/LifeOs.Application.csproj src/LifeOs.Application/
COPY src/LifeOs.Infrastructure/LifeOs.Infrastructure.csproj src/LifeOs.Infrastructure/
COPY src/LifeOs.Api/LifeOs.Api.csproj src/LifeOs.Api/
RUN dotnet restore src/LifeOs.Api/LifeOs.Api.csproj
COPY db/ db/
COPY src/LifeOs.Domain/ src/LifeOs.Domain/
COPY src/LifeOs.Application/ src/LifeOs.Application/
COPY src/LifeOs.Infrastructure/ src/LifeOs.Infrastructure/
COPY src/LifeOs.Api/ src/LifeOs.Api/
RUN dotnet publish src/LifeOs.Api/LifeOs.Api.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore \
    --no-self-contained

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .
COPY --from=web --chown=$APP_UID:$APP_UID /web/dist ./wwwroot
USER $APP_UID
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_EnableDiagnostics=0
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=25s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "LifeOs.Api.dll"]
