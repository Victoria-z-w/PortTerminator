# Port Terminator - 端口终结工具

Windows 端口管理工具，用于查看 TCP/UDP 端口占用、定位进程、安全结束进程。

## 技术栈

- .NET 8
- WPF + MVVM (CommunityToolkit.Mvvm)
- HandyControl
- SQLite
- Windows Native API (GetExtendedTcpTable / GetExtendedUdpTable)

## 项目结构

```
PortTerminator.sln
├── src/PortTerminator.Core          # 模型、接口、核心业务逻辑
├── src/PortTerminator.Windows       # Windows API、端口扫描、进程查询
├── src/PortTerminator.Infrastructure # SQLite、配置、日志
├── src/PortTerminator.UI            # WPF 界面
└── src/PortTerminator.Elevated      # UAC 提权 Helper
```

## 构建要求

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## 构建与运行

```powershell
cd D:\java\PortTerminator
dotnet restore
dotnet build PortTerminator.sln
dotnet run --project src\PortTerminator.UI\PortTerminator.UI.csproj
```

## 打包安装程序

本项目使用 [Inno Setup](https://jrsoftware.org/isinfo.php) 生成 Windows 安装向导。

### 本地打包

1. 安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
2. 安装 [Inno Setup 6](https://jrsoftware.org/isinfo.php)
3. 运行打包脚本：

```powershell
.\scripts\build-installer.ps1 -Version 1.0.0
```

生成的安装包位于 `dist\PortTerminator-Setup-1.0.0.exe`。

### 发布到 GitHub Release

推送版本标签后，GitHub Actions 会自动构建安装包并上传到 Release：

```powershell
git tag v1.0.0
git push origin v1.0.0
```

Release 页面将包含 `PortTerminator-Setup-1.0.0.exe` 安装向导，用户下载后双击即可安装。

### 发布到 Gitee Release

1. 在 [Gitee 私人令牌](https://gitee.com/profile/personal_access_tokens) 生成令牌（需 `projects` 权限）
2. 本地打包后执行：

```powershell
$env:GITEE_TOKEN = '你的令牌'
.\scripts\publish-gitee-release.ps1 -Version 1.0.0
```

Gitee 仓库：[https://gitee.com/hherr54tge3tg/port-terminator](https://gitee.com/hherr54tge3tg/port-terminator)

> 安装包为自包含发布（Self-contained），用户无需单独安装 .NET 运行时。

## 功能

- 真实 TCP/UDP 端口扫描（Native API）
- 进程信息查询（路径、命令行、签名）
- 增量刷新（Snapshot Diff）
- 风险评级
- 结束进程 / 强制终结 / 释放端口
- 系统关键进程保护
- UAC 提权 Helper（按需）
- 白名单、规则中心
- 操作日志（SQLite）
- 系统托盘

## 数据目录

- 配置: `%AppData%\PortTerminator\config.json`
- 数据库: `%LocalAppData%\PortTerminator\Data\port_terminator.db`

## 验收测试

```powershell
# 测试 1: Python HTTP Server
python -m http.server 8080

# 测试 2: Java
java -jar app.jar --server.port=8080

# 测试 3: Node
npm run dev
```

启动 Port Terminator 后应能检测到对应端口和进程，并支持释放端口。
