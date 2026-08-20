# RockyBackup - Rocky Linux 10 SSH/SFTP 目录备份

Windows 图形程序，将 Rocky Linux 的远程目录（默认 `/backup`）同步到 Windows 本地目录（默认 `D:\backup`）。

## 功能

- 配置 SSH IP/主机、端口、账户、密码
- 配置 Linux 源目录和 Windows 目标目录
- SFTP 递归下载，保持目录层级一致
- 增量备份：文件大小和修改时间相同则跳过
- 镜像模式：删除目标目录中源端已经不存在的文件/空目录
- 每天指定时间自动备份
- 关闭窗口后缩到系统托盘，定时任务继续工作
- 可设置 Windows 登录后自动启动到托盘
- SSH 密码用 Windows DPAPI（CurrentUser）加密后保存
- 日志保存在 `%LOCALAPPDATA%\RockyBackup\logs`
- 支持命令行 `RockyBackup.exe --run-backup`，便于后续配合 Windows 任务计划程序

## 开发环境

- Windows 10/11 或 Windows Server
- Visual Studio 2022
- .NET 8 SDK
- NuGet: SSH.NET 2026.0.0

## 编译

1. 用 Visual Studio 2022 打开 `RockyBackupWinForms.csproj`。
2. 等待 NuGet 自动还原。
3. 选择 `Release` 编译。
4. 推荐发布为单文件：项目右键 -> 发布 -> 文件夹 -> win-x64 -> 自包含。

也可以命令行：

```powershell
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

发布结果通常位于：

```text
bin\Release\net8.0-windows\win-x64\publish\
```

## 使用

首次打开后填写：

```text
SSH IP：192.168.148.62
端口：22
账户：root
密码：******
源主机目录：/backup
目标主机目录：D:\backup
每日备份时间：02:00
```

先点“测试连接”，成功后点“保存配置”。如果需要目标端与源端严格一致，请勾选“镜像模式”。

> 注意：镜像模式会删除 `D:\backup` 中远程 `/backup` 已不存在的文件，请不要把其他资料混放在目标目录。

定时备份依赖程序在后台运行，因此建议勾选“Windows 登录后自动启动到托盘”。点击窗口右上角 X 不会真正退出，而是进入托盘；要彻底退出请右键托盘图标选择“退出”。

## Rocky Linux 端要求

SSH/SFTP 服务必须可用：

```bash
systemctl status sshd
ss -lntp | grep :22
```

SSH 用户必须至少拥有 `/backup` 的读取和目录遍历权限：

```bash
ls -ld /backup
find /backup -maxdepth 2 -ls | head
```

## 同步规则

远端：

```text
/backup/a.sql
/backup/mysql/db.sql
/backup/oracle/full.dmp
```

本地：

```text
D:\backup\a.sql
D:\backup\mysql\db.sql
D:\backup\oracle\full.dmp
```

不会额外生成 `D:\backup\backup\...`。

## 重要说明

Linux 允许部分 Windows 不允许的文件名字符。如果 `/backup` 下存在文件名包含 `: * ? " < > |` 等字符，程序会报错而不会擅自改名，以避免破坏“目录一致”的语义。

当前版本使用密码认证。生产环境更推荐后续增加 SSH 私钥认证和主机指纹校验。

## Windows x64 自包含发布

本项目已包含：

- `Properties/PublishProfiles/win-x64.pubxml`：Visual Studio 发布配置
- `build-win-x64.ps1`：Windows 本地一键发布脚本
- `.github/workflows/build-win-x64.yml`：GitHub Actions 自动构建

### 本地发布

安装 .NET 8 SDK 后，在 PowerShell 中运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\build-win-x64.ps1
```

成功后生成：

```text
publish\RockyBackup.exe
```

这是 `win-x64` 自包含单文件版本，不要求目标 Windows 另装 .NET 运行时。

### GitHub Actions

把项目文件放在仓库根目录。推送到 `main` 或 `master` 后会自动构建，也可以在 Actions 页面手动运行 `Build RockyBackup Windows x64`。

构建完成后下载 Artifact：`RockyBackup-win-x64`。
