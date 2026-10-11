param(
    [string]$FirefoxXpiPath,
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$buildRoot = Join-Path $env:TEMP "SmartMixer-MsiBuild-$([Guid]::NewGuid().ToString('N'))"
$appPublishDirectory = Join-Path $buildRoot "app"
$firmwareBuildDirectory = Join-Path $buildRoot "firmware"
$nativeHostBuildDirectory = Join-Path $buildRoot "native-host-build"
$arduinoCliDirectory = Join-Path $buildRoot "arduino-cli"
$arduinoConfigDirectory = Join-Path $buildRoot "arduino-config"
$arduinoDataDirectory = Join-Path $buildRoot "arduino-data"
$arduinoDownloadsDirectory = Join-Path $buildRoot "arduino-downloads"
$arduinoUserDirectory = Join-Path $buildRoot "arduino-user"

trap {
    $buildFailure = $_
    if (Test-Path -LiteralPath $buildRoot) {
        Remove-Item -LiteralPath $buildRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Error -ErrorRecord $buildFailure -ErrorAction Continue
    exit 1
}

if ($FirefoxXpiPath) {
    if ([System.IO.Path]::GetExtension($FirefoxXpiPath) -ne ".xpi" -or
        -not (Test-Path -LiteralPath $FirefoxXpiPath -PathType Leaf)) {
        throw "FirefoxXpiPath must point to an existing signed Firefox .xpi file."
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $FirefoxXpiPath).Path)
    try {
        $entries = @{}
        foreach ($entry in $archive.Entries) {
            $entries[$entry.FullName.ToLowerInvariant()] = $entry
        }
        if (-not $entries.ContainsKey("meta-inf/mozilla.rsa") -or
            -not $entries.ContainsKey("meta-inf/mozilla.sf")) {
            throw "FirefoxXpiPath is not signed by Mozilla. Provide the signed XPI downloaded from AMO."
        }
        if (-not $entries.ContainsKey("manifest.json")) {
            throw "FirefoxXpiPath does not contain manifest.json."
        }
        $reader = [System.IO.StreamReader]::new($entries["manifest.json"].Open())
        try {
            $manifest = $reader.ReadToEnd() | ConvertFrom-Json
        }
        finally {
            $reader.Dispose()
        }
        $extensionId = $manifest.browser_specific_settings.gecko.id
        if ($extensionId -ne "brthy467@gmail.com") {
            throw "FirefoxXpiPath has extension ID '$extensionId'; SmartMixer requires 'brthy467@gmail.com'."
        }
    }
    finally {
        $archive.Dispose()
    }
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path -LiteralPath $vswhere)) {
    throw "Visual Studio Installer (vswhere.exe) is required to build the native WinUI application."
}
$visualStudioPath = & $vswhere -latest -products "*" -requires Microsoft.Component.MSBuild -property installationPath
if ($LASTEXITCODE -ne 0 -or -not $visualStudioPath) {
    throw "A Visual Studio installation with MSBuild was not found."
}
$msbuild = Join-Path $visualStudioPath "MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path -LiteralPath $msbuild)) {
    throw "MSBuild.exe was not found at '$msbuild'."
}

New-Item -ItemType Directory -Path $appPublishDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $firmwareBuildDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $arduinoCliDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $arduinoConfigDirectory -Force | Out-Null

