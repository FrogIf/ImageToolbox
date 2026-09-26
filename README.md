# 图片工具箱 (Image Toolbox)

一个用 C# / WinForms 编写的 Windows 桌面工具箱，以多标签页形式集成多种图片处理功能。界面基于 WinForms，图片解码/编码基于 WPF / WIC，无需安装 .NET SDK 即可编译运行。

## 功能

| 工具 | 说明 |
| --- | --- |
| **批量处理** | 批量转换格式（PNG/JPG/BMP/GIF/TIFF，WebP 可作输入）、缩放、旋转翻转（含 EXIF 自动校正）、重命名（模板/序号/查找替换）、加水印（文字/图片、九宫格、平铺、透明度）。 |
| **裁剪构图** | 自由/固定比例（1:1、16:9、3:2、4:3…）裁剪，旋转拉直 + 自动裁边，画布扩展留白 / 压缩裁剪（九宫格锚点）。 |
| **切图拼图** | 九宫格切图（行×列，带切分线预览）；多图横向/纵向/网格拼图，可设间距、边距、背景色、输出长边。 |
| **证件照** | 一寸/二寸等标准尺寸 + DPI，裁剪填满 / 完整留白，换背景色，并排版到 5寸/6寸/A4 相纸（可加裁剪线）。 |
| **局部覆盖** | 用一张覆盖图替换目标图的一个或多个区域（按比例对应），支持边缘羽化与不透明度。 |
| **风格调整** | 13 种预设滤镜 + 手动调色（亮度/对比度/饱和度/色温/色调）+ 空间特效（暗角/柔焦/锐化/颗粒/漏光/边框），实时预览。 |
| **特效** | 马赛克、高斯模糊、动感模糊、油画、素描、浮雕、边缘检测、背景虚化（人像近似）、纸张纹理、光晕、圆角、圆形头像、投影，实时预览。 |
| **画笔打码** | 用画笔在图上涂抹，只对涂过的地方做模糊或马赛克（软边蒙版），可调笔刷/强度、撤销一笔、清除全部。 |
| **图像调整** | 直方图 + 色阶 / 曲线、白平衡取点、HSL、径向/线性局部蒙版、色调分离 / 双色调 / 渐变映射、.cube 3D LUT，实时预览。 |
| **取色配色** | 分析参考图的整体影调与色调（亮度、对比度、饱和度、冷暖、品绿），把风格统计迁移到目标图。 |
| **图片信息** | 查看尺寸、格式、DPI、文件大小、色彩统计、直方图与 EXIF（相机/时间/曝光/ISO/GPS），可去除元数据并另存。 |
| **图像对比** | 两张图左右/上下并排、滑块对比、差异高亮。 |
| **图层合成** | 多张图层自下而上叠加，支持 11 种混合模式（正常/正片叠底/滤色/叠加/柔光/强光/差值/变暗/变亮/相加/相减）与不透明度。 |
| **抠图** | 颜色阈值（全局）或魔术棒（连续）把相近颜色变为透明，可反转，输出 PNG。 |
| **颜色工具** | 点击取色（RGB/HEX/HSV）+ 中位切分法提取主色调色板。 |
| **批量导出** | 每个文件按多个尺寸批量导出，并可打包多尺寸 `.ico` 图标。 |

详细用法见 [`使用说明.md`](使用说明.md)。

## 环境要求

- Windows 7 及以上（推荐 Windows 11）
- .NET Framework 4.x（Windows 自带，无需额外安装）
- WebP 解码依赖系统 WIC 编解码器：
  - Windows 11：内置，开箱即用
  - Windows 10：如提示无法解码，请在 Microsoft Store 安装 “WebP Image Extensions”

## 编译

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

脚本调用系统自带的旧版编译器 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，编译仓库根目录下所有 `.cs`，在仓库根目录生成 `ImageToolbox.exe`。

> 该编译器为 **C# 5**，不支持字符串插值、`?.`、表达式成员等 C# 6+ 语法。

