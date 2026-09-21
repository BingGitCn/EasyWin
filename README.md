# EasyWin — Windows 一站式管理工具

网卡管理与 IP 一键切换 + Windows 系统设置工具箱。

> 🤖 本软件完全由 AI 编写完成——包括全部代码、界面与文档,人类仅负责提出需求与验收。

![技术栈](https://img.shields.io/badge/C%23%20%2F%20.NET%208-WPF-blue) ![UI](https://img.shields.io/badge/UI-WPF--UI%20Fluent-purple) ![平台](https://img.shields.io/badge/平台-Windows%2010%2F11-lightgrey) ![AI](https://img.shields.io/badge/由_AI_编写-100%25-8A2BE2)

## 功能

### 🏠 仪表盘
系统版本、激活状态、CPU / 内存实时占用(1s 刷新)、磁盘用量、开机时长、网络连通性(ping)。

### 🌐 网络与 IP
- 列出所有网卡(**含已禁用的**,与 ncpa.cpl 一致):状态、IP、网关、DNS、MAC、速率
- 一键切换 **DHCP ↔ 静态 IP**,支持 IP / 掩码 / 网关 / 主备 DNS
- 网卡启用 / 禁用;插拔网线、切 Wi-Fi 自动刷新列表
- 底层走 `netsh`,已处理中文系统 GBK 输出与中文网卡名转义

### 📋 IP 方案(一键切换)
- 把常用 IP 配置保存为方案,双击卡片**一键应用**到目标网卡
- 支持从网卡当前配置导入、新建、编辑、删除
- **自动化规则**:连接到指定 Wi-Fi 时自动应用对应方案(如"连上公司 Wi-Fi 自动切静态 IP")
- **方案导入导出**:JSON 文件备份 / 在多台电脑间迁移
- 连接方案与设置数据统一存放在 `%AppData%\EasyWin`,首次启动自动迁移旧数据

### 📡 Wi-Fi 快速切换
- 主动扫描附近网络(含信号强度 / 加密方式),一键连接;开放网络免密直连
- **查看本机已保存的 Wi-Fi 明文密码**(选择网络 → 查看密码 → 复制)

### 🔔 托盘常驻
- 关闭窗口即最小化到托盘,后台常驻(自动化规则持续生效)
- 托盘右键菜单**一键切换 IP 方案 / 连接 Wi-Fi**,双击打开主窗口;二次启动自动唤起已有窗口

### 🛠 网络工具
- 系统代理(HTTP)查看 / 开关 / 修改,写入后立即广播生效(无需重启浏览器)
- hosts 文件内嵌编辑,保存前自动备份
- **端口占用查询**:输入端口号列出占用进程(TCP/UDP),可一键结束
- 一键刷新 DNS 缓存 / 重置 Winsock

### ⚡ 系统设置(全部可逆)
| 调整项 | 原理 |
|---|---|
| 禁用 Windows 自动更新 | 组策略 `NoAutoUpdate` + 服务 `wuauserv` + `WaaSMedicSvc` 三重保险,防自动恢复 |
| 去除快捷方式小箭头 | `Shell Icons\29` 注册表项(生效需重启资源管理器,可一键重启) |
| 隐藏「快捷方式」字样 | `HKCU\...\Explorer\Link = 0` |
| 启用远程桌面 | `fDenyTSConnections`(家庭版不支持作为被控端) |
| 视觉效果最佳性能 | `VisualFXSetting = 2` |
| 关闭防火墙 | netsh 关闭全部三配置文件(检测走注册表,不受本地化影响) |
| 电源计划切换 | powercfg,高性能方案缺失时自动创建 |

### 🖥 远程桌面
保存 mstsc 连接(服务器/用户/分辨率/管理模式),一键发起远程控制;勾选"记住凭据"后通过 Windows 凭据管理器免密登录(CredWrite 直写,密码不经过命令行),密码经 DPAPI 加密存储;支持连接方案导入导出(跨机器导入需重新输入密码)。

### 🚀 快捷启动
30+ 常用系统工具分类直达:设备管理器、服务、注册表、组策略、磁盘管理、事件查看器、ncpa.cpl、防火墙、ms-settings 各面板、任务管理器等。

## 运行要求
- Windows 10 / 11
- **管理员权限**(清单已内置,启动时 UAC 提权):修改 IP、写 HKLM 注册表都需要
- [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)(发布版为依赖框架单文件;需要免装运行时请改用自包含发布)

## 开发

```bash
# 构建(需要 .NET 8+ SDK,SDK 9/10 也可编译 net8.0 目标)
dotnet build src/EasyWin/EasyWin.sln

# 运行
src/EasyWin/bin/Debug/net8.0-windows/EasyWin.exe

# 直接跳转某页(也可用于自动化演示)
EasyWin.exe --page=profiles   # network / profiles / nettools / tweaks / launcher / about

# 发布(依赖框架单文件 → dist\EasyWin.exe)
tools\publish.bat
```

## 项目结构
```
src/EasyWin/
├── Models/      NetworkAdapterInfo、IpProfile、AutomationRule、LaunchItem 等
├── Services/    NetworkService(WMI+netsh)、TweakService(注册表/服务/电源)
│                TrayService(托盘)、AutomationService(场景自动化)、PortLookup(端口占用)
│                ProfileStore、SysProxyService、HostsService、SystemInfoService
├── ViewModels/  MVVM(CommunityToolkit.Mvvm),每页一个 VM
├── Views/       FluentWindow 主窗体 + 9 个页面 + 方案/规则编辑窗口
└── Controls/    TweakCard 卡片控件
```

## 安全说明
- 所有系统调整均有确认提示,且可在原处一键恢复
- 修改 IP / 切换方案前会显示完整参数并要求确认(切换时网络会短暂中断)
- 禁用更新后建议定期恢复以获取安全补丁;关闭防火墙仅建议在受信任内网临时使用