$cliArchive = Join-Path $buildRoot "arduino-cli.zip"
$cliUrl = "https://downloads.arduino.cc/arduino-cli/arduino-cli_1.3.1_Windows_64bit.zip"
curl.exe --fail --location --silent --show-error $cliUrl --output $cliArchive
if ($LASTEXITCODE -ne 0) {
    throw "Could not download Arduino CLI from the official Arduino distribution server."
}
Expand-Archive -LiteralPath $cliArchive -DestinationPath $arduinoCliDirectory -Force
$arduinoCli = Join-Path $arduinoCliDirectory "arduino-cli.exe"
& $arduinoCli config init --overwrite --dest-dir $arduinoConfigDirectory | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Arduino CLI could not initialize its isolated build configuration." }
$arduinoConfig = Join-Path $arduinoConfigDirectory "arduino-cli.yaml"
& $arduinoCli --config-file $arduinoConfig config set directories.data $arduinoDataDirectory
if ($LASTEXITCODE -ne 0) { throw "Arduino CLI could not configure its isolated package directory." }
& $arduinoCli --config-file $arduinoConfig config set directories.downloads $arduinoDownloadsDirectory
if ($LASTEXITCODE -ne 0) { throw "Arduino CLI could not configure its isolated downloads directory." }
& $arduinoCli --config-file $arduinoConfig config set directories.user $arduinoUserDirectory
if ($LASTEXITCODE -ne 0) { throw "Arduino CLI could not configure its isolated sketchbook directory." }
& $arduinoCli --config-file $arduinoConfig core update-index
if ($LASTEXITCODE -ne 0) { throw "Arduino's AVR board index could not be downloaded." }
& $arduinoCli --config-file $arduinoConfig core install arduino:avr@1.8.6
if ($LASTEXITCODE -ne 0) { throw "Arduino AVR Boards 1.8.6 could not be installed." }
& $arduinoCli --config-file $arduinoConfig compile `
    --fqbn arduino:avr:micro `
    --output-dir $firmwareBuildDirectory `
    (Join-Path $repositoryRoot "firmware\SmartMixerProMicro")
if ($LASTEXITCODE -ne 0) { throw "SmartMixer firmware compilation failed for Arduino Micro (ATmega32U4, 16 MHz)." }

$arduinoPackages = Join-Path $arduinoDataDirectory "packages\arduino"
$avrdudeRoot = Join-Path $arduinoPackages "tools\avrdude\6.3.0-arduino17"

$appProject = Join-Path $repositoryRoot "src\SmartMixer\SmartMixer.csproj"
& $msbuild $appProject /restore /t:Publish `
    "/p:Configuration=Release" `
    "/p:Platform=x64" `
    "/p:RuntimeIdentifier=win-x64" `
    "/p:SelfContained=true" `
    "/p:SmartMixerMsi=true" `
    "/p:WindowsPackageType=None" `
    "/p:EnableMsixTooling=false" `
    "/p:GenerateAppxPackageOnBuild=false" `
    "/p:WindowsAppSDKSelfContained=true" `
    "/p:ProjectPriFileName=resources.pri" `
    "/p:PublishDir=$appPublishDirectory\" `
    "/p:DebugSymbols=false" `
    "/p:DebugType=None" `
    /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "The unpackaged, self-contained WinUI application could not be published."
}

$priDestination = Join-Path $appPublishDirectory "resources.pri"
if (-not (Test-Path -LiteralPath $priDestination -PathType Leaf)) {
    $priCandidates = @(
        Join-Path $repositoryRoot "src\SmartMixer\bin\SmartMixerMsi\x64\Release\net8.0-windows10.0.19041.0\win-x64\resources.pri",
        Join-Path $repositoryRoot "src\SmartMixer\bin\SmartMixerMsi\x64\Release\net8.0-windows10.0.19041.0\win-x64\SmartMixer.pri",
        Join-Path $repositoryRoot "src\SmartMixer\bin\x64\Release\net8.0-windows10.0.19041.0\resources.pri"
    )
    $copiedPri = $false
    foreach ($candidate in $priCandidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            Copy-Item -LiteralPath $candidate -Destination $priDestination -Force
            $copiedPri = $true
            break
        }
    }
    if (-not $copiedPri -or -not (Test-Path -LiteralPath $priDestination -PathType Leaf)) {
        throw "WinUI resource index (resources.pri) was not generated for unpackaged deployment."
    }
}

