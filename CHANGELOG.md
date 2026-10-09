# 更新记录

## 1.4.8

- 点击主窗口关闭按钮或收起到托盘时立即保存用量；托盘退出以及 Windows 关机、重启、注销时补采最后一次网卡计数器变化，保存以太网/WLAN 本月累计与以太网剩余额度。
- 主记录、每月用量日志及以太网独立日志刷新到磁盘，下次启动自动恢复。取消关机后保持监控运行，系统关闭浮窗时保留显示偏好。网卡读取失败时保留已有累计。

本次同步同时包含此前在本地完成的功能：

- 以太网和 WLAN 按本机时间在每月 1 日清零累计；未运行时在下次启动检查月份并清零。
- 以太网月限额、本月用量和剩余额度独立保存到安装目录之外的 `ethernet-usage.log`，升级、卸载后保留，并支持记录恢复。
- 主窗口和浮窗新增 WinUI 3 风格半透明主题，主题同步切换并保存；修正顶部按钮的白色矩形背景。
- 浮窗支持右下角拖动缩放，按尺寸调整内容密度，保存尺寸和位置。
- 安装包显示预计安装大小；选择硬盘根目录时自动安装到 `Internet Monitor` 子文件夹。
- 安装完成后询问是否启用开机自启，软件内可随时切换。登录后通过计划任务进入托盘并恢复浮窗。

验证：Release 构建通过；33 项关闭与结束会话验证、83 项界面和原有功能检查、7 项安装包验证通过。结束会话验证仅向测试窗口发送通知，不会实际关闭或重启电脑。

```powershell
dotnet build NetworkMonitor.csproj -c Release
dotnet bin/Release/net8.0-windows/NetworkMonitor.dll --verify-usage artifacts/verify-usage
dotnet bin/Release/net8.0-windows/NetworkMonitor.dll --verify-shutdown artifacts/verify-shutdown
dotnet bin/Release/net8.0-windows/NetworkMonitor.dll --verify-ui artifacts/verify-ui
```
