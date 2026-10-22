$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$demoProject = Join-Path $projectRoot 'server-sqlite-demo'
$webRoot = Join-Path $demoProject 'wwwroot'
$releaseRoot = Join-Path $projectRoot 'release-sqlite-demo'
$publishRoot = Join-Path $releaseRoot 'publish'

Push-Location $projectRoot
try {
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }

    if (Test-Path -LiteralPath $webRoot) { Remove-Item -LiteralPath $webRoot -Recurse -Force }
    New-Item -ItemType Directory -Path $webRoot -Force | Out-Null
    Copy-Item -Path (Join-Path $projectRoot 'server-dotnet\wwwroot\*') -Destination $webRoot -Recurse -Force

    if (Test-Path -LiteralPath $releaseRoot) {
        $resolvedRelease = (Resolve-Path -LiteralPath $releaseRoot).Path
        if (-not $resolvedRelease.StartsWith($projectRoot, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe release path.' }
        Remove-Item -LiteralPath $resolvedRelease -Recurse -Force
    }
    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
    & dotnet publish (Join-Path $demoProject 'CoffeeOSDemo.csproj') -c Release -r win-x64 --self-contained true --no-restore -o $publishRoot /p:PublishSingleFile=true /p:IncludeAllContentForSelfExtract=true /p:EnableCompressionInSingleFile=true
    if ($LASTEXITCODE -ne 0) { throw 'SQLite demo publish failed.' }

    $exe = Join-Path $publishRoot 'CoffeeOS-Demo.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw 'CoffeeOS-Demo.exe was not produced.' }
    Copy-Item -LiteralPath $exe -Destination (Join-Path $releaseRoot 'CoffeeOS-Demo.exe') -Force
    Copy-Item -LiteralPath (Join-Path $demoProject 'README.txt') -Destination (Join-Path $releaseRoot 'HUONG-DAN.txt') -Force
    Compress-Archive -Path (Join-Path $releaseRoot 'CoffeeOS-Demo.exe'),(Join-Path $releaseRoot 'HUONG-DAN.txt') -DestinationPath (Join-Path $releaseRoot 'CoffeeOS-Demo-SQLite.zip') -Force
    Write-Host "SQLite demo ready: $(Join-Path $releaseRoot 'CoffeeOS-Demo.exe')"
}
finally { Pop-Location }
