# 图片工具箱 (Image Toolbox)

一个用 C# / WinForms 编写的 Windows 桌面工具箱，以多标签页形式集成多种图片处理功能。界面基于 WinForms，图片解码/编码基于 WPF / WIC，无需安装 .NET SDK 即可编译运行。

## 功能

| 工具 | 说明 |
| --- | --- |
| **批量处理** | 批量转换格式（PNG/JPG/BMP/GIF/TIFF，WebP 可作输入）、缩放、旋转翻转（含 EXIF 自动校正）、重命名（模板/序号/查找替换）、加水印（文字/图片、九宫格、平铺、透明度）。 |
| **局部覆盖** | 用一张覆盖图的某个区域，替换目标图相同位置（按比例对应），可框选预览后保存。 |
| **风格调整** | 13 种预设滤镜 + 手动调色（亮度/对比度/饱和度/色温/色调）+ 空间特效（暗角/柔焦/锐化/颗粒/漏光/边框），实时预览。 |
| **取色配色** | 分析参考图的整体影调与色调（亮度、对比度、饱和度、冷暖、品绿），把风格统计迁移到目标图。 |

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
├── ToolPage.cs         # 工具页抽象基类（ToolName / Shutdown）
├── Program.cs          # 主窗口外壳（TabControl）+ 程序入口
├── BatchProcessPage.cs # “批量处理”工具页
├── LocalOverlayPage.cs # “局部覆盖”工具页
├── StyleAdjustPage.cs  # “风格调整”工具页
├── ColorMatchPage.cs   # “取色配色”工具页
├── ImageCanvas.cs      # 可框选的图片显示控件
├── ImageEffects.cs     # 色彩矩阵、预设滤镜、空间特效、取色统计
├── ImageUtil.cs        # 通用图片加载 / 合成 / 保存（WIC 解码）
├── ImageBatch.cs       # 批量处理逻辑（格式/缩放/旋转/重命名/水印）
├── app.manifest        # 应用程序清单（DPI 感知）
├── app.ico             # 程序图标
├── build.ps1           # 一键编译脚本
└── 使用说明.md          # 详细使用说明书
```

## 扩展新工具

1. 新建类继承 `ToolPage`，在构造函数里搭建界面：

   ```csharp
   public class MyToolPage : ToolPage
   {
       public override string ToolName { get { return "我的工具"; } }
   }
   ```

2. 在 `Program.cs` 的 `MainForm` 构造函数中注册：

   ```csharp
   AddTool(new MyToolPage());
   ```

3. 重新运行 `build.ps1`，新工具即成为一个标签页。

## 许可

见 [`LICENSE`](LICENSE)。
