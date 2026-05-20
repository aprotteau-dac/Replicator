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

The script writes:

```text
artifacts\publish\Replicator-0.1.0-win-x64\
artifacts\package\Replicator-0.1.0-win-x64\
artifacts\package\Replicator-0.1.0-win-x64.zip
```

## Install From Package

Install from the unpacked package:

```powershell
.\install-replicator.ps1
```

Default install location:

```text
%LOCALAPPDATA%\Programs\Replicator
```

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
