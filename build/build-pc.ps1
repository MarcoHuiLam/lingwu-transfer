# 编译 Windows 端 exe（零依赖：使用 Windows 自带 .NET Framework 编译器）
# 用法: powershell -ExecutionPolicy Bypass -File build/build-pc.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$src  = Join-Path $root 'src\pc'
$dist = Join-Path $root 'dist'
$ico  = Join-Path $root 'build\icon\app.ico'
New-Item -ItemType Directory -Path $dist -Force | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw '找不到 csc.exe（需要 .NET Framework 4.x）' }

if (-not (Test-Path $ico)) {
    Write-Host '未找到图标，先用 build/make-icons.ps1 生成…'
    & (Join-Path $PSScriptRoot 'make-icons.ps1')
}

# 在纯 ASCII 临时目录编译：避开中文/空格路径引起的参数传递问题
$work = Join-Path $env:TEMP 'lingwu-pc-build'
Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $work -Force | Out-Null
Get-ChildItem -LiteralPath $src -Filter *.cs | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $work }
Copy-Item -LiteralPath $ico -Destination $work

Push-Location $work
try {
    & $csc /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /win32icon:app.ico `
        /out:LingwuTransfer.exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
        Program.cs Config.cs Server.cs Ui.cs MainForm.cs
    if ($LASTEXITCODE -ne 0) { throw "csc 编译失败，退出码 $LASTEXITCODE" }
}
finally { Pop-Location }

$outFile = Join-Path $dist '凌雾跨设备传输.exe'
Move-Item -LiteralPath (Join-Path $work 'LingwuTransfer.exe') -Destination $outFile -Force
$f = Get-Item -LiteralPath $outFile
Write-Host ("编译成功: {0}  {1:N1} KB" -f $f.Name, ($f.Length / 1KB))
