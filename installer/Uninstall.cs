using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using NetworkMonitor;

internal static class Uninstall
{
    private const string Product = "网络流量监控";
    private const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NetworkMonitorCodex";
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileEx(string existing, string replacement, int flags);

    [STAThread]
    private static void Main(string[] args)
    {
        string target;
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKey))
            target = key == null ? null : key.GetValue("InstallLocation") as string;
        if (string.IsNullOrEmpty(target))
        {
            MessageBox.Show("找不到安装记录。", Product);
            return;
        }
        if (args.Length == 0)
        {
            if (MessageBox.Show("确定卸载网络流量监控？", Product, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string copy = Path.Combine(Path.GetTempPath(), "NetworkMonitor-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(Application.ExecutablePath, copy);
            Process.Start(new ProcessStartInfo(copy, "/run") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden });
            return;
        }
        try
        {
            target = Path.GetFullPath(target);
            if (target.TrimEnd(Path.DirectorySeparatorChar).Equals(Path.GetPathRoot(target).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new Exception("安装记录指向磁盘根目录，无法卸载。");
            StartupTaskService.SetEnabled(false, null);
            string start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Product + ".lnk");
            string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Product + ".lnk");
            if (File.Exists(start)) File.Delete(start);
            if (File.Exists(desktop)) File.Delete(desktop);
            // Custom install directories may also contain the user's unrelated files.
            foreach (string file in new[] { "NetworkMonitor.exe", "NetworkMonitor.dll", "NetworkMonitor.deps.json", "NetworkMonitor.runtimeconfig.json", "network-monitor.png", "Uninstall.exe" })
            {
                string installed = Path.Combine(target, file);
                if (File.Exists(installed)) File.Delete(installed);
            }
            if (Directory.Exists(target) && Directory.GetFileSystemEntries(target).Length == 0) Directory.Delete(target);
            Registry.CurrentUser.DeleteSubKey(RegistryKey, false);
            MessageBox.Show("卸载完成。", Product);
        }
        catch (Exception ex) { MessageBox.Show("卸载失败：" + ex.Message, Product, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { MoveFileEx(Application.ExecutablePath, null, 4); }
    }
}
