# syntax=docker/dockerfile:1
#
# Single-artifact container for Planning Poker: one image where the .NET API serves the
# REST surface, the SignalR hub, and the Angular SPA from wwwroot — the same shape as the App Service
# deploy. Single instance only (in-process SignalR + local SQLite); scale up, not out.

# --- Build stage: .NET SDK + Node (the publish target runs `npm ci` + `ng build`) ------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG NODE_MAJOR=20
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl ca-certificates gnupg \
    && curl -fsSL https://deb.nodesource.com/setup_${NODE_MAJOR}.x | bash - \
    && apt-get install -y --no-install-recommends nodejs \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src

# Restore npm deps first so node_modules is cached across source-only changes. The publish target
# skips its own `npm ci` when node_modules already exists, then runs the production `ng build`.
COPY frontend/package.json frontend/package-lock.json ./frontend/
RUN cd frontend && npm ci

# Now the source. dotnet publish restores + builds the API and (via the .csproj target) the SPA,
# emitting the SPA into /app/wwwroot.
COPY . .
RUN dotnet publish backend/src/PlanningPoker.Api/PlanningPoker.Api.csproj -c Release -o /app

# --- Runtime stage: ASP.NET runtime only (no SDK/Node) --------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Persistent SQLite lives on a mounted volume at /data (the app creates the dir on startup). WORKDIR
# is the content root, so appsettings*.json load correctly (no content-root pitfall, cf. run scripts).
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    ConnectionStrings__Default="Data Source=/data/planningpoker.db"

# Pre-create the data dir and run as the image's non-root user (uid 1654).
RUN mkdir -p /data && chown -R app:app /data /app
USER app

EXPOSE 8080
ENTRYPOINT ["dotnet", "PlanningPoker.Api.dll"]
