using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
using NetworkMonitor;

internal static class Setup
{
    internal const string Product = "网络流量监控";
    internal const string Folder = "NetworkMonitor";
    internal const string RegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\NetworkMonitorCodex";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--enable-startup")
        {
            try { StartupTaskService.SetEnabled(true, args[1]); }
            catch { Environment.ExitCode = 1; }
            return;
        }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Length == 2 && args[0] == "--verify-ui")
        {
            Directory.CreateDirectory(args[1]);
            using (var form = new SetupForm())
            {
                form.Show(); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    bitmap.Save(Path.Combine(args[1], "installer.png"));
                }
                var results = new System.Collections.Generic.List<string>();
                results.Add("InstalledBytes=" + PayloadSize());
                results.Add("PackageBytes=" + new FileInfo(Assembly.GetExecutingAssembly().Location).Length);
                Verify(ResolveInstallPath(@"D:\") == @"D:\Internet Monitor", "drive root installs inside Internet Monitor", results);
                Verify(ResolveInstallPath(@"c:\").Equals(@"c:\Internet Monitor", StringComparison.OrdinalIgnoreCase), "drive root matching is case insensitive", results);
                Verify(ResolveInstallPath(" D:\\ ") == @"D:\Internet Monitor", "root selection trims surrounding whitespace", results);
                Verify(ResolveInstallPath(@"D:\Internet Monitor") == @"D:\Internet Monitor", "resolved folder is not nested again", results);
                Verify(ResolveInstallPath(@"D:\Apps\Monitor") == @"D:\Apps\Monitor", "custom nonroot directory is preserved", results);
                string fixture = Path.Combine(Path.GetFullPath(args[1]), "Internet Monitor");
                Directory.CreateDirectory(fixture);
                File.WriteAllText(Path.Combine(fixture, "user-note.txt"), "keep");
                ExtractPayload(fixture);
                Verify(File.Exists(Path.Combine(fixture, "NetworkMonitor.exe")) && File.Exists(Path.Combine(fixture, "Uninstall.exe")), "payload extracts into a directory containing spaces", results);
                Verify(File.ReadAllText(Path.Combine(fixture, "user-note.txt")) == "keep", "installation preserves unrelated files", results);
                File.WriteAllLines(Path.Combine(args[1], "installer-verification.txt"), results);
                form.Close();
            }
            return;
        }
        Application.Run(new SetupForm());
    }
    private static void Verify(bool success, string name, System.Collections.Generic.List<string> results)
    {
        if (!success) throw new Exception(name);
        results.Add("PASS " + name);
    }
    internal static void ExtractPayload(string target)
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
                    using (Stream input = entry.Open())
                    using (FileStream file = new FileStream(Path.Combine(target, name), FileMode.Create, FileAccess.Write))
                        input.CopyTo(file);
                }
        }
    }

    internal static long PayloadSize()
    {
        using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
        {
            if (resource == null) throw new Exception("安装包缺少程序文件。");
            using (var zip = new ZipArchive(resource, ZipArchiveMode.Read))
            {
                long total = 0;
                foreach (ZipArchiveEntry entry in zip.Entries) total = checked(total + entry.Length);
                return total;
            }
        }
    }
    internal static string FormatSize(long bytes)
    {
        return bytes < 1024 * 1024 ? (bytes / 1024d).ToString("F1") + " KiB" : (bytes / (1024d * 1024d)).ToString("F2") + " MiB";
    }

    internal static string DefaultPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Folder);
    }
    internal static string ResolveInstallPath(string selected)
    {
        string full = Path.GetFullPath(selected.Trim());
        string root = Path.GetPathRoot(full);
        if (full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            return Path.Combine(root, "Internet Monitor");
        return full;
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
    private readonly Button browse = new Button();
    private readonly CheckBox desktopShortcut = new CheckBox();
    private readonly Button install = new Button();
    private readonly Label runtime = new Label();
    private readonly long installedBytes = Setup.PayloadSize();

    internal SetupForm()
    {
        Text = "安装 · " + Setup.Product;
        ClientSize = new Size(560, 360);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        var heading = new Label { Text = "安装网络流量监控", Font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold), Left = 22, Top = 20, Width = 500, Height = 34 };
        var description = new Label { Text = "实时比较以太网和 WLAN，并查看软件与域名流量。", Left = 24, Top = 63, Width = 500, Height = 25 };
        var pathLabel = new Label { Text = "选择安装文件夹", Left = 24, Top = 101, Width = 180, Height = 22 };
        path.SetBounds(24, 128, 405, 28);
        path.Text = Setup.DefaultPath();
        path.ReadOnly = true;
        browse.SetBounds(438, 126, 98, 31);
        browse.Text = "浏览…";
        browse.Click += (s, e) => BrowseFolder();
        desktopShortcut.SetBounds(24, 169, 300, 26);
        desktopShortcut.Text = "创建桌面快捷方式";
        desktopShortcut.Checked = true;
        var size = new Label { Text = "预计安装大小：" + Setup.FormatSize(installedBytes) + "    ·    安装包：" + Setup.FormatSize(new FileInfo(Assembly.GetExecutingAssembly().Location).Length), Left = 24, Top = 205, Width = 510, Height = 25 };
        var data = new Label { Text = "用量与额度日志独立保存，升级时保留；上述大小不含运行时与日志。", Left = 24, Top = 235, Width = 510, Height = 25 };
        runtime.SetBounds(24, 265, 510, 32);
        runtime.Text = "需要 .NET 8 Windows Desktop Runtime；程序启动时会申请管理员权限。";
        install.SetBounds(407, 313, 129, 32);
        install.Text = "安装";
        install.Click += (s, e) => Install();
        Controls.AddRange(new Control[] { heading, description, pathLabel, path, browse, desktopShortcut, size, data, runtime, install });
    }

    private void BrowseFolder()
    {
        using (var dialog = new FolderBrowserDialog())
        {
            dialog.Description = "选择网络流量监控的安装文件夹";
            dialog.SelectedPath = Directory.Exists(path.Text) ? path.Text : Setup.DefaultPath();
            dialog.ShowNewFolderButton = true;
            if (dialog.ShowDialog(this) == DialogResult.OK) path.Text = Setup.ResolveInstallPath(dialog.SelectedPath);
        }
    }

    private void Install()
    {
        string target;
        try { target = Setup.ResolveInstallPath(path.Text); path.Text = target; }
        catch { MessageBox.Show("请选择有效的安装位置。", "安装失败"); return; }
        install.Enabled = false;
        try
        {
            Setup.ExtractPayload(target);

            string app = Path.Combine(target, "NetworkMonitor.exe");
            string start = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Setup.Product + ".lnk");
            string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Setup.Product + ".lnk");
            Setup.Shortcut(start, app, target);
            if (desktopShortcut.Checked) Setup.Shortcut(desktop, app, target);
            else if (File.Exists(desktop)) File.Delete(desktop);
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Setup.RegistryKey))
            {
                key.SetValue("DisplayName", Setup.Product);
                key.SetValue("DisplayVersion", "1.4.8");
                key.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, (installedBytes + 1023) / 1024), RegistryValueKind.DWord);
                key.SetValue("Publisher", "NetworkMonitor");
                key.SetValue("InstallLocation", target);
                key.SetValue("DisplayIcon", app);
                key.SetValue("UninstallString", "\"" + Path.Combine(target, "Uninstall.exe") + "\"");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            string shortcuts = desktopShortcut.Checked ? "桌面和开始菜单已创建快捷方式。" : "已创建开始菜单快捷方式。";
            var startup = MessageBox.Show(this, "安装完成。" + shortcuts + "\n安装位置：" + target + "\n软件需要 .NET 8 Windows Desktop Runtime。\n\n是否开启开机自启？\n开启后，当前用户登录 Windows 时会自动启动并进入托盘。\n选择“否”跳过启用，已有的自启设置保持不变。", Setup.Product, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (startup == DialogResult.Yes)
            {
                try
                {
                    using (var helper = Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--enable-startup \"" + app + "\"") { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }))
                    {
                        if (helper == null) throw new Exception("无法启动自启配置程序。");
                        helper.WaitForExit();
                        if (helper.ExitCode != 0) throw new Exception("Windows 未能创建开机自启任务。");
                    }
                    MessageBox.Show(this, "已开启开机自启。当前用户下次登录 Windows 时，软件会自动进入托盘。\n\n可在软件中取消“跟随系统启动”关闭此功能。", Setup.Product, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "软件已安装，但开机自启未开启：" + ex.Message + "\n\n可稍后在软件中勾选“跟随系统启动”重试。", Setup.Product, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("安装失败：" + ex.Message, Setup.Product, MessageBoxButtons.OK, MessageBoxIcon.Error);
            install.Enabled = true;
        }
    }
}
