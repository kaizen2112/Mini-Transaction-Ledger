# Multi-stage build, per docs/08-docker.md §3.
#
# Build context is ./backend (see docker-compose.yml), so paths below are
# relative to the backend/ folder: TransactionLedger.sln,
# TransactionLedger/TransactionLedger.csproj, TransactionLedger.Tests/*.

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy only the project files first: dotnet restore is the slow step, and
# Docker invalidates a layer (and everything after it) the moment a copied
# file changes. Editing a .cs file is frequent; a dependency changing is not.
# Copying the whole source tree before restore would re-download every NuGet
# package on every single code edit.
COPY *.sln .
COPY TransactionLedger/*.csproj TransactionLedger/
COPY TransactionLedger.Tests/*.csproj TransactionLedger.Tests/
RUN dotnet restore

COPY . .
RUN dotnet publish TransactionLedger/TransactionLedger.csproj -c Release -o /app

# ---- runtime ----
# aspnet, not sdk: the SDK image carries compilers, MSBuild, and the NuGet
# cache — none of which a compiled app needs to run, and all of which is
# extra attack surface and image weight to ship.
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Microsoft strips curl and wget from the .NET 8+ runtime images as a
# hardening measure (smaller attack surface, fewer CVEs to track) - verified
# by `docker run --rm mcr.microsoft.com/dotnet/aspnet:9.0 which curl`, which
# comes back empty. Without this, docker-compose.yml's healthcheck
# (`curl -f http://localhost:8080/health`) would fail with "exec: curl: not
# found" on every attempt, the api container would never report healthy, and
# web's `depends_on: condition: service_healthy` would block forever - the
# exact failure the healthcheck exists to catch, just self-inflicted instead.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app .
EXPOSE 8080

# The .NET runtime images define a non-root user for exactly this reason: a
# containerised process running as root that escapes its namespace is root
# on the host, and a web API has no reason to run as root.
USER $APP_UID

ENTRYPOINT ["dotnet", "TransactionLedger.dll"]
