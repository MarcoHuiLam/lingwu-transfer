# 复制本文件为「签名配置.ps1」，填上密钥路径（密码不必填，打包时会提示输入）
# 该文件已被 .gitignore 忽略，不会上传 GitHub
$SignKeystore  = 'D:\凌雾签名\lingwu-release.keystore'
$SignKeyAlias  = 'lingwu'

# 密码默认不写在这里（防止明文泄露），打包时弹窗输入。
# 如确实想省事，可以把下面一行取消注释并填上（不推荐）：
# $SignStorePass = '密码'

# —— 可选：构建工具路径（不填会自动探测）——
# $JdkBin     = 'C:\jdk-17\bin'
# $AndroidJar = 'C:\android-sdk\platforms\android-34\android.jar'
# $BuildTools = 'C:\android-sdk\build-tools\34.0.0'