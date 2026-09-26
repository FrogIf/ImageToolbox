$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$out = Join-Path $root "ImageToolbox.exe"

function Find-Assembly([string]$name) {
    $base = "C:\Windows\Microsoft.NET\assembly"
    $found = Get-ChildItem $base -Recurse -Filter "$name.dll" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $found) { throw "找不到程序集 $name.dll" }
    return $found.FullName
}

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) { throw "找不到 C# 编译器 csc.exe，请安装 .NET Framework 4.x" }

$refs = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    (Find-Assembly "PresentationCore"),
    (Find-Assembly "WindowsBase"),
    (Find-Assembly "System.Xaml")
)

$manifest = Join-Path $root "app.manifest"
$icon = Join-Path $root "app.ico"
$args = @("/nologo", "/target:winexe", "/platform:anycpu")
$args += "/out:`"$out`""
$args += "/win32manifest:`"$manifest`""
if (Test-Path $icon) { $args += "/win32icon:`"$icon`"" }
foreach ($r in $refs) { $args += "/r:`"$r`"" }
$sources = Get-ChildItem -Path $root -Recurse -Filter *.cs | ForEach-Object { $_.FullName }
$args += $sources

& $csc $args
if ($LASTEXITCODE -ne 0) { throw "编译失败" }
Write-Host "编译成功: $out"
