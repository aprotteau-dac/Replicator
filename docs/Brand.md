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
Compatibility.xaml
AppControls.xaml
Icons.xaml
Logo.xaml
```

`Compatibility.xaml` maps the older WPF resource keys onto the brand tokens so the current app can migrate gradually. `AppControls.xaml` is the current WPF control bridge: it keeps existing controls dark, square, and branded while views are moved toward direct brand keys over time.

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

The first integration is a hybrid path. The UI now uses the brand palette and mark, but not every view has been redesigned around the kit's native `Brush.*`, `Text.*`, `Button.*`, and `Icon.*` resources.

Future UI work should prefer the brand keys directly and remove compatibility aliases when the WPF views no longer depend on the old theme contract.
