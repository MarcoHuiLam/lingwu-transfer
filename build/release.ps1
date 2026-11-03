# 一键签名打包：读取同目录的「签名配置.ps1」，调用 build-apk.ps1
# 用法： powershell -ExecutionPolicy Bypass -File build/release.ps1
$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$cfg = Join-Path $here '签名配置.ps1'
if (-not (Test-Path $cfg)) {
    Write-Host '缺少 build\签名配置.ps1'
    Write-Host '请把 签名配置.example.ps1 复制成 签名配置.ps1，并填入密钥路径/密码/别名'
    exit 1
}
. $cfg

$jdk = if ($env:JAVA_HOME) { Join-Path $env:JAVA_HOME 'bin' } else { '' }
$sdk = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } elseif ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { '' }
if (-not $sdk) { Write-Host '请先设置 ANDROID_HOME'; exit 1 }
$platform = (Get-ChildItem -Path (Join-Path $sdk 'platforms') -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1)
$buildTools = (Get-ChildItem -Path (Join-Path $sdk 'build-tools') -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Select-Object -First 1)
if (-not $platform -or -not $buildTools) { Write-Host '找不到 platforms 或 build-tools'; exit 1 }

& (Join-Path $here 'build-apk.ps1') `
    -JdkBin $jdk `
    -AndroidJar (Join-Path $platform.FullName 'android.jar') `
    -BuildTools $buildTools.FullName `
    -Keystore $SignKeystore -KsPass $SignStorePass -KeyAlias $SignKeyAlias
