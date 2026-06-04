# Packaging And Install

## Install From Repo

Install directly from a repo checkout:

```powershell
.\tools\install-dev.ps1
```

This rebuilds the local package, then installs Replicator per-user.

To intentionally reuse an existing package:

```powershell
.\tools\install-dev.ps1 -SkipPublish
```

File-only install without shortcuts:

```powershell
.\tools\install-dev.ps1 -NoShortcuts
```

## Create Release Package

Create a self-contained Windows x64 package:

```powershell
.\tools\publish-release.ps1 -Version 0.1.0
```

The WinUI 3 app is published as an unpackaged, self-contained Windows App SDK app. The package remains folder-based and installs per-user.

The script writes:

```text
artifacts\publish\Replicator-0.1.0-win-x64\
artifacts\package\Replicator-0.1.0-win-x64\
artifacts\package\Replicator-0.1.0-win-x64.zip
```

The package also includes `replicator.ico` as a standalone shortcut icon. Installer-created shortcuts use that icon instead of asking Windows Explorer to extract an icon from the self-contained app executable.

## Install From Package

Install from the unpacked package:

```powershell
.\install-replicator.ps1
```

Default install location:

```text
%LOCALAPPDATA%\Programs\Replicator
```

By default the installer creates Start Menu and Desktop shortcuts that target `Replicator.App.exe` and use the packaged `replicator.ico`.

Skip desktop shortcut:

```powershell
.\install-replicator.ps1 -NoDesktopShortcut
```

Skip all shortcuts:

```powershell
.\install-replicator.ps1 -NoShortcuts
```

## Uninstall

```powershell
.\uninstall-replicator.ps1
```

Remove profile/script/log data too:

```powershell
.\uninstall-replicator.ps1 -RemoveAppData
```

This is a lightweight installer path, not an MSI/MSIX yet.
