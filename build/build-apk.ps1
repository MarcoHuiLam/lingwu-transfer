# 手工构建 Android APK（不用 Gradle）：javac -> d8 -> aapt2 -> 加 dex -> zipalign -> apksigner
#
# 依赖：JDK 17 + Android SDK（build-tools 的 aapt2/d8/zipalign/apksigner + platform 的 android.jar）
# 推荐用 release.ps1 调用（它会读签名配置并传好参数）。
#
# 构建目录自动选择：用 javac 真编一个测试类来验证"能不能写"，依次尝试
#   %TEMP% → %LOCALAPPDATA%\Temp → 仓库 build\apk → 仓库 .build
#   → %USERPROFILE%\lingwu-build → D:\lingwu-build → C:\lingwu-build
# 第一个能写的就用；全失败时打印 javac 的真实原因，并提示用 -StageDir 手动指定。
param(
    [string]$JdkBin = "",
    [string]$AndroidJar = "",
    [string]$BuildTools = "",
    [string]$Keystore = "",
    [string]$KsPass = "android",
    [string]$KeyAlias = "lingwu",
    [string]$StageDir = ""
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$srcA = Join-Path $root 'src\android'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null

# ---------- 1. 定位工具 ----------
if (-not $JdkBin) {
    if ($env:JAVA_HOME) { $JdkBin = Join-Path $env:JAVA_HOME 'bin' }
    else { throw '请用 -JdkBin 指定 JDK 的 bin 目录（或设置 JAVA_HOME）' }
}
$javacExe = Join-Path $JdkBin 'javac.exe'
$keytool  = Join-Path $JdkBin 'keytool.exe'
if (-not (Test-Path $javacExe)) { throw "找不到 javac: $javacExe" }

if (-not $BuildTools) { throw '请用 -BuildTools 指定 Android build-tools 目录' }
$aapt2     = Join-Path $BuildTools 'aapt2.exe'
$d8Bat     = Join-Path $BuildTools 'd8.bat'
$zipalign  = Join-Path $BuildTools 'zipalign.exe'
$apksigner = Join-Path $BuildTools 'apksigner.bat'
foreach ($p in @($aapt2, $d8Bat, $zipalign, $apksigner)) {
    if (-not (Test-Path $p)) { throw "找不到 $p" }
}
if (-not $AndroidJar -or -not (Test-Path $AndroidJar)) { throw '请用 -AndroidJar 指定 android.jar' }

$env:JAVA_HOME = Split-Path $JdkBin -Parent
$env:PATH = $JdkBin + ';' + $env:PATH

# ---------- 2. 选一个 javac 真能写的构建目录 ----------
$script:lastJavacError = ''

function Test-JavacWritable([string]$dir) {
    $script:lastJavacError = ''
    try {
        [void][IO.Directory]::CreateDirectory($dir)
        $probeSrc = Join-Path $dir '_probe'
        [void][IO.Directory]::CreateDirectory($probeSrc)
        $javaFile = Join-Path $probeSrc 'Probe.java'
        [IO.File]::WriteAllText($javaFile, "public class Probe { public static void main(String[] a) {} }",
            (New-Object System.Text.UTF8Encoding($false)))
        $outDir = Join-Path $dir '_probe_out'
        $err = (& $javacExe -nowarn -d $outDir $javaFile 2>&1 | Out-String)
        $ok = Test-Path (Join-Path $outDir 'Probe.class')
        if (-not $ok) {
            $line = ($err -split "`r?`n" | Where-Object { $_ -match '\S' } | Select-Object -First 1)
            if ($line) { $script:lastJavacError = $line.Trim() }
        }
        Remove-Item -LiteralPath $probeSrc -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $outDir -Recurse -Force -ErrorAction SilentlyContinue
        return $ok
    } catch {
        $script:lastJavacError = $_.Exception.Message
        return $false
    }
}

$candidates = @()
if ($StageDir) { $candidates += $StageDir }
if ($env:TEMP)         { $candidates += (Join-Path $env:TEMP 'lingwu-apk-build') }
if ($env:LOCALAPPDATA) { $candidates += (Join-Path $env:LOCALAPPDATA 'Temp\lingwu-apk-build') }
$candidates += (Join-Path $root 'build\apk')
$candidates += (Join-Path $root '.build')
if ($env:USERPROFILE) { $candidates += (Join-Path $env:USERPROFILE 'lingwu-build') }
$candidates += 'D:\lingwu-build'
$candidates += 'C:\lingwu-build'

$stage = $null
foreach ($c in $candidates) {
    if (-not $c) { continue }
    Remove-Item -LiteralPath $c -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "尝试构建目录: $c"
    if (Test-JavacWritable $c) { $stage = $c; break }
    if ($script:lastJavacError) {
        Write-Host "  ✗ 不可写：$($script:lastJavacError)" -ForegroundColor Yellow
    } else {
        Write-Host '  ✗ 不可写' -ForegroundColor Yellow
    }
}
if (-not $stage) {
    throw '找不到 javac 可写的构建目录。请手动指定 -StageDir，例如 -StageDir "D:\lwbuild"（路径短、纯英文最好）'
}
Write-Host "使用构建目录: $stage" -ForegroundColor Green
Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue

# ---------- 3. 准备 ----------
New-Item -ItemType Directory -Path $stage, "$stage\obj", "$stage\dex", "$stage\out" -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $srcA 'res') -Destination $stage -Recurse -Force
Copy-Item -LiteralPath (Join-Path $srcA 'AndroidManifest.xml') -Destination $stage -Force
Copy-Item -LiteralPath $AndroidJar -Destination "$stage\android.jar" -Force