## 运行

双击 `ImageToolbox.exe` 启动，窗口标题为“图片工具箱”。

## 项目结构

```
ImageToolbox/
├── src/
│   ├── App/            # 程序外壳
│   │   ├── Program.cs          # 主窗口外壳（TabControl）+ 程序入口
│   │   └── ToolPage.cs         # 工具页抽象基类（ToolName / Shutdown）
│   ├── Controls/       # 自定义控件
│   │   ├── ImageCanvas.cs      # 可框选 / 画笔涂抹的图片显示控件
│   │   ├── HistogramView.cs    # 直方图显示控件
│   │   └── CurveEditor.cs      # 曲线编辑控件
│   ├── Imaging/        # 纯逻辑（无 UI 依赖）
│   │   ├── ImageUtil.cs        # 通用图片加载 / 合成 / 保存（WIC 解码）
│   │   ├── ImageEffects.cs     # 色彩矩阵、预设滤镜、空间特效、取色统计
│   │   ├── ImageFilters.cs     # 像素级特效（模糊/油画/素描/浮雕/边缘/背景虚化/光晕/圆角/投影）
│   │   ├── ImageBatch.cs       # 批量处理逻辑（格式/缩放/旋转/重命名/水印）
│   │   ├── ImageLayout.cs      # 构图逻辑（裁剪/旋转校正/画布/切图/拼图/证件照）
│   │   ├── ImageTuning.cs      # 调色逻辑（色阶/曲线/白平衡/HSL/色调/LUT/局部）
│   │   ├── ImageMeta.cs        # 图片信息 / EXIF 读取 / 去除元数据
│   │   ├── ImageCompare.cs     # 对比逻辑（并排/滑块/差异）
│   │   ├── ImageBlend.cs       # 图层混合模式
│   │   ├── ImageMatting.cs     # 抠图逻辑（颜色阈值/魔术棒）
│   │   ├── PaletteExtractor.cs # 中位切分调色板提取、颜色转换
│   │   └── MultiSizeExport.cs  # 多尺寸缩放与 .ico 写入
│   └── Pages/          # 各工具页（每页一个标签）
│       ├── BatchProcessPage.cs     # “批量处理”
│       ├── CropComposePage.cs      # “裁剪构图”
│       ├── SliceCollagePage.cs     # “切图拼图”
│       ├── IdPhotoPage.cs          # “证件照”
│       ├── LocalOverlayPage.cs     # “局部覆盖”
│       ├── StyleAdjustPage.cs      # “风格调整”
│       ├── EffectsPage.cs          # “特效”
│       ├── BrushBlurPage.cs        # “画笔打码”
│       ├── ImageAdjustPage.cs      # “图像调整”
│       ├── ColorMatchPage.cs       # “取色配色”
│       ├── ImageInfoPage.cs        # “图片信息”
│       ├── ImageComparePage.cs     # “图像对比”
│       ├── LayerComposePage.cs     # “图层合成”
│       ├── MattingPage.cs          # “抠图”
│       ├── ColorToolPage.cs        # “颜色工具”
│       └── MultiSizeExportPage.cs  # “批量导出”
├── app.manifest        # 应用程序清单（DPI 感知）
├── app.ico             # 程序图标
├── build.ps1           # 一键编译脚本（递归编译 src 下所有 .cs）
└── 使用说明.md          # 详细使用说明书
```

## 扩展新工具

1. 在 `src/Pages/` 新建类继承 `ToolPage`，在构造函数里搭建界面：

   ```csharp
   public class MyToolPage : ToolPage
   {
       public override string ToolName { get { return "我的工具"; } }
   }
   ```

2. 在 `src/App/Program.cs` 的 `MainForm` 构造函数中注册：

   ```csharp
   AddTool(new MyToolPage());
   ```

3. 重新运行 `build.ps1`，新工具即成为一个标签页。

## 许可

见 [`LICENSE`](LICENSE)。
