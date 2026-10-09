using System.Diagnostics;

namespace NetworkMonitor;

internal static class AppInfo
{
    public const string GitHubUrl = "https://github.com/XiaozhuangXzsg/WLAN-LAN-Monitor-";
    public const string Author = "兰大数学院 zza";
    public const string BilibiliName = "一只小庄呀";

    public static void OpenGitHub()
    {
        Process.Start(new ProcessStartInfo(GitHubUrl) { UseShellExecute = true });
    }
}
