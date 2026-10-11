param(
    [Parameter(Mandatory = $true)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$ExecutablePath
)

$ErrorActionPreference = "Stop"
$resolvedExecutable = (Resolve-Path -LiteralPath $ExecutablePath).Path
$hostDirectory = Join-Path $env:LOCALAPPDATA "SmartMixer\FirefoxNativeHost"
$manifestPath = Join-Path $hostDirectory "com.smartmixer.bridge.json"
$registryPath = "HKCU:\Software\Mozilla\NativeMessagingHosts\com.smartmixer.bridge"

New-Item -ItemType Directory -Path $hostDirectory -Force | Out-Null
$manifest = @{
    name = "com.smartmixer.bridge"
    description = "SmartMixer local Firefox bridge"
    path = $resolvedExecutable
    type = "stdio"
    allowed_extensions = @("brthy467@gmail.com")
} | ConvertTo-Json -Depth 4
[System.IO.File]::WriteAllText($manifestPath, $manifest, [System.Text.UTF8Encoding]::new($false))
New-Item -Path $registryPath -Force | Out-Null
Set-Item -Path $registryPath -Value $manifestPath
Write-Output "Firefox native host registered for the current Windows user."
Write-Output "Manifest: $manifestPath"
