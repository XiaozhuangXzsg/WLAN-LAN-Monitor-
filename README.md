# 网络流量监控

Windows 桌面程序，在同一窗口并排显示以太网和 WLAN 网卡的实时上传、下载曲线，并按软件、域名或远端 IP 汇总启动后的 IPv4 流量。主窗口的“打开流量浮窗”按钮可随时打开独立的置顶对比浮窗；再次点击按钮或点击浮窗关闭按钮即可关闭。

## 运行

需要 Windows 10/11 和 .NET 8 SDK。在 PowerShell 中运行：

```powershell
dotnet run --project D:\Codex\Monitor\NetworkMonitor.csproj
```

程序清单会申请管理员权限。管理员权限用于本机原始套接字抓包；不需要安装 Npcap。以太网与 WLAN 在主窗口左右并排显示；同一类型如有多块网卡，可在各自区域的标签页切换。浮窗会汇总每种类型的所有网卡，便于实时比较。

## 安装包

运行 `artifacts\NetworkMonitor-Setup-1.0.0.exe`，程序会安装到当前用户的 `%LOCALAPPDATA%\Programs\NetworkMonitor`，并创建桌面与开始菜单快捷方式。可在 Windows“已安装的应用”中卸载。安装包不含 .NET 运行时，目标电脑需安装 .NET 8 Windows Desktop Runtime。开发者可运行 `installer\Build-Installer.ps1` 重新生成安装包。

## 统计口径

- 曲线使用 Windows 网卡计数器，包含该网卡的总流量，每秒更新一次。
- 软件和域名表使用该网卡捕获的 IPv4 IP 包长度统计。它们与曲线的总数可能不同，例如 IPv6、链路层开销、丢包、分片以及无法捕获的包。
- 软件归属通过 Windows TCP/UDP 连接表匹配。连接很短、进程已结束或连接表未能及时读到时，流量归入“未归属进程”。
- 域名来自当前抓到的普通 DNS 响应；使用加密 DNS、缓存 DNS、直连 IP 时显示远端 IP。一个 IP 由多个域名共享时，域名归属可能不准确。
- HTTPS 的完整网址、路径和页面内容受加密保护，程序无法读取；表中显示的是域名或远端 IP。QUIC 和 IPv6 流量目前只计入网卡曲线，不能归属到软件或域名。
- 统计只在程序运行期间累计，不保存历史记录。
