# Security Policy

## Supported versions

Only the latest GitHub release is supported with security fixes.

## Reporting a vulnerability

Please **do not** open a public issue for security problems. Use
[GitHub Security Advisories](https://github.com/bryanrafaelbueno/SmartMixer/security/advisories/new)
to report vulnerabilities privately. We will acknowledge the report as soon as
possible and coordinate a fix and disclosure.

## Scope notes

SmartMixer runs locally and does not contact any remote service. The pieces
most relevant for security review are:

- the serial protocol parser (`src/SmartMixer/Core/SerialProtocol.cs`);
- the named pipe bridge between the Firefox Native Messaging Host and the app
  (`firefox/native-host/Program.cs`, `src/SmartMixer/Services/FirefoxConnector.cs`);
- the per-user registry registration of the Native Messaging Host
  (`scripts/Install-FirefoxHost.ps1`).

Reports about the bundled Mozilla-signed Firefox extension should also be sent
through this repository; we will forward them to the Mozilla Add-ons review
process when needed.
