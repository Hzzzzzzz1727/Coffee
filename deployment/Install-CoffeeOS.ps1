$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Hãy nhấp phải PowerShell và chọn Run as administrator rồi chạy lại file cài đặt.' }

$sql = Get-Service -Name 'MSSQLSERVER' -ErrorAction SilentlyContinue
if (-not $sql) { throw 'Chưa tìm thấy SQL Server (MSSQLSERVER). Hãy cài SQL Server trước.' }
if ($sql.Status -ne 'Running') { Start-Service -Name 'MSSQLSERVER' }

$installRoot = Join-Path $env:ProgramData 'CoffeeOS'
$sourceApp = Join-Path $PSScriptRoot 'app'
if (-not (Test-Path -LiteralPath (Join-Path $sourceApp 'CoffeeApi.exe'))) { throw 'Gói cài đặt thiếu CoffeeApi.exe.' }
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Copy-Item -Path (Join-Path $sourceApp '*') -Destination $installRoot -Recurse -Force

$email = Read-Host 'Email tài khoản chủ quán'
$name = Read-Host 'Tên chủ quán'
$securePassword = Read-Host 'Mật khẩu (ít nhất 8 ký tự)' -AsSecureString
$password = [System.Net.NetworkCredential]::new('', $securePassword).Password
if ($password.Length -lt 8) { throw 'Mật khẩu phải có ít nhất 8 ký tự.' }
& (Join-Path $installRoot 'CoffeeApi.exe') --setup $email $password $name
if ($LASTEXITCODE -ne 0) { throw 'Không thể khởi tạo database.' }

if (-not (Get-NetFirewallRule -DisplayName 'CoffeeOS LAN' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'CoffeeOS LAN' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 3032 -Profile Private | Out-Null
}
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'CoffeeOS.lnk'))
$shortcut.TargetPath = Join-Path $installRoot 'CoffeeApi.exe'
$shortcut.WorkingDirectory = $installRoot
$shortcut.WindowStyle = 7
$shortcut.Save()
Start-Process -FilePath (Join-Path $installRoot 'CoffeeApi.exe') -WorkingDirectory $installRoot -WindowStyle Hidden
Write-Host 'Cài đặt hoàn tất. Mở http://localhost:3032' -ForegroundColor Green
