# 手工构建 Android APK（不用 Gradle）：javac -> d8 -> aapt2 -> 加 dex -> zipalign -> apksigner
#
# 依赖：JDK 17 + Android SDK（build-tools 的 aapt2/d8/zipalign/apksigner + platform 的 android.jar）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File build/build-apk.ps1 `
#     -JdkBin     "C:\jdk-17\bin" `
#     -AndroidJar "C:\android-sdk\platforms\android-34\android.jar" `
#     -BuildTools "C:\android-sdk\build-tools\34.0.0" `
#     [-Keystore "path\to\release.keystore" -KsPass "密码" -KeyAlias "别名"]
#
# 不传 -Keystore 时用 build/local.keystore（首次自动生成，仅供本地调试，已被 .gitignore 忽略）。
# aapt2 不认非 ASCII 路径，脚本会自动把工程拷到 %TEMP%\lingwu-apk-build 下构建。
param(
    [string]$JdkBin = "",
    [string]$AndroidJar = "",
    [string]$BuildTools = "",
    [string]$Keystore = "",
    [string]$KsPass = "android",
    [string]$KeyAlias = "lingwu"
)
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$srcA = Join-Path $root 'src\android'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null

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
if (-not $AndroidJar -or -not (Test-Path $AndroidJar)) { throw "请用 -AndroidJar 指定 android.jar" }

$env:JAVA_HOME = Split-Path $JdkBin -Parent
$env:PATH = $JdkBin + ';' + $env:PATH

$stage = Join-Path $env:TEMP 'lingwu-apk-build'
Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stage, "$stage\obj", "$stage\dex", "$stage\out", "$stage\tmp" -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $srcA 'res') -Destination $stage -Recurse -Force
Copy-Item -LiteralPath (Join-Path $srcA 'AndroidManifest.xml') -Destination $stage -Force
Copy-Item -LiteralPath $AndroidJar -Destination "$stage\android.jar" -Force
$env:TEMP = "$stage\tmp"
$env:TMP  = "$stage\tmp"
$oldTemp = $env:TEMP
$oldTmp  = $env:TMP
$jar = "$stage\android.jar"

Write-Host "构建目录: $stage"
Push-Location $stage
try {
    Write-Host "[1/6] javac"
    $javaFiles = @(Get-ChildItem -Path (Join-Path $srcA 'java') -Filter *.java -Recurse | ForEach-Object { $_.FullName })
    & $javacExe -encoding UTF-8 -source 8 -target 8 -nowarn -bootclasspath $jar -d "$stage\obj" $javaFiles
    if ($LASTEXITCODE -ne 0) { throw 'javac 失败' }

    Write-Host "[2/6] d8"
    $classFiles = @(Get-ChildItem -Path "$stage\obj" -Filter *.class -Recurse | ForEach-Object { $_.FullName })
    & $d8Bat --release --lib $jar --min-api 24 --output "$stage\dex" $classFiles
    if (-not (Test-Path "$stage\dex\classes.dex")) { throw 'd8 未生成 classes.dex' }

    Write-Host "[3/6] aapt2"
    & $aapt2 compile --dir res -o out\res.zip
    if ($LASTEXITCODE -ne 0) { throw 'aapt2 compile 失败' }
    & $aapt2 link -o out\base.apk -I android.jar --manifest AndroidManifest.xml `
        --min-sdk-version 24 --target-sdk-version 34 --auto-add-overlay out\res.zip
    if ($LASTEXITCODE -ne 0) { throw 'aapt2 link 失败' }

    Write-Host "[4/6] 打包 dex"
    Copy-Item -LiteralPath "$stage\out\base.apk" -Destination "$stage\out\withdex.apk" -Force
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::Open("$stage\out\withdex.apk", 'Update')
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $zip, "$stage\dex\classes.dex", 'classes.dex', [System.IO.Compression.CompressionLevel]::Optimal)
    $zip.Dispose()

    Write-Host "[5/6] zipalign"
    & $zipalign -p -f 4 "$stage\out\withdex.apk" "$stage\out\aligned.apk"
    if ($LASTEXITCODE -ne 0) { throw 'zipalign 失败' }

    Write-Host "[6/6] 签名"
    if (-not $Keystore) {
        $Keystore = Join-Path $root 'build\local.keystore'
        if (-not (Test-Path $Keystore)) {
            Write-Host "  未指定签名，生成本地密钥（仅调试用，git 已忽略）: $Keystore"
            & $keytool -genkeypair -keystore $Keystore -alias $KeyAlias -storepass $KsPass -keypass $KsPass `
                -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=Lingwu, O=Lingwu, C=CN"
        }
    }
    $outApk = Join-Path $dist '凌雾跨设备传输.apk'
    Remove-Item -LiteralPath $outApk -Force -ErrorAction SilentlyContinue
    & $apksigner sign --ks $Keystore --ks-pass "pass:$KsPass" --key-pass "pass:$KsPass" `
        --ks-key-alias $KeyAlias --min-sdk-version 24 --out $outApk "$stage\out\aligned.apk"
    if ($LASTEXITCODE -ne 0) { throw 'apksigner 签名失败' }

    Write-Host "`n===== 校验 ====="
    & $apksigner verify $outApk
    $f = Get-Item -LiteralPath $outApk
    Write-Host ("`n产物: {0}  {1:N1} KB" -f $f.Name, ($f.Length / 1KB))
}
finally {
    Pop-Location
}
