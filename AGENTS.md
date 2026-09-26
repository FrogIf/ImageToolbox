# AGENTS.md

Windows desktop "图片工具箱" (Image Toolbox): a tabbed shell that hosts multiple image tools. Current tools: "批量处理" (batch format conversion / resize / rename / rotate-flip / watermark), "裁剪构图" (crop / straighten / canvas), "切图拼图" (grid slicing / collage), "证件照" (ID photo + sheet), "局部覆盖" (copy a selected rectangle from an overlay image onto the same region of a target image), "风格调整" (preset color-matrix filters + manual panel + spatial effects), "特效" (mosaic / gaussian / motion blur / oil paint / sketch / emboss / edge detect / background blur / paper texture / glow / rounded corners / circle avatar / drop shadow), "画笔打码" (paint a soft-edged brush mask over an image to blur or mosaic just the painted area), "图像调整" (levels/curves/white-balance/HSL/local mask/toning/LUT), and "取色配色" (match one image's global tone/color to another via statistics). C# WinForms UI + WPF/WIC imaging.

## Build

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

Output: `ImageToolbox.exe` in the repo root. `build.ps1` compiles every `*.cs` in the repo root, so new source files are picked up automatically. Always rebuild after editing `.cs` files; the `.exe` is committed/present and does not auto-update.

## Toolchain constraints (easy to get wrong)

- There is **no** `dotnet` SDK, Visual Studio, `.csproj`/`.sln`, NuGet, or `nuget` on PATH. Build uses the in-box legacy compiler `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.
- That compiler is **C# 5**. Do not use string interpolation (`$"..."`), null-conditional (`?.`), expression-bodied members, auto-property initializers, `nameof`, or other C# 6+ syntax — it will not compile.
- `build.ps1` resolves the WPF assemblies `PresentationCore`, `WindowsBase`, `System.Xaml` by searching the GAC at `C:\Windows\Microsoft.NET\assembly`. Do not replace them with simple `/r:Name.dll` references; csc cannot resolve them that way here.
- `PixelFormat` is ambiguous between `System.Drawing.Imaging` and `System.Windows.Media`. `ImageBatch.cs` and `ImageUtil.cs` disambiguate with `using PixelFormat = System.Drawing.Imaging.PixelFormat;` — keep those aliases.

## Architecture

- `ToolPage.cs` — abstract `ToolPage : UserControl` base (`ToolName`, `Shutdown`). Each tool is one page.
- `Program.cs` — `MainForm` shell: a `TabControl` (docked fill) + `Program.Main` entrypoint. `MainForm.AddTool(ToolPage)` adds a tab; `OnFormClosing` calls `Shutdown` on every tool.
- `BatchProcessPage.cs` — the `"批量处理"` tool page (file list/thumbnails + a scrollable options panel: 输出格式 / 尺寸 / 旋转翻转 / 重命名 / 水印). Processing and thumbnails each run in their own `BackgroundWorker`. `BuildOptions()` snapshots the UI into a `BatchOptions` on the UI thread; the worker only touches that snapshot. A watermark image is preloaded on the UI thread and disposed in the worker's `finally`.
- `LocalOverlayPage.cs` — the `"局部覆盖"` tool page; loads two images, `ImageCanvas` handles drag-selection. The selection is in **target** image pixels; `ImageUtil.MapRegion` maps it to the overlay by relative position (target and overlay may differ in size, so the overlay region is scaled up to the target region).
- `StyleAdjustPage.cs` — the `"风格调整"` tool page; preset color filters + a manual adjustment panel (`亮度/对比度/饱和度/色温/色调`) + spatial effects (暗角/柔焦/锐化/颗粒/漏光/边框). Presets and builders live in `ImageEffects.cs`; strength lerps the preset matrix toward identity; slider changes are debounced by a `Timer`. Preview renders on a downscaled copy (`ImageUtil.CreatePreview`), full-res only on save.
- `ColorMatchPage.cs` — the `"取色配色"` tool page; measures two images with `ImageEffects.MeasureStats`, derives the 5 adjustment params via `ImageEffects.EstimateAdjustment` (iterative residual refinement), then reuses the same color-matrix pipeline.
- `ImageEffects.cs` — `ColorMatrix` builders (`Brightness`, `Contrast`, `Saturation`, `Temperature`, `Tint`, `Grayscale`, `Sepia`, `Multiply`) and `PresetMatrix(name)`; in-place spatial effects (`Vignette`, `Soften`, `Sharpen`, `Grain`, `LightLeak`, `Frame`) that expect a 32bppArgb bitmap; and statistics (`ColorStats`/`MeasureStats`/`EstimateAdjustment`/`MatrixFromParams`) for color matching.
- `EffectsPage.cs` — the `"特效"` tool page; a `ComboBox` selects one of 13 effects (马赛克/高斯模糊/动感模糊/油画/素描/浮雕/边缘检测/背景虚化/纸张纹理/光晕/圆角/圆形头像/投影), each exposing up to two sliders (`ConfigureEffect` sets labels/ranges/defaults and the `说明` note). Preview uses `ImageUtil.CreatePreview` (downscaled to 1000) debounced by a `Timer`; `Render` clones the base and dispatches into `ImageFilters`; `Circle`/`DropShadow` return a **different-sized** bitmap. Full-res on save.
- `ImageFilters.cs` — no WinForms/WPF UI deps; heavier pixel filters operating on 32bppArgb bitmaps: `Clone`, `Mosaic`, `GaussianBlur` (3× separable `BoxBlur`), `MotionBlur`, `OilPaint`, `Sketch`, `Emboss`, `EdgeDetect`, `BackgroundBlur` (radial clear-zone blend), `Paper`, `Glow` (bright-pass + screen), `RoundedCorners`, `Circle` and `DropShadow` (both return new bitmaps), and `MaskBlend(base, effect, mask)` which lerps toward `effect` by the mask's R channel. `CopyPixels`/`PastePixels` are the LockBits helpers.
- `ImageUtil.cs` — generic WIC-backed image helpers: `LoadImage`, `CreatePreview`, `LoadThumbnail`, `Compose`, `MapRegion`, `ApplyColorMatrix`, `SavePng`, private `ToBitmap`. Always converts to `Bgra32` before copying pixels, so opaque images don't end up with alpha 0.
- `ImageCanvas.cs` — custom `Control` that displays an image letterboxed and supports rubber-band selection in image pixel coordinates (`ReadOnly` mode shows a non-editable selection); `LockAspect > 0` constrains the selection to a fixed width/height ratio; `PixelClicked` reports the clicked image-pixel coordinate (used for white-balance sampling and local-mask center). `BrushEnabled` switches mouse handling to freehand painting: `BrushStarted`/`BrushMoved`/`BrushFinished` report image-pixel points, and a `BrushRadius` (in displayed-image pixels) circle follows the cursor. `BrushEnabled` and selection mode are mutually exclusive.
- `BrushBlurPage.cs` — the `"画笔打码"` tool page; paint with the mouse to blur or mosaic only the painted area. Strokes are stored in **source-image pixels** (radius in source px) so the same strokes replay on the downscaled preview and the full-res save. For smooth painting it keeps a persistent preview mask and result bitmap: each mouse move stamps a prebuilt soft brush (`CreateStamp`, opaque core + `GaussianBlur` rim) only along the new segment and re-blends just the dirty rectangle (`BlendRegion`, alpha-channel coverage), so no full-image rebuild happens while dragging. The blurred/mosaicked base is cached and only recomputed when the mode/strength changes (debounced by a `Timer`); `撤销一笔`/`清除全部` rebuild the mask/result from strokes. Full-res save replays the strokes onto a full-size mask and uses `ImageFilters.MaskBlend`.
- `ImageBatch.cs` — no WinForms/WPF UI deps; the batch pipeline: `Run` (per-file entry point), `Resize`, `GetExifOrientation`/`ApplyOrientation`, `BuildName`/`ResolveTargetPath` (collision-safe), `DrawWatermark` (text/image, nine-grid, tiling, opacity, rotation), and `Save` (PNG/JPG/BMP/GIF/TIFF, JPEG quality, transparency flattening).
- `CropComposePage.cs` — the `"裁剪构图"` tool page; `ImageCanvas` drag-selection with optional fixed ratio (`LockAspect`), straighten/rotate with auto edge-crop, and canvas extend/compress with nine-grid anchor + fill/transparent.
- `SliceCollagePage.cs` — the `"切图拼图"` tool page; a `TabControl` with nine-grid slicing (grid overlay preview, `原名_r行_c列.png`) and collage (horizontal/vertical/grid, spacing, background, optional coverage crop).
- `IdPhotoPage.cs` — the `"证件照"` tool page; size presets in mm + DPI, fit-vs-fill mode, background color, and `BuildSheet` tiling onto 5寸/6寸/A4 paper with optional cut lines.
- `ImageLayout.cs` — no WinForms/WPF UI deps; composition logic: `Crop`, `Rotate`/`LargestInscribed` (straighten), `ExtendCanvas`, `Slice`, `Collage`, `BuildIdPhoto`/`BuildSheet`, `DrawInBox`.
- `ImageAdjustPage.cs` — the `"图像调整"` tool page; a `TabControl` (色阶/曲线/白平衡/HSL/局部/色调/LUT) drives a single `TuningState`; preview is debounced by a `Timer` and rendered on `ImageUtil.CreatePreview` (full-res on save). Curves, histogram and pixel-sampling UI live in `CurveEditor`/`HistogramView`/`ImageCanvas.PixelClicked`.
- `ImageTuning.cs` — no WinForms/WPF UI deps; `Histogram`, `AutoLevels`, `AutoWhiteBalance`, `WhiteBalanceFromColor`, `BuildCurveLut` (monotone cubic), `Apply` (one per-pixel pass for white balance/levels/curve/HSL/toning + a second pass for the local mask), `TryLoadCube` and 3D trilinear LUT sampling, gradient presets.
- `HistogramView.cs` / `CurveEditor.cs` — custom `Control`s for the histogram display and draggable curve editing (add/drag/right-click-delete control points).
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
- For headless logic checks, reference the built `ImageToolbox.exe` (copy it next to the temp harness `.exe` so it resolves at runtime) and call `ImageBatch`/`ImageUtil` from a temporary console harness; do not add test files to the repo.
- Console output for Chinese text may appear as mojibake on this machine; source files and `使用说明.md` are UTF-8 and should stay UTF-8.
