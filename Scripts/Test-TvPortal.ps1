$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $workspace
try {
    & dotnet build AccIntegration.WebApp.csproj -p:SidebarCompileOnly=true -p:BaseIntermediateOutputPath=_build_verify/tv-portal-obj/ -o _build_verify/tv-portal-bin --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build isolado do backend falhou.' }
    & dotnet test Tests/NA_Connect.TvPortal.Tests/NA_Connect.TvPortal.Tests.csproj --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Testes do portal TV falharam.' }
    Push-Location ClientApp
    try {
        & npm.cmd run typecheck
        if ($LASTEXITCODE -ne 0) { throw 'Typecheck falhou.' }
        & npx.cmd vite build --outDir ../_build_verify/tv-portal-vite
        if ($LASTEXITCODE -ne 0) { throw 'Build Vite falhou.' }
        & node scripts/verify-tv-portal.mjs
        if ($LASTEXITCODE -ne 0) { throw 'Ensaio UI falhou.' }
    } finally { Pop-Location }
} finally { Pop-Location }
# No service starts/restarts, deployment, SQL migration or synchronization is executed.
