# Contributing to SmartMixer

Thanks for your interest in SmartMixer! This guide covers everything you need to
build, test and submit changes.

## Project layout

| Path | Contents |
|---|---|
| `src/SmartMixer/` | WinUI 3 application (pages, services, tray, overlay) |
| `src/SmartMixer/Core/` | Platform-free core compiled by both the app and the tests |
| `src/SmartMixer.Core/` | Project that compiles the shared core for tests and tooling |
| `tests/SmartMixer.Tests/` | xUnit tests for the core |
| `firmware/SmartMixerProMicro/` | Arduino firmware for the ATmega32U4 |
| `firefox/extension/` | Firefox WebExtension source |
| `firefox/native-host/` | Firefox Native Messaging Host (C#) |
| `installer/` | WiX 5 MSI packaging and first-run payload |
| `scripts/` | Registration, packaging and release helpers |
| `docs/` | Protocol reference |

## Prerequisites

- Windows 11 (or Windows 10 1809+);
- .NET 8 SDK;
- Visual Studio 2022 with the **WinUI 3 / Windows App SDK** workload and a
  Windows SDK to build the application;
- Arduino IDE 2.x or Arduino CLI only when changing the firmware.

## Build and test

Run the unit tests:

```powershell
dotnet test .\tests\SmartMixer.Tests\SmartMixer.Tests.csproj
```

Build the application from a Developer PowerShell:

```powershell
MSBuild.exe .\src\SmartMixer\SmartMixer.csproj /restore /p:Configuration=Release /p:Platform=x64
```

Publish the Firefox native host:

```powershell
dotnet publish .\firefox\native-host\SmartMixer.NativeHost.csproj -c Release -r win-x64 --self-contained true
```

Package the Firefox extension ZIP (validated for AMO):

```powershell
.\scripts\Build-FirefoxExtension.ps1
```

## Commit style

Use short, focused commits with a conventional-style prefix when it applies:

```text
feat(audio): keep mute state when volume changes
fix(serial): ignore duplicate sequence numbers after reconnect
docs: clarify the calibration flow
```

## Pull requests

1. Fork the repository and create a topic branch.
2. Keep changes scoped: one feature or fix per pull request.
3. Run the tests and build commands above before opening the PR.
4. Fill in the pull request template, including how you validated the change.
5. Open the pull request against `main`.

## Code guidelines

- Follow the existing style; the code uses file-scoped namespaces and nullable
  reference types everywhere.
- Keep error handling explicit: catch the specific exceptions the call can
  throw and log through `StructuredLogger`.
- Do not commit build outputs (`bin/`, `obj/`, `installer/dist/`,
  `firefox/dist/`), personal paths or machine-specific settings.
- Serial protocol changes must update `docs/PROTOCOL.md`, the firmware and
  `Core/SerialProtocol.cs` together.

## Releases

Releases are automated. Pushing a `v*` tag (for example `v1.0.1`) runs the
`Release` workflow, which stamps versions, builds the MSI with the bundled
firmware and Firefox bridge, and publishes the GitHub release with checksums.
