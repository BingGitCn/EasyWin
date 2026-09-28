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
- **Wi-Fi 连接二维码**:已存网络一键生成扫码卡,手机相机扫码免密加入(WIFI: URI 标准,本地渲染不出应用)

### 🔔 托盘常驻
- 关闭窗口即最小化到托盘,后台常驻(自动化规则持续生效)
- 托盘右键菜单**一键切换 IP 方案 / 连接 Wi-Fi**,双击打开主窗口;二次启动自动唤起已有窗口

### 🛠 网络工具
- 系统代理(HTTP)查看 / 开关 / 修改,写入后立即广播生效(无需重启浏览器)
- hosts 文件内嵌编辑,保存前自动备份
- **端口占用查询**:输入端口号列出占用进程(TCP/UDP),可一键结束
- 一键刷新 DNS 缓存 / 重置 Winsock

### ⚡ 系统设置(40 项,全部可逆)
五大分组,参考「Windows11轻松设置」与 [microsoft/WindowsDeveloperConfig](https://github.com/microsoft/WindowsDeveloperConfig) 补齐系统工具箱维度;关闭安全防护类调整带「高危」角标与二次确认。

| 分组 | 调整项 |
|---|---|
| Windows 更新 | 禁用自动更新(组策略 `NoAutoUpdate` + 服务 `wuauserv` + `WaaSMedicSvc` 三重保险) |
| 资源管理器 | 去快捷方式箭头、隐藏「快捷方式」字样、显示文件扩展名、显示隐藏文件、隐藏「3D 对象」、Win11 恢复经典右键菜单、右键「管理员取得所有权」、右键「在此处打开 CMD」、默认打开「此电脑」、标题栏显示完整路径、精简快速访问与推广提示 |
| 任务栏与开始菜单 | 图标靠左(Win11)、时钟显示秒数、隐藏搜索框、隐藏任务视图、隐藏小组件(Win11)、隐藏聊天/Copilot(Win11)、任务栏右键「结束任务」(23H2+)、关闭开始菜单推荐与账户通知、关闭搜索亮点 |
| 隐私 | 关闭遥测(DiagTrack 等)、禁用广告 ID、禁用位置跟踪、禁用活动历史、禁用 Cortana、禁用错误报告、搜索仅本地结果、勿扰模式(关闭全部通知) |
| 系统与安全 | 启用远程桌面(自动放行防火墙 RDP 规则)、视觉效果最佳性能、系统深色模式一键切换、禁用休眠与快速启动(powercfg)、关闭自动播放、防火墙、禁用 SmartScreen、禁用内存完整性(HVCI)、禁用系统还原、开发者模式、启用长路径、Win11 Sudo(24H2+) |

高危项(防火墙/SmartScreen/内存完整性/系统还原)应用前需二次确认;任务栏/资源管理器修改会广播 `WM_SETTINGCHANGE`,多数即时生效,少数需一键重启资源管理器。

### 🧹 清理
理念参考 [builtbybel/FluentCleaner](https://github.com/builtbybel/FluentCleaner)(MIT):只清理明确列出的目标,不做注册表「深度清理」,勾选了才动手;被占用/受保护的文件自动跳过。进入页面自动扫描各目标大小,一键清理勾选项并汇总释放空间。

| 目标 | 说明 |
|---|---|
| 系统 / 用户临时文件夹 | 仅清理 24 小时前的旧文件,避免误删正在使用的文件 |
| Windows 更新缓存 / 传递优化缓存 | SoftwareDistribution\Download 与 P2P 分发缓存 |
| 缩略图与图标缓存 | 被资源管理器占用的会跳过,可配合「重启资源管理器」重试 |
| 错误报告队列 / 应用崩溃转储 | WER ReportQueue/Archive、CrashDumps、Minidump |
| 回收站 | shell32 查询大小并清空(所有盘符,不可恢复) |
| Edge / Chrome / Firefox 缓存 | 各 Profile 的网页缓存 / Code Cache / GPU 缓存,清理前请关浏览器 |
| 最近使用记录 | 「最近使用的文件」列表与「运行」对话框历史 |

### 🩺 网络诊断
七大工具一页装齐(功能清单参考 [BornToBeRoot/NETworkManager](https://github.com/BornToBeRoot/NETworkManager),实现自写未用其代码):
- **Ping 监视**:持续 ping + 实时延迟曲线,统计丢失率/平均/最小/最大
- **路由跟踪**:TTL 递增逐跳探测,超时跳标注
- **DNS 查询**:A/AAAA/CNAME/MX/TXT/NS 等记录类型(基于 DnsClient,MIT)
- **端口扫描**:并发扫描远程主机端口,支持 `22,80,1000-2000` 混写,最多 4096 个
- **HTTP 响应头**:查看 Web 服务器响应头与状态码
- **本机网络状态**:ARP 表 / 活动 TCP 连接 / 监听端口,自动关联进程名
- **子网计算器**:IP+掩码(点分或 /CIDR)→ 网段/广播/可用范围/主机数/IP 分类

仪表盘同步显示公网出口 IP(ipify)。

### 📊 使用统计
软件使用时长排行(理念参考 [Planshit/Tai](https://github.com/Planshit/Tai)(MIT),轻量版):开关默认关闭,开启后托盘常驻期间每 15 秒采样前台窗口进程,无输入 5 分钟视为离开不计入;数据仅存本地 `usage.json`,保留 30 天,无任何网络上传。页面支持今日/近 7 天/近 30 天切换,Top 10 排行 + 占比条 + 总活跃时长。

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
dotnet build src/EasyWin/EasyWin.csproj

# 运行
src/EasyWin/bin/Debug/net8.0-windows/EasyWin.exe

# 直接跳转某页(也可用于自动化演示)
EasyWin.exe --page=profiles   # network / profiles / nettools / diag / tweaks / cleaner / usage / launcher / about

# 发布(依赖框架单文件 → dist\EasyWin.exe)
tools\publish.bat
```

## 项目结构
```
src/EasyWin/
├── Models/      NetworkAdapterInfo、IpProfile、AutomationRule、LaunchItem 等
├── Services/    NetworkService(WMI+netsh)、TweakService(注册表/服务/电源)、CleanerService(扫描/清理引擎)
│                DiagnosticsService(Ping/路由跟踪/DNS 查询/端口扫描/子网计算)
│                TrayService(托盘)、AutomationService(场景自动化)、PortLookup(端口占用)
│                ProfileStore、SysProxyService、HostsService、SystemInfoService
├── ViewModels/  MVVM(CommunityToolkit.Mvvm),每页一个 VM
├── Views/       FluentWindow 主窗体 + 12 个页面 + 方案/规则编辑窗口
└── Controls/    TweakCard 卡片控件
```

## 安全说明
- 所有系统调整均有确认提示,且可在原处一键恢复
- 修改 IP / 切换方案前会显示完整参数并要求确认(切换时网络会短暂中断)
- 禁用更新后建议定期恢复以获取安全补丁;关闭防火墙仅建议在受信任内网临时使用
