# make-icon

Builds the app logo and the Windows icon from the logo art.

The source of truth is `Content/Assets/Images/IELTop-rounded.png`. Replace it to
change the look, then run this script again.

## Output

`Content/Assets/Images`:

- `app.ico` is a multi size Windows icon (16, 24, 32, 48, 64, 128, 256). It is
  wired into the build through `ApplicationIcon` in `IELTop.csproj` and used by
  the Velopack installer.
- `logo-256.png` is the square logo for docs and the store.
- `logo-64.png` is the small logo for the README.

Every size keeps the full logo, exactly like the source art, so the app icon
matches the supplied PNG and SVG.

## Run

```powershell
conda env create -f environment.yml
conda run -n ieltop-icon python make_icon.py
```

## Notes

- Do not edit `app.ico`, `logo-256.png`, or `logo-64.png` by hand. Run this
  script instead.
- Keep the logo art as a transparent PNG. The outer corners are transparent,
  which the rounded app tile relies on.
