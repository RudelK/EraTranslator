param(
    [string]$HostName = "0.0.0.0",
    [int]$Port = 8000,
    [switch]$Reload,
    [switch]$SkipMigration
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ServerDir = Split-Path -Parent $ScriptDir
Set-Location $ServerDir

if (-not $env:ERATRANSLATOR_DATABASE_URL) {
    $env:ERATRANSLATOR_DATABASE_URL = "postgresql+psycopg://eratran:eratrandb!23@localhost:5432/eratrandb"
}

if (-not (Get-Command uv -ErrorAction SilentlyContinue)) {
    throw "uv is not installed or not available on PATH."
}

if (-not $SkipMigration) {
    uv run alembic upgrade head
}

$uvicornArgs = @("uvicorn", "app.main:app", "--host", $HostName, "--port", "$Port")
if ($Reload) {
    $uvicornArgs += "--reload"
}

uv run @uvicornArgs
