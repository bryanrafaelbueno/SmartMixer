# Changelog

All notable changes to SmartMixer are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.0] - 2026-10-11

### Added

- Seven-channel hardware mixer for Windows: seven potentiometers, seven channel
  LEDs and a touch sensor connected to an Arduino Pro Micro (ATmega32U4).
- WinUI 3 / Windows App SDK application with Mica backdrop, dashboard, channels,
  applications, profiles, Firefox, hardware, calibration, settings and
  diagnostics pages.
- Per-application volume and mute control through the Windows Audio Session API
  (NAudio/Core Audio) with automatic session enumeration.
- Automatic, Fixed and Hybrid channel assignment with seven ordering modes
  (alphabetical, recently opened, most recently used, playing audio, foreground
  first, audio activity and custom priority).
- Profiles with duplicate, rename, remove, default, import/export and automatic
  switching rules per application.
- Serial protocol v1 with ACK/latency telemetry, sequence validation, automatic
  reconnect, firmware version probe, soft takeover and per-channel calibration
  (endpoints, deadzone, smoothing, sensitivity, inversion).
- Acrylic volume overlay (position, duration and content options) that never
  steals focus, plus a native notification-area icon with quick actions and
  profile switching.
- Firefox integration: WebExtension that shares tab title, domain, favicon and
  audio state locally through a Native Messaging Host and a named pipe, with
  per-tab volume control and Firefox mode (toggled by the touch sensor).
- First-run setup wizard with firmware upload (bundled AVRDUDE) and
  calibration, delivered through a WiX 5 MSI that also registers the Firefox
  Native Messaging Host.
- MSIX packaging support for Windows Desktop, including the startup task
  declaration.
- xUnit test suite covering the serial protocol, calibration, configuration
  store, assignment engine and Firefox tab ordering.
- GitHub Actions workflows: CI on every push/PR and automated tagged releases
  that build and publish the MSI with checksums.

[Unreleased]: https://github.com/bryanrafaelbueno/SmartMixer/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/bryanrafaelbueno/SmartMixer/releases/tag/v1.0.0
