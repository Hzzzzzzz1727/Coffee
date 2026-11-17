$ErrorActionPreference = 'Stop'

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from PowerShell as Administrator.'
}

$installRoot = Join-Path $env:ProgramData 'CoffeeOS'
$apiPath = Join-Path $installRoot 'CoffeeApi.exe'
$envPath = Join-Path $installRoot '.env'
if (-not (Test-Path -LiteralPath $apiPath)) {
    throw "CoffeeOS was not found at $apiPath. Run this script on the installed server."
}

$hostName = Read-Host 'SMTP host (for example smtp.gmail.com)'
$portText = Read-Host 'SMTP port (default 587)'
if ([string]::IsNullOrWhiteSpace($portText)) { $portText = '587' }
$user = Read-Host 'SMTP account (usually the sending email)'
$from = Read-Host "Sender email (press Enter to use $user)"
if ([string]::IsNullOrWhiteSpace($from)) { $from = $user }
$securePassword = Read-Host 'SMTP password/App Password' -AsSecureString
$password = [System.Net.NetworkCredential]::new('', $securePassword).Password

if ([string]::IsNullOrWhiteSpace($hostName) -or
    [string]::IsNullOrWhiteSpace($user) -or
    [string]::IsNullOrWhiteSpace($password)) {
    throw 'SMTP host, account, and password are required.'
}
$invalidCharacters = [char[]]@([char]13, [char]10, [char]34)
foreach ($value in @($hostName, $user, $from, $password)) {
    if ($value.IndexOfAny($invalidCharacters) -ge 0) {
        throw 'SMTP values cannot contain double quotes or line breaks.'
    }
}
if ($portText -notmatch '^\d+$' -or [int]$portText -lt 1 -or [int]$portText -gt 65535) {
    throw 'SMTP port must be a number from 1 to 65535.'
}
try {
    $null = [System.Net.Mail.MailAddress]::new($from)
}
catch {
    throw 'The sender email address is invalid.'
}

$settings = [ordered]@{
    COFFEE_SMTP_HOST = $hostName
    COFFEE_SMTP_PORT = $portText
    COFFEE_SMTP_USER = $user
    COFFEE_SMTP_PASSWORD = $password
    COFFEE_SMTP_FROM = $from
}
foreach ($key in $settings.Keys) {
    foreach ($scope in @('Process', 'User', 'Machine')) {
        $existingValue = [Environment]::GetEnvironmentVariable($key, $scope)
        if (-not [string]::IsNullOrWhiteSpace($existingValue) -and
            $existingValue -cne [string]$settings[$key]) {
            throw "$key is set in Windows environment variables and overrides .env. Update or remove it, then rerun this script."
        }
    }
}
$newFile = -not (Test-Path -LiteralPath $envPath)
if ($newFile) { [System.IO.File]::Create($envPath).Dispose() }
$acl = Get-Acl -LiteralPath $envPath
$acl.SetAccessRuleProtection($true, $false)
foreach ($rule in @($acl.Access)) { $acl.RemoveAccessRuleSpecific($rule) }
$acl.SetOwner($identity.User)
$accounts = @(
    $identity.User,
    [System.Security.Principal.SecurityIdentifier]::new('S-1-5-18'),
    [System.Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
)
foreach ($account in $accounts) {
    $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
        $account,
        [System.Security.AccessControl.FileSystemRights]::FullControl,
        [System.Security.AccessControl.AccessControlType]::Allow
    )
    $acl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $envPath -AclObject $acl

$lines = [System.Collections.Generic.List[string]]::new()
if (Test-Path -LiteralPath $envPath) {
    foreach ($line in Get-Content -LiteralPath $envPath) { $lines.Add($line) }
}

foreach ($key in $settings.Keys) {
    $entry = "$key=`"$($settings[$key])`""
    $found = $false
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match ('^\s*' + [regex]::Escape($key) + '\s*=')) {
            $lines[$index] = $entry
            $found = $true
            break
        }
    }
    if (-not $found) { $lines.Add($entry) }
}

[System.IO.File]::WriteAllLines($envPath, $lines.ToArray(), [System.Text.UTF8Encoding]::new($false))

$installedProcesses = Get-CimInstance Win32_Process -Filter "Name = 'CoffeeApi.exe'" |
    Where-Object { $_.ExecutablePath -and $_.ExecutablePath.Equals($apiPath, [StringComparison]::OrdinalIgnoreCase) }
foreach ($process in $installedProcesses) {
    Stop-Process -Id $process.ProcessId -Force
}
Start-Sleep -Seconds 2
$startedProcess = Start-Process -FilePath $apiPath -WorkingDirectory $installRoot -WindowStyle Hidden -PassThru
Start-Sleep -Seconds 2
if ($startedProcess.HasExited) {
    throw 'CoffeeOS API exited during startup. Check the SQL Server connection and API logs before retrying.'
}

Write-Host "SMTP settings saved to $envPath. CoffeeOS API has been restarted." -ForegroundColor Green
Write-Host 'Client computers keep using the server IP; do not copy SMTP credentials to them.'
