# 项目约定

- 用户要求：每次更新软件安装包，都要同步到对应 GitHub 项目的 Releases。
- 对应仓库：`XiaozhuangXzsg/WLAN-LAN-Monitor-`。
- 每个新版本使用 `v<版本号>` 标签，发布说明包含本次功能更新；Release 必须附带该版本验证通过的 `NetworkMonitor-Setup-<版本号>.exe`。
- 源码与安装包同步到 GitHub 后，使用 `installer/Publish-GitHubRelease.ps1` 发布。确认 Release 已公开且附件大小、SHA-256 与本地安装包一致，再交付 Release 和直接下载链接。
- 已发布的同版本附件内容不同时，先升级版本号；保留历史 Release。
