<#
.SYNOPSIS
  Runs the API locally without Docker, for active backend/frontend development.

.DESCRIPTION
  `dotnet run` alone is not enough: Jwt:Key and ConnectionStrings:Default are
  required and validated at startup (ValidateOnStart, docs/08-docker.md section
  6), and appsettings.json ships them empty on purpose - no secret is committed
  (NFR-08). Docker Compose supplies them as environment variables built from
  .env (docs/08-docker.md section 5). This script does the exact same thing for
  a plain `dotnet run`, so a teammate needs nothing beyond the one-time .env
  setup Docker already requires - no manual environment variables, and no
  personal `dotnet user-secrets` that only exist on one machine.

.EXAMPLE
  cp .env.example .env    # once, then fill in real values
  ./run-api.ps1
#>

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$api  = Join-Path $root 'backend\TransactionLedger'
$envFile = Join-Path $root '.env'

if (-not (Test-Path $envFile)) {
    Write-Error "No .env file found at $envFile. Run: cp .env.example .env, then fill in real values."
    exit 1
}

$envMap = @{}
Get-Content $envFile | ForEach-Object {
    if ($_ -match '^\s*([^#=]+)=(.*)$') { $envMap[$Matches[1].Trim()] = $Matches[2].Trim() }
}

foreach ($key in 'POSTGRES_DB', 'POSTGRES_USER', 'POSTGRES_PASSWORD', 'JWT_KEY', 'JWT_ISSUER', 'JWT_AUDIENCE') {
    if (-not $envMap.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($envMap[$key])) {
        Write-Error "$key is missing or empty in .env."
        exit 1
    }
}

# --no-launch-profile below skips launchSettings.json entirely, including its
# applicationUrl - so the port has to be set here too, explicitly, rather than
# left to a JSON file this script does not read. Without it ASP.NET Core falls
# back to its own default (localhost:5000), which matches neither
# docs/08-docker.md's :8080 nor the frontend's NEXT_PUBLIC_API_BASE_URL.
#
# Same shape as docker-compose.yml's api service (docs/08-docker.md section 5),
# minus the container-network hostname: Postgres is reached at localhost here
# because this process runs on the host, not inside Compose's bridge network.
$env:ASPNETCORE_ENVIRONMENT     = 'Development'
$env:ASPNETCORE_URLS            = 'http://localhost:8080'
$env:ConnectionStrings__Default = "Host=localhost;Port=5432;Database=$($envMap.POSTGRES_DB);Username=$($envMap.POSTGRES_USER);Password=$($envMap.POSTGRES_PASSWORD)"
$env:Jwt__Key                   = $envMap.JWT_KEY
$env:Jwt__Issuer                = $envMap.JWT_ISSUER
$env:Jwt__Audience              = $envMap.JWT_AUDIENCE

# Not set inside Compose's api service either - there it is set explicitly to
# true because the healthcheck already proved Postgres is reachable. Here,
# reaching Postgres is on you: "docker compose up db" (or a local Postgres 16)
# has to be running first, or migrations fail against a database that is not
# there yet.
$env:Database__RunMigrationsOnStartup = 'true'

Write-Host "Starting the API at http://localhost:8080 (Ctrl+C to stop) ..."
Write-Host "Expects Postgres reachable at localhost:5432 - run 'docker compose up db' first if it is not already running."
Write-Host ""

dotnet run --project $api --no-launch-profile
