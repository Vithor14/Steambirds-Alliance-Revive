# Steambirds Alliance Revive — branding

The project logo, drawn from scratch with the original game logo as the style reference
(black plate, golden yellow, slanted blocky lettering, extruded "ALLIANCE", head / wings / tail emblem).
Nothing is copied from the original image. The new elements are the **REVIVE** banner and the ember-orange
accent (head visor, rocket-pod cells, "REVIVE" lettering) that mark this as the community revival.

| File | Use |
|---|---|
| `logo.svg` / `logo.png` | Full logo, 2000 x 1480, transparent background (README, GitHub social preview, launcher header) |
| `icon.svg` / `icon.png` | Square emblem with a big "R", 1024 x 1024, transparent background (avatar, launcher icon) |
| `icon.ico` | Windows icon with 16, 24, 32, 48, 64, 128 and 256 px sizes |
| `discord-logo.png` | Discord's own logo (their brand, not ours), shown only on the launcher buttons that open the community Discord invite |

## Colors

| Name | Hex | Where |
|---|---|---|
| Plate black | `#151019` | plates, wings, outlines (sampled from the original logo) |
| Steam yellow | `#F8BA24` | lettering, head and tail plates (sampled from the original logo) |
| Ember | `#F2622A` | "REVIVE", head visor, pod cells, triangle mark |

The black plate is part of the design: on dark backgrounds it fades into the background, exactly like the
original logo does. Put the logo on a light or mid-tone background when you can.

## Editing

The lettering is drawn glyph by glyph (no font needed), and `logo.svg` / `icon.svg` contain plain paths.
Both the SVG and the PNG files are generated from the same geometry by [`tools\make_logo.py`](../tools/make_logo.py)
(Python and Pillow only):

```powershell
python tools\make_logo.py
```

Change the coordinates or colors in that script, run it again, and all five files are rewritten.

## Credits

Steambirds Alliance is a game by Spry Fox. This is a fan-made, non-commercial community project and is not
affiliated with or endorsed by Spry Fox; the original logo and artwork belong to them.
