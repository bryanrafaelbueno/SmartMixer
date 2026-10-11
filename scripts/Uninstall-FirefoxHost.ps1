$ErrorActionPreference = "Stop"
$registryPath = "HKCU:\Software\Mozilla\NativeMessagingHosts\com.smartmixer.bridge"
$hostDirectory = Join-Path $env:LOCALAPPDATA "SmartMixer\FirefoxNativeHost"

if (Test-Path -LiteralPath $registryPath) {
    Remove-Item -LiteralPath $registryPath -Force
}
if (Test-Path -LiteralPath $hostDirectory) {
    Remove-Item -LiteralPath (Join-Path $hostDirectory "com.smartmixer.bridge.json") -Force -ErrorAction SilentlyContinue
}
Write-Output "Firefox native host registration removed."
