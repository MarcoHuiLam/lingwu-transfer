# 凌雾跨设备传输 · 一键签名打包
# 用法： powershell -ExecutionPolicy Bypass -File build/release.ps1
#
# 它会：读签名配置 → 找构建工具 → 编译 APK → 用正式密钥签名 → 验签 → 打印版本/指纹/SHA256
# 签名配置优先读 build\签名配置.ps1（已被 .gitignore 忽略，不会上传）
param([string]$StageDir = '')
$ErrorActionPreference = 'Stop'
$here     = $PSScriptRoot
$repoRoot = Split-Path -Parent $here
$dist     = Join-Path $repoRoot 'dist'

Write-Host '===== 凌雾跨设备传输 · 一键签名打包 =====' -ForegroundColor Cyan
Write-Host "仓库: $repoRoot"

# ---------- 1. 签名配置 ----------
$cfgCandidates = @(
    (Join-Path $here '签名配置.ps1'),
    (Join-Path $here '签名配置.ps1.ps1'),   # 兼容 Windows 隐藏扩展名导致的重复 .ps1
    (Join-Path $here '签名配置.txt')
)
$cfgPath = $cfgCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

if ($cfgPath) {
    . $cfgPath
    Write-Host "签名配置: $cfgPath"
} else {
    Write-Host '未找到 build\签名配置.ps1，正在自动识别…' -ForegroundColor Yellow
    $defaultKs = 'D:\凌雾签名\lingwu-release.keystore'
    if (-not (Test-Path -LiteralPath $defaultKs)) {
        Write-Host ''
        Write-Host '找不到签名配置，也找不到默认密钥。请二选一：' -ForegroundColor Red
        Write-Host '  A) 把 build\签名配置.example.ps1 复制成 build\签名配置.ps1，填好密钥路径与密码'
        Write-Host '  B) 直接在命令后面加参数： -Keystore <密钥路径> -KsPass <密码>'
        exit 1
    }
    $SignKeystore = $defaultKs
    $SignKeyAlias = 'lingwu'
    $pwdFile = 'D:\凌雾签名\密码与指纹.txt'
    if (Test-Path -LiteralPath $pwdFile) {
        $line = (Get-Content -LiteralPath $pwdFile -Encoding UTF8 | Select-String -Pattern '^密码').Line
        if ($line) { $SignStorePass = ($line -split '：')[1].Trim() }
    }
    if (-not $SignStorePass) {
        $sec = Read-Host '请输入 keystore 密码（输入不显示）' -AsSecureString
        $SignStorePass = [Runtime.InteropServices.Marshal]::PtrToStringBSTR(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec))
    }
    Write-Host "已自动识别密钥: $SignKeystore"
}

if (-not $SignKeyAlias) { $SignKeyAlias = 'lingwu' }
if (-not (Test-Path -LiteralPath $SignKeystore)) { throw "密钥文件不存在：$SignKeystore" }

# ---------- 2. 构建工具 ----------
function Resolve-Tool([string]$configured, [string]$kind) {
    if ($configured -and (Test-Path -LiteralPath $configured)) { return $configured }
    if ($kind -eq 'jdk' -and $env:JAVA_HOME) {
        $p = Join-Path $env:JAVA_HOME 'bin'; if (Test-Path $p) { return $p }
    }
    if ($kind -eq 'jar') {
        foreach ($root in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)) {
            if (-not $root) { continue }
            $p = Get-ChildItem -Path (Join-Path $root 'platforms') -Filter android.jar -Recurse -ErrorAction SilentlyContinue |
                 Sort-Object FullName -Descending | Select-Object -First 1
            if ($p) { return $p.FullName }
        }
    }
    if ($kind -eq 'buildtools') {
        foreach ($root in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT)) {
            if (-not $root) { continue }
            $p = Get-ChildItem -Path (Join-Path $root 'build-tools') -Filter aapt2.exe -Recurse -ErrorAction SilentlyContinue |
                 Sort-Object FullName -Descending | Select-Object -First 1
            if ($p) { return $p.Directory.FullName }
        }
    }
    # 最后兜底：在仓库上级目录里找 _toolchain
    $parent = Split-Path $repoRoot -Parent
    $tc = Get-ChildItem -Path $parent -Directory -Recurse -Depth 4 -Filter '_toolchain' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($tc) {
        switch ($kind) {
            'jdk'  { $p = Get-ChildItem -Path $tc.FullName -Filter javac.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1; if ($p) { return $p.Directory.FullName } }
            'jar'  { $p = Get-ChildItem -Path $tc.FullName -Filter android.jar -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1; if ($p) { return $p.FullName } }
            'buildtools' { $p = Get-ChildItem -Path $tc.FullName -Filter aapt2.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1; if ($p) { return $p.Directory.FullName } }
        }
    }
    return $null
}

$JdkBin     = Resolve-Tool $JdkBin     'jdk'
$AndroidJar = Resolve-Tool $AndroidJar 'jar'
$BuildTools = Resolve-Tool $BuildTools 'buildtools'

if (-not $JdkBin)     { throw '找不到 JDK（javac.exe）。请在 签名配置.ps1 里填写 $JdkBin' }
if (-not $AndroidJar) { throw '找不到 android.jar。请在 签名配置.ps1 里填写 $AndroidJar' }
if (-not $BuildTools) { throw '找不到 Android build-tools。请在 签名配置.ps1 里填写 $BuildTools' }

Write-Host "JDK       : $JdkBin"
Write-Host "android.jar: $AndroidJar"
Write-Host "build-tools: $BuildTools"

# ---------- 3. 编译 + 签名 ----------
& (Join-Path $here 'build-apk.ps1') `
    -JdkBin $JdkBin -AndroidJar $AndroidJar -BuildTools $BuildTools `
    -Keystore $SignKeystore -KsPass $SignStorePass -KeyAlias $SignKeyAlias -StageDir $StageDir
if ($LASTEXITCODE -ne 0) { throw '打包失败（见上面的错误）' }

# ---------- 4. 自动验收 ----------
$apk = Join-Path $dist '凌雾跨设备传输.apk'
if (-not (Test-Path -LiteralPath $apk)) { throw "没有产出 $apk" }

$env:JAVA_HOME = Split-Path (Split-Path $JdkBin -Parent) -Parent
$apksigner = Join-Path $BuildTools 'apksigner.bat'
$aapt2     = Join-Path $BuildTools 'aapt2.exe'

Write-Host ''
Write-Host '----- 验收结果 -----' -ForegroundColor Cyan
if (Test-Path $aapt2) {
    $badging = (& $aapt2 dump badging $apk 2>&1) -join "`n"
    if ($badging -match "versionCode='(\d+)'\s+versionName='([^']+)'") {
        Write-Host ("版本    : v" + $Matches[2] + " (versionCode " + $Matches[1] + ")")
    }
    if ($badging -match "application-label:'([^']+)'") { Write-Host ("应用名  : " + $Matches[1]) }
}
(& $apksigner verify --print-certs $apk 2>&1) | Select-String -Pattern 'SHA-256 digest' |
    ForEach-Object { Write-Host ("签名指纹: " + ($_.Line -replace '.*digest: *','')) }
$sha = (Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash
Write-Host ("文件    : $apk")
Write-Host ("大小    : {0:N1} KB" -f ((Get-Item $apk).Length / 1KB))
Write-Host ("SHA-256 : $sha")
Write-Host ''
Write-Host '把上面这行 SHA-256 贴到 GitHub Release 说明里，用户就能核对安装包没被改过。' -ForegroundColor Green
