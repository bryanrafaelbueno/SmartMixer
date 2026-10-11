param(
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$sourceDirectory = Join-Path $repositoryRoot "firefox\extension"
if (-not $OutputPath) {
    $OutputPath = Join-Path $repositoryRoot "firefox\dist\SmartMixer.zip"
}
$outputFile = [System.IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $outputFile

if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory "manifest.json") -PathType Leaf)) {
    throw "Firefox extension manifest was not found in '$sourceDirectory'."
}
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
if (Test-Path -LiteralPath $outputFile) {
    Remove-Item -LiteralPath $outputFile -Force
}

Compress-Archive `
    -Path (Join-Path $sourceDirectory "*") `
    -DestinationPath $outputFile `
    -CompressionLevel Optimal

Add-Type -AssemblyName System.IO.Compression
$archive = [System.IO.Compression.ZipFile]::OpenRead($outputFile)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    foreach ($requiredFile in @("manifest.json", "background.js")) {
        if ($entries -notcontains $requiredFile) {
            throw "Firefox extension archive is missing root entry '$requiredFile'."
        }
    }
}
finally {
    $archive.Dispose()
}

Write-Output "Firefox extension package created: $outputFile"
