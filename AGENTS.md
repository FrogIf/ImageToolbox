# AGENTS.md

Windows desktop "图片工具箱" (Image Toolbox): a tabbed shell that hosts multiple image tools. Current tools: batch WebP -> PNG, "局部覆盖" (copy a selected rectangle from an overlay image onto the same region of a target image), "风格调整" (preset color-matrix filters + manual panel + spatial effects), and "取色配色" (match one image's global tone/color to another via statistics). C# WinForms UI + WPF/WIC imaging.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Output: `ImageToolbox.exe` in the repo root. `build.ps1` compiles every `*.cs` in the repo root, so new source files are picked up automatically. Always rebuild after editing `.cs` files; the `.exe` is committed/present and does not auto-update.

## Toolchain constraints (easy to get wrong)

- There is **no** `dotnet` SDK, Visual Studio, `.csproj`/`.sln`, NuGet, or `nuget` on PATH. Build uses the in-box legacy compiler `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.
- That compiler is **C# 5**. Do not use string interpolation (`$"..."`), null-conditional (`?.`), expression-bodied members, auto-property initializers, `nameof`, or other C# 6+ syntax — it will not compile.
- `build.ps1` resolves the WPF assemblies `PresentationCore`, `WindowsBase`, `System.Xaml` by searching the GAC at `C:\Windows\Microsoft.NET\assembly`. Do not replace them with simple `/r:Name.dll` references; csc cannot resolve them that way here.
- `PixelFormat` is ambiguous between `System.Drawing.Imaging` and `System.Windows.Media`. `WebPConverter.cs` disambiguates with `using PixelFormat = System.Drawing.Imaging.PixelFormat;` — keep that alias.

## Architecture

- `ToolPage.cs` — abstract `ToolPage : UserControl` base (`ToolName`, `Shutdown`). Each tool is one page.
- `Program.cs` — `MainForm` shell: a `TabControl` (docked fill) + `Program.Main` entrypoint. `MainForm.AddTool(ToolPage)` adds a tab; `OnFormClosing` calls `Shutdown` on every tool.
- `WebPToPngPage.cs` — the `"WebP 转 PNG"` tool page (all list/thumbnail/convert UI); conversion and thumbnails each run in their own `BackgroundWorker`.
- `LocalOverlayPage.cs` — the `"局部覆盖"` tool page; loads two images, `ImageCanvas` handles drag-selection. The selection is in **target** image pixels; `ImageUtil.MapRegion` maps it to the overlay by relative position (target and overlay may differ in size, so the overlay region is scaled up to the target region).
- `StyleAdjustPage.cs` — the `"风格调整"` tool page; preset color filters + a manual adjustment panel (`亮度/对比度/饱和度/色温/色调`) + spatial effects (暗角/柔焦/锐化/颗粒/漏光/边框). Presets and builders live in `ImageEffects.cs`; strength lerps the preset matrix toward identity; slider changes are debounced by a `Timer`. Preview renders on a downscaled copy (`ImageUtil.CreatePreview`), full-res only on save.
- `ColorMatchPage.cs` — the `"取色配色"` tool page; measures two images with `ImageEffects.MeasureStats`, derives the 5 adjustment params via `ImageEffects.EstimateAdjustment` (iterative residual refinement), then reuses the same color-matrix pipeline.
- `ImageEffects.cs` — `ColorMatrix` builders (`Brightness`, `Contrast`, `Saturation`, `Temperature`, `Tint`, `Grayscale`, `Sepia`, `Multiply`) and `PresetMatrix(name)`; in-place spatial effects (`Vignette`, `Soften`, `Sharpen`, `Grain`, `LightLeak`, `Frame`) that expect a 32bppArgb bitmap; and statistics (`ColorStats`/`MeasureStats`/`EstimateAdjustment`/`MatrixFromParams`) for color matching.
- `ImageUtil.cs` — generic WIC-backed image helpers: `LoadImage`, `CreatePreview`, `LoadThumbnail`, `Compose`, `MapRegion`, `ApplyColorMatrix`, `SavePng`, private `ToBitmap`. Always converts to `Bgra32` before copying pixels, so opaque images don't end up with alpha 0.
- `ImageCanvas.cs` — custom `Control` that displays an image letterboxed and supports rubber-band selection in image pixel coordinates (`ReadOnly` mode shows a non-editable selection).
- `WebPConverter.cs` — no WinForms/WPF UI deps; pure logic: `Convert` (WebP -> PNG), `GetTargetPath` (collision-safe naming).
- Adding a new tool: subclass `ToolPage`, build its UI into itself, register it with `AddTool(...)` in the `MainForm` constructor. Namespace is `ImageToolbox`.
- WebP decoding uses **WIC** (`BitmapDecoder` / `BitmapImage`), not `System.Drawing`. GDI+ has no WebP codec: `Image.FromFile("*.webp")` throws `OutOfMemoryException`. Do not switch to System.Drawing for decoding.
- Decoding depends on the OS WebP WIC codec (built into Windows 11; Windows 10 needs the "WebP Image Extensions" Store package).
- WPF imaging objects must stay on the thread that creates them. `LoadThumbnail` runs on the worker thread and hands back only a `System.Drawing.Bitmap`.

## DPI / manifest

- `app.manifest` is embedded via `/win32manifest` for DPI awareness and must stay wired into `build.ps1`. Without it, Windows bitmap-stretches the window and text is blurry at 125%/150% scaling.
- `app.ico` is embedded via `/win32icon`; `MainForm` also sets its title-bar icon from `ExtractAssociatedIcon`. Regenerate the `.ico` with Pillow if needed.
- `MainForm` uses `AutoScaleMode.Dpi` with `AutoScaleDimensions = (96, 96)`.

## Verification

- No test suite, CI, linter, or formatter exists. There is no `dotnet test`.
- Verify changes by building (`build.ps1`) and launching `ImageToolbox.exe`.
- For headless logic checks, compile `WebPConverter.cs` into a temporary console harness with the same reference set as `build.ps1`; do not add test files to the repo.
- Console output for Chinese text may appear as mojibake on this machine; source files and `使用说明.md` are UTF-8 and should stay UTF-8.