Push-Location $stage
try {
    $javaFiles = @(Get-ChildItem -Path (Join-Path $srcA 'java') -Filter *.java -Recurse | ForEach-Object { $_.FullName })

    Write-Host '[1/6] javac'
    & $javacExe -encoding UTF-8 -source 8 -target 8 -nowarn -bootclasspath android.jar -d obj $javaFiles
    if ($LASTEXITCODE -ne 0) { throw 'javac 失败' }

    Write-Host '[2/6] d8'
    $classFiles = @(Get-ChildItem -Path (Join-Path $stage 'obj') -Filter *.class -Recurse | ForEach-Object { $_.FullName })
    if ($classFiles.Count -eq 0) { throw 'javac 没产出 class 文件' }
    & $d8Bat --release --lib android.jar --min-api 24 --output dex $classFiles
    if (-not (Test-Path (Join-Path $stage 'dex\classes.dex'))) { throw 'd8 未生成 classes.dex' }

    Write-Host '[3/6] aapt2'
    & $aapt2 compile --dir res -o out\res.zip
    if ($LASTEXITCODE -ne 0) { throw 'aapt2 compile 失败' }
    & $aapt2 link -o out\base.apk -I android.jar --manifest AndroidManifest.xml `
        --min-sdk-version 24 --target-sdk-version 34 --auto-add-overlay out\res.zip
    if ($LASTEXITCODE -ne 0) { throw 'aapt2 link 失败' }

    Write-Host '[4/6] 打包 dex'
    Copy-Item -LiteralPath (Join-Path $stage 'out\base.apk') -Destination (Join-Path $stage 'out\withdex.apk') -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::Open((Join-Path $stage 'out\withdex.apk'), 'Update')
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $zip, (Join-Path $stage 'dex\classes.dex'), 'classes.dex',
        [System.IO.Compression.CompressionLevel]::Optimal)
    $zip.Dispose()

    Write-Host '[5/6] zipalign'
    & $zipalign -p -f 4 out\withdex.apk out\aligned.apk
    if ($LASTEXITCODE -ne 0) { throw 'zipalign 失败' }

    Write-Host '[6/6] 签名'
    if (-not $Keystore) {
        $Keystore = Join-Path $root 'build\local.keystore'
        if (-not (Test-Path $Keystore)) {
            Write-Host "  未指定签名，生成本地密钥（仅调试用，git 已忽略）: $Keystore"
            & $keytool -genkeypair -keystore $Keystore -alias $KeyAlias -storepass $KsPass -keypass $KsPass `
                -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=Lingwu, O=Lingwu, C=CN" 2>&1 | Out-Null
        }
    }
    if (-not (Test-Path $Keystore)) { throw "找不到密钥文件：$Keystore" }

    & $apksigner sign --ks $Keystore --ks-pass "pass:$KsPass" --key-pass "pass:$KsPass" `
        --ks-key-alias $KeyAlias --min-sdk-version 24 --out out\signed.apk out\aligned.apk
    if ($LASTEXITCODE -ne 0) { throw 'apksigner 签名失败' }

    $outApk = Join-Path $dist '凌雾跨设备传输.apk'
    Copy-Item -LiteralPath (Join-Path $stage 'out\signed.apk') -Destination $outApk -Force

    Write-Host ''
    Write-Host '----- 校验 -----'
    & $apksigner verify --print-certs $outApk
    $f = Get-Item -LiteralPath $outApk
    Write-Host ("产物: {0}  {1:N1} KB" -f $f.Name, ($f.Length / 1KB))
}
finally {
    Pop-Location
}