$firmwareDirectory = Join-Path $appPublishDirectory "firmware"
$toolsDirectory = Join-Path $appPublishDirectory "tools"
$imagesDirectory = Join-Path $appPublishDirectory "Images"
$nativeHostDirectory = Join-Path $appPublishDirectory "firefox\native-host"
$extensionSourceDirectory = Join-Path $appPublishDirectory "firefox\extension-source"
New-Item -ItemType Directory -Path $firmwareDirectory,$toolsDirectory,$imagesDirectory,$nativeHostDirectory,$extensionSourceDirectory -Force | Out-Null
Copy-Item (Join-Path $repositoryRoot "src\SmartMixer\Images\*") $imagesDirectory -Recurse -Force
Copy-Item (Join-Path $firmwareBuildDirectory "SmartMixerProMicro.ino.hex") `
    (Join-Path $firmwareDirectory "SmartMixerMicro.hex") -Force
Copy-Item (Join-Path $repositoryRoot "firmware\SmartMixerProMicro\SmartMixerProMicro.ino") `
    (Join-Path $firmwareDirectory "SmartMixerProMicro.ino") -Force
Copy-Item (Join-Path $avrdudeRoot "bin\avrdude.exe") (Join-Path $toolsDirectory "avrdude.exe") -Force
Copy-Item (Join-Path $avrdudeRoot "bin\libusb0.dll") (Join-Path $toolsDirectory "libusb0.dll") -Force
Copy-Item (Join-Path $avrdudeRoot "etc\avrdude.conf") (Join-Path $toolsDirectory "avrdude.conf") -Force
Copy-Item (Join-Path $repositoryRoot "firefox\extension\*") $extensionSourceDirectory -Recurse -Force
Copy-Item (Join-Path $repositoryRoot "installer\THIRD-PARTY-NOTICES.txt") `
    (Join-Path $appPublishDirectory "THIRD-PARTY-NOTICES.txt") -Force
if ($FirefoxXpiPath) {
    Copy-Item -LiteralPath $FirefoxXpiPath (Join-Path $appPublishDirectory "firefox\SmartMixer.signed.xpi") -Force
}

$nativeHostProject = Join-Path $repositoryRoot "firefox\native-host\SmartMixer.NativeHost.csproj"
dotnet publish $nativeHostProject --configuration Release --runtime win-x64 --self-contained true `
    "-p:OutputPath=$nativeHostBuildDirectory\" `
    -p:PublishSingleFile=true -p:DebugSymbols=false -p:DebugType=None `
    --output $nativeHostDirectory
if ($LASTEXITCODE -ne 0) {
    throw "The Firefox native messaging host could not be published."
}

$installerProject = Join-Path $PSScriptRoot "SmartMixer.Installer.wixproj"
$installerArguments = @($installerProject, "--configuration", "Release", "-p:AppPublishDir=$appPublishDirectory")
if ($Version) {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version must use the form major.minor.patch (for example 1.0.1)."
    }
    $installerArguments += "-p:PackageVersion=$Version"
}
dotnet build @installerArguments
if ($LASTEXITCODE -ne 0) {
    throw "WiX could not build the SmartMixer MSI."
}

$installerDirectory = Join-Path $PSScriptRoot "dist"
$builtInstaller = Join-Path $installerDirectory "Release\SmartMixerSetup.msi"
if (-not (Test-Path -LiteralPath $builtInstaller -PathType Leaf)) {
    throw "WiX reported success but the MSI was not produced at '$builtInstaller'."
}
Write-Output "SmartMixer MSI created: $builtInstaller"
Write-Output "Firmware target: Arduino Micro / ATmega32U4, 16 MHz."
if (-not $FirefoxXpiPath) {
    Write-Warning "No Mozilla-signed Firefox .xpi was supplied. The MSI prepares the native host; the signed extension must be supplied or installed from AMO."
}
Remove-Item -LiteralPath $buildRoot -Recurse -Force
