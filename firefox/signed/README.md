# Signed extension

`SmartMixer.signed.xpi` is the Mozilla-signed package of the extension source
in `firefox/extension/`, downloaded from addons.mozilla.org after review. The
installer bundles it into the MSI (`installer/Build-Msi.ps1 -FirefoxXpiPath`)
so the first-run wizard can offer a trusted installation.

The signed package is version-locked to the source it was built from. When the
extension source changes, submit the new build for review and replace this
file with the newly signed XPI. Without it, the MSI still prepares the native
host and the user can install the extension from
[addons.mozilla.org](https://addons.mozilla.org/firefox/) or pick a signed
`.xpi` manually.
