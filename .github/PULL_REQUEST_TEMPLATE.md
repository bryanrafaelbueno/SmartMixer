# Pull request

## Summary

<!-- What does this PR change and why? Link related issues with "Fixes #123". -->

## Type of change

- [ ] Bug fix
- [ ] New feature
- [ ] Documentation
- [ ] Build, tooling or CI

## How was this validated?

<!-- Commands you ran, hardware you tested with, screenshots of the UI. -->

## Checklist

- [ ] `dotnet test .\tests\SmartMixer.Tests\SmartMixer.Tests.csproj` passes
- [ ] The WinUI 3 app builds in Release (`MSBuild.exe .\src\SmartMixer\SmartMixer.csproj /restore /p:Configuration=Release /p:Platform=x64`)
- [ ] Serial protocol changes update `docs/PROTOCOL.md`, the firmware and `Core/SerialProtocol.cs`
- [ ] No build outputs, personal paths or secrets are included
