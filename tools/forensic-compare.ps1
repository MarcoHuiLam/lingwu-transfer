# 同源取证比对工具 · Forensic Compare
#
# 用途：拿到一个可疑 APK，与自己的官方 APK 比对，输出"是否同源"的证据报告。
#       报告可直接作为向平台投诉、向律师说明、向法院举证的材料附件。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File tools/forensic-compare.ps1 `
#     -Suspect "D:\证据\可疑APP.apk" `
#     -Mine    "D:\官方\凌雾跨设备传输.apk" `
#     [-BuildTools "C:\android-sdk\build-tools\34.0.0"] `
#     [-Out "D:\证据\同源比对报告.txt"]
#
# 说明：
#   - 不修改任何文件，只读
#   - 取证前建议先对"下载、安装、运行、关于页"全程录屏，再运行本脚本
param(
    [Parameter(Mandatory = $true)][string]$Suspect,
    [string]$Mine = "",
    [string]$BuildTools = "",
    [string]$Out = ""
)
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$lines = New-Object System.Collections.ArrayList
function W([string]$s) { [void]$lines.Add($s); Write-Host $s }

W "同源取证比对报告"
W ("生成时间: " + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
W ("比对工具: forensic-compare.ps1 / 凌雾工坊")
W ("=" * 72)

function Get-ApkInfo([string]$path, [string]$label) {
    W ""
    W "【$label】$path"
    if (-not (Test-Path -LiteralPath $path)) { W "  ✗ 文件不存在"; return $null }
    $fi = Get-Item -LiteralPath $path
    $sha = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    W ("  大小      : {0:N0} 字节 ({1:N1} KB)" -f $fi.Length, ($fi.Length / 1KB))
    W ("  文件 SHA256: $sha")
    W ("  修改时间  : " + $fi.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))

    if ($BuildTools) {
        $aapt2 = Join-Path $BuildTools 'aapt2.exe'
        if (Test-Path $aapt2) {
            W "  --- aapt2 badging ---"
            & $aapt2 dump badging $path 2>&1 | Select-String -Pattern '^package:|^application-label|^sdkVersion|^targetSdkVersion|^launchable-activity' |
                ForEach-Object { W ("    " + $_.Line.Trim()) }
        }
        $apksigner = Join-Path $BuildTools 'apksigner.bat'
        if (Test-Path $apksigner) {
            W "  --- 签名证书指纹（关键：与官方是否同一把证书）---"
            $javaHome = $env:JAVA_HOME
            if (-not $javaHome -or -not (Test-Path (Join-Path $javaHome 'bin'))) {
                $javac = Get-Command javac -ErrorAction SilentlyContinue
                if ($javac) { $env:JAVA_HOME = Split-Path (Split-Path $javac.Source -Parent) -Parent }
            }
            if (-not $env:JAVA_HOME) {
                W "    ⚠ 未找到 JAVA_HOME，跳过签名指纹（先设置 JAVA_HOME 指向 JDK 目录）"
            } else {
                $sig = & $apksigner verify --print-certs $path 2>&1
                $sig | Select-String -Pattern 'SHA-256 digest|SHA-1 digest|certificate DN' |
                    ForEach-Object { W ("    " + $_.Line.Trim()) }
            }
        }
    }

    # 解包清单
    $zip = $null
    $entries = @{}
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
        W ("  条目数    : " + $zip.Entries.Count)
        foreach ($e in $zip.Entries) {
            if ($e.Length -le 0) { continue }
            $s = $e.Open()
            $sha256 = [System.Security.Cryptography.SHA256]::Create()
            $hash = $sha256.ComputeHash($s)
            $s.Close()
            $hex = ($hash | ForEach-Object { $_.ToString('x2') }) -join ''
            $entries[$e.FullName] = @{ Hash = $hex; Size = $e.Length }
        }
        $dex = $entries.Keys | Where-Object { $_ -match '^classes\d*\.dex$' }
        foreach ($d in $dex) { W ("  {0,-16} {1,10:N0} B  SHA256={2}" -f $d, $entries[$d].Size, $entries[$d].Hash.Substring(0,16)) }

        # 特征字符串扫描：同源最强证据（协议魔数、中文专有文案、包名）
        W "  --- 特征字符串（出现即为同源强证据）---"
        $markers = @(
            'LWT1DISC', 'LWT1HERE', 'LWT1',
            'com/lingwu/transfer', 'com.lingwu.transfer',
            '凌雾跨设备传输', '凌雾工坊', 'MarcoHuiLam',
            '配对码不对', '电脑端已暂停接收', '接收模式已就绪',
            '下载/凌雾传输', '凌雾传输'
        )
        foreach ($name in $dex) {
            $entry = $zip.GetEntry($name)
            $ms = New-Object System.IO.MemoryStream
            $st = $entry.Open(); $st.CopyTo($ms); $st.Close()
            $bytes = $ms.ToArray()
            # 直接按 UTF-8 / ASCII 在字节流里搜（dex 的字符串池本身就是 UTF-8 变体）
            $utf8 = [Text.Encoding]::UTF8.GetString($bytes)
            $hits = @()
            foreach ($m in $markers) { if ($utf8.Contains($m)) { $hits += $m } }
            W ("    " + $name + " 命中 " + $hits.Count + " 个: " + ($(if ($hits.Count) { $hits -join ', ' } else { '无' })))
        }
    } catch {
        W "  ✗ 解包失败: $($_.Exception.Message)"
    } finally {
        if ($zip) { $zip.Dispose() }
    }
    return $entries
}

$mineEntries = $null
if ($Mine) { $mineEntries = Get-ApkInfo $Mine '官方版本（我方）' }
$susEntries = Get-ApkInfo $Suspect '可疑版本'

if ($mineEntries -and $susEntries) {
    W ""
    W "【逐条目哈希比对】"
    $common = $susEntries.Keys | Where-Object { $mineEntries.ContainsKey($_) }
    $same = $common | Where-Object { $mineEntries[$_].Hash -eq $susEntries[$_].Hash }
    W ("  共同条目: {0}    完全一致: {1}" -f $common.Count, $same.Count)
    if ($same.Count -gt 0) {
        W "  --- 完全一致的条目（未改动的原始文件）---"
        $same | Sort-Object | ForEach-Object { W ("    ✓ " + $_) }
    }
    $onlyMine = $mineEntries.Keys | Where-Object { -not $susEntries.ContainsKey($_) }
    if ($onlyMine) { W ("  仅官方有（被删除）: " + (($onlyMine | Select-Object -First 10) -join ', ')) }
}

W ""
W ("=" * 72)
W "结论建议"
W "  1) 若「特征字符串」命中 LWT1DISC/LWT1HERE 或包名 com.lingwu.transfer → 同源证据极强"
W "  2) 若多个条目 SHA256 完全一致（尤其 classes.dex、resources.arsc、mipmap）→ 直接复制进一步坐实"
W "  3) 若签名证书指纹与你一致 → 对方直接使用了你的安装包（若你用调试密钥，此项证明力较弱，需靠上面两条）"
W "  4) 下一步：录屏 + 公证 + 平台侵权投诉 + 律师函（详见 维权指南.md）"

if ($Out) {
    [IO.File]::WriteAllText($Out, ($lines -join "`r`n"), (New-Object System.Text.UTF8Encoding($true)))
    Write-Host ""
    Write-Host "报告已保存: $Out"
}
