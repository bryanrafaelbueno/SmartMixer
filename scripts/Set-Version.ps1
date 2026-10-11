param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

# Stamps the release version into the Firefox extension manifest and the MSIX
# package manifest. The MSI version is passed separately to WiX by the release
# workflow through installer\Build-Msi.ps1 -Version.
$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path

function Set-ManifestVersion {
    param(
        [string]$Path,
        [string]$Pattern,
        [string]$Replacement
    )

    $content = [System.IO.File]::ReadAllText($Path)
    if (-not [System.Text.RegularExpressions.Regex]::IsMatch($content, $Pattern)) {
        throw "Version marker was not found in '$Path'."
    }
    $updated = [System.Text.RegularExpressions.Regex]::Replace($content, $Pattern, $Replacement)
    if ($updated -ne $content) {
        [System.IO.File]::WriteAllText($Path, $updated, [System.Text.UTF8Encoding]::new($false))
    }
}

$extensionManifest = Join-Path $repositoryRoot "firefox\extension\manifest.json"
Set-ManifestVersion -Path $extensionManifest `
    -Pattern '("version"\s*:\s*")[^"]+(")' `
    -Replacement "`${1}$Version`$2"

$packageManifest = Join-Path $repositoryRoot "src\SmartMixer\Package.appxmanifest"
Set-ManifestVersion -Path $packageManifest `
    -Pattern '(<Identity[^>]*?\sVersion=")\d+(\.\d+){1,3}(")' `
    -Replacement "`${1}$Version.0`$3"

Write-Output "Stamped version $Version into the extension and package manifests."
