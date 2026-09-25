using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class Setup
{
    internal const string Product = "网络流量监控";
    internal const string Folder = "NetworkMonitor";
    internal const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NetworkMonitorCodex";

    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SetupForm());
    }

    internal static string DefaultPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Folder);
    }

    internal static void Shortcut(string path, string target, string workingDirectory)
    {
        Type type = Type.GetTypeFromProgID("WScript.Shell");
        if (type == null) throw new Exception("无法创建 Windows 快捷方式。");
        dynamic shell = Activator.CreateInstance(type);
        dynamic link = shell.CreateShortcut(path);
        link.TargetPath = target;
        link.WorkingDirectory = workingDirectory;
        link.IconLocation = target + ",0";
        link.Description = Product;
        link.Save();
    }
}

internal sealed class SetupForm : Form
{
    private readonly TextBox path = new TextBox();
    private readonly Button install = new Button();
    private readonly Label runtime = new Label();

    internal SetupForm()
    {
        Text = "安装 · " + Setup.Product;
        ClientSize = new Size(540, 245);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        var heading = new Label { Text = "安装网络流量监控", Font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold), Left = 22, Top = 22, Width = 480, Height = 34 };
        var description = new Label { Text = "实时比较以太网和 WLAN，并查看软件与域名流量。", Left = 24, Top = 67, Width = 480, Height = 25 };
        var pathLabel = new Label { Text = "安装位置", Left = 24, Top = 104, Width = 100, Height = 22 };
        path.SetBounds(24, 129, 490, 25);
        path.Text = Setup.DefaultPath();
        path.ReadOnly = true;
        runtime.SetBounds(24, 164, 490, 24);
        runtime.Text = "需要 .NET 8 Windows Desktop Runtime；程序启动时会申请管理员权限。";
        install.SetBounds(385, 200, 129, 32);
        install.Text = "安装";
        install.Click += (s, e) => Install();
        Controls.AddRange(new Control[] { heading, description, pathLabel, path, runtime, install });
    }

    private void Install()
    {
        string target;
        try { target = Path.GetFullPath(path.Text.Trim()); }
        catch { MessageBox.Show("请选择有效的安装位置。", "安装失败"); return; }
        if (target == Path.GetPathRoot(target)) { MessageBox.Show("不能安装到磁盘根目录。", "安装失败"); return; }
        install.Enabled = false;
        try
        {
            Directory.CreateDirectory(target);
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            {
                if (resource == null) throw new Exception("安装包缺少程序文件。");
                using (var zip = new ZipArchive(resource, ZipArchiveMode.Read))
                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        string name = Path.GetFileName(entry.FullName);
                        if (name.Length == 0 || name != entry.FullName) throw new Exception("安装包内有无效路径。");
                        string output = Path.Combine(target, name);
                        using (Stream input = entry.Open())
                        using (FileStream file = new FileStream(output, FileMode.Create, FileAccess.Write))
                            input.CopyTo(file);
                    }
            }

            string app = Path.Combine(target, "NetworkMonitor.exe");
            string start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Setup.Product + ".lnk");
            string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Setup.Product + ".lnk");
            Setup.Shortcut(start, app, target);
            Setup.Shortcut(desktop, app, target);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Setup.RegistryKey))
            {
                key.SetValue("DisplayName", Setup.Product);
                key.SetValue("DisplayVersion", "1.0.0");
                key.SetValue("Publisher", "NetworkMonitor");
                key.SetValue("InstallLocation", target);
                key.SetValue("DisplayIcon", app);
                key.SetValue("UninstallString", "\"" + Path.Combine(target, "Uninstall.exe") + "\"");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            MessageBox.Show("安装完成。桌面和开始菜单已创建快捷方式。\n\n如果目标电脑尚未安装 .NET 8 Windows Desktop Runtime，请先安装后再启动程序。", Setup.Product, MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("安装失败：" + ex.Message, Setup.Product, MessageBoxButtons.OK, MessageBoxIcon.Error);
            install.Enabled = true;
        }
    }
}
