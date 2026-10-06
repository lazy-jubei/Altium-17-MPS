# Altium 17 MPS

An Altium plugin that backports Manufacturer Part Search to Altium Designer 17.

- Searches Altium's configured suppliers by MPN or description.
- Shows technical parameters, supplier stock, prices and datasheet links.
- Downloads matching symbols, footprints and available 3D models from connected Altium Vaults.
- Fixes footprint links in local copies so AD17 can resolve their 3D bodies.
- Copies a matching library component with supplier metadata; placement supports undo/redo.
- Fixes Ciiva's broken MPN lookup and supplier links, and keeps dialogs visible under Wine.

Tested in **Altium Designer 17.1 under Wine**. Uses Altium's existing login; CAD models must be available in a connected Vault.

## Install

Download **Altium-17-MPS-Setup.exe** from the [release](https://github.com/lazy-jubei/Altium-17-MPS/releases/latest). Close AD17, run the installer in Windows or your AD17 Wine prefix, and click **Install**. No PowerShell is needed.

The ZIP also includes the PowerShell and Python installers for manual deployment.

Restart Altium, then open **Manufacturer Part Search (AD17)** from File or Tools. It runs inside Altium; no script is needed to use it. Search, select a part and choose **Download CAD models**. If several revisions match, select one in Models. Reimport parts downloaded with v0.2.0 to refresh their footprint links. **Import library component...** remains available for installed libraries.

Output is `Documents/AltiumParts/ManufacturerParts.schlib`; `ALTIUM_PART_SEARCH_LIBRARY_DIR` overrides it. Keep the source libraries or Altium's Vault cache for linked models.

## Build

Requires .NET SDK 8 or later and AD17's SDK assemblies. The plugin targets .NET Framework 4.8/x86; Altium DLLs are not bundled.

```powershell
.\Build.ps1 -AltiumInstallDir 'C:\Program Files (x86)\Altium\AD17'
.\Package.ps1
```

On macOS: `./tools/build-macos.sh`, then `pwsh -File Package.ps1`.
Manual installers accept `-ExtensionsRoot` or `--extensions-root` to select an installation.

Based on [expired6978/EasyEDALoader](https://github.com/expired6978/EasyEDALoader) and the AD17 work in its fork. Runtime errors are logged to `%LOCALAPPDATA%\Altium17PartSearch\runtime-errors.log`.
