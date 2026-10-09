using System.Drawing.Drawing2D;

namespace NetworkMonitor;

internal sealed class WelcomeForm : Form
{
    public WelcomeForm()
    {
        Text = "关于网络流量监控";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(480, 286);
        BackColor = Theme.Surface;
        ForeColor = Theme.Text;
        Font = new Font(Theme.FontFamily, 10);
        DoubleBuffered = true;
        SizeChanged += (_, _) => UpdateRegion();
        UpdateRegion();

        var title = Theme.Label("欢迎使用网络流量监控", 18, Theme.Text, true);
        title.Dock = DockStyle.None;
        title.SetBounds(28, 25, 400, 40);
        var close = new Button { Text = "×", FlatStyle = FlatStyle.Flat, ForeColor = Theme.Muted, BackColor = Theme.Surface, Font = new Font(Theme.FontFamily, 16), Bounds = new Rectangle(434, 17, 30, 30), Cursor = Cursors.Hand };
        close.FlatAppearance.BorderSize = 0;
        close.Click += (_, _) => Close();

        var subtitle = Theme.Label("感谢使用，项目与更新信息见 GitHub：", 10, Theme.Muted);
        subtitle.Dock = DockStyle.None;
        subtitle.SetBounds(30, 82, 420, 26);
        var github = new LinkLabel { Text = AppInfo.GitHubUrl, LinkColor = Theme.Blue, ActiveLinkColor = Theme.Gold, VisitedLinkColor = Theme.Blue, BackColor = Theme.Surface, Font = new Font(Theme.FontFamily, 10), Bounds = new Rectangle(30, 112, 420, 28), AutoEllipsis = true };
        github.LinkClicked += (_, _) =>
        {
            try { AppInfo.OpenGitHub(); }
            catch (Exception ex) { MessageBox.Show(this, "无法打开 GitHub 链接：" + ex.Message, "网络流量监控"); }
        };

        var author = Theme.Label("作者：" + AppInfo.Author, 10, Theme.Text);
        author.Dock = DockStyle.None;
        author.SetBounds(30, 164, 420, 26);
        var bilibili = Theme.Label("B站：" + AppInfo.BilibiliName, 10, Theme.Text);
        bilibili.Dock = DockStyle.None;
        bilibili.SetBounds(30, 192, 420, 26);
        var start = new RoundedButton("开始使用") { Bounds = new Rectangle(333, 232, 118, 38), AutoSize = false, MinimumSize = Size.Empty, Margin = Padding.Empty };
        start.Click += (_, _) => Close();
        AcceptButton = start;
        Controls.AddRange(new Control[] { title, close, subtitle, github, author, bilibili, start });
        Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = Theme.RoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), 18);
            using var pen = new Pen(Theme.Border, 1.5f);
            e.Graphics.DrawPath(pen, path);
        };
    }

    private void UpdateRegion()
    {
        if (Width < 2 || Height < 2) return;
        using var path = Theme.RoundedPath(new Rectangle(0, 0, Width, Height), 18);
        var previous = Region;
        Region = new Region(path);
        previous?.Dispose();
    }
}
