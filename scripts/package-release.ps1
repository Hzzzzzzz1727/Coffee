$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$releaseRoot = Join-Path $projectRoot 'release'
$packageRoot = Join-Path $releaseRoot 'CoffeeOS-Windows-x64'
$appRoot = Join-Path $packageRoot 'app'

if (Test-Path -LiteralPath $packageRoot) {
    $resolvedRelease = (Resolve-Path -LiteralPath $releaseRoot).Path
    $resolvedPackage = (Resolve-Path -LiteralPath $packageRoot).Path
    if (-not $resolvedPackage.StartsWith($resolvedRelease, [StringComparison]::OrdinalIgnoreCase)) { throw 'Đường dẫn release không an toàn.' }
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $appRoot -Force | Out-Null

Push-Location $projectRoot
try {
    & npm.cmd run build
    if ($LASTEXITCODE -ne 0) { throw 'Build giao diện thất bại.' }
    & dotnet publish 'server-dotnet/CoffeeApi.csproj' -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None -o $appRoot
    if ($LASTEXITCODE -ne 0) { throw 'Publish API thất bại.' }
}
finally { Pop-Location }

Copy-Item -LiteralPath (Join-Path $projectRoot 'deployment/Install-CoffeeOS.ps1') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $projectRoot 'deployment/README-CUSTOMER.txt') -Destination $packageRoot
$zipPath = Join-Path $releaseRoot 'CoffeeOS-Windows-x64.zip'
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Hoàn tất: $zipPath" -ForegroundColor Green
Write-Host 'Private key KHÔNG được đưa vào gói phát hành.' -ForegroundColor Yellow
