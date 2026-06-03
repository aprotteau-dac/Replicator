# Brand

Replicator uses the v1.0.1 brand kit:

- Interlock mark
- Industrial Red action color
- bone ink for ordinary text and successful states
- amber only for alarm states
- mono-forward, square-edged interface

The kit is integrated under:

```text
src/Replicator.App/Themes/Brand/
```

Loaded dictionaries:

```text
Theme.xaml
Icons.xaml
Logo.xaml
```

The current WinUI shell uses WinUI-native theme, icon, and logo resources. The older WPF compatibility/control bridge dictionaries were removed during the WinUI 3 migration.

The app icon uses:

```text
src/Replicator.App/Themes/Brand/logo/replicator.ico
```

## Rules

- Red is for brand and action, not decoration.
- Bone is for ok; success states should not be green.
- Amber is for alarms: locked drives, conflicts, required user response.
- Do not put red on amber.
- Keep the mark's seam red unless using the explicit knockout mark variant.

## Migration Notes

The WinUI shell now uses the brand palette and mark through native WinUI resource dictionaries. Future UI work should prefer the existing brand keys directly and keep new resources in `Theme.xaml`, `Icons.xaml`, or `Logo.xaml`.
