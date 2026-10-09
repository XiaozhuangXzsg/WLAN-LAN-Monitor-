using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;

namespace NetworkMonitor;

internal sealed class Usage
{
    public long Upload { get; set; }
    public long Download { get; set; }
    public long TodayUpload { get; set; }
    public long TodayDownload { get; set; }
    public long MonthUpload { get; set; }
    public long MonthDownload { get; set; }
    public string Day { get; set; } = "";
    public string MonthKey { get; set; } = "";
    public long Total => Upload + Download;
    public long Today => TodayUpload + TodayDownload;
    public long Month => MonthUpload + MonthDownload;
    public bool RollOver(DateTime date)
    {
        var day = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var month = date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        bool changed = Day != day || MonthKey != month || Upload != MonthUpload || Download != MonthDownload;
        if (Day != day) { Day = day; TodayUpload = TodayDownload = 0; }
        if (MonthKey != month) { MonthKey = month; MonthUpload = MonthDownload = 0; }
        // Older versions stored lifetime totals here. Preserve only the saved current-month usage.
        Upload = MonthUpload; Download = MonthDownload;
        return changed;
    }
    public void Add(long up, long down, DateTime date)
    {
        RollOver(date);
        Upload += up; Download += down; TodayUpload += up; TodayDownload += down;
        MonthUpload += up; MonthDownload += down;
    }
}

internal sealed class AppState
{
    public string ThemeName { get; set; } = "原神暖白";
    public bool GithubWelcomeShown { get; set; }
    public bool ShowFloating { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public int? FloatingX { get; set; }
    public int? FloatingY { get; set; }
    public int? FloatingWidth { get; set; }
    public int? FloatingHeight { get; set; }
    public Dictionary<string, Usage> Adapters { get; set; } = new();
    public HashSet<string> EthernetAdapterIds { get; set; } = new();
    public DateTimeOffset SavedAt { get; set; }
    public long EthernetMonthlyLimitBytes { get; set; }
    public long EthernetMonth => EthernetAdapterIds.Where(Adapters.ContainsKey).Sum(id => Adapters[id].Month);
    public bool RollOver(DateTime date)
    {
        bool changed = false;
        foreach (var usage in Adapters.Values) changed |= usage.RollOver(date);
        return changed;
    }
    public Usage GetUsage(string id, DateTime? date = null)
    {
        if (!Adapters.TryGetValue(id, out var usage)) Adapters[id] = usage = new Usage();
        usage.RollOver(date ?? DateTime.Now);
        return usage;
    }
}

internal sealed class StateStore
{
    private readonly string path;
    public string? Warning { get; private set; }
    public string? LogWarning { get; private set; }
    public string? EthernetLogWarning { get; private set; }
    public string EthernetLogPath => Path.Combine(Path.GetDirectoryName(path)!, "ethernet-usage.log");
    public StateStore(string? path = null) => this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetworkMonitor", "state.json");
    public AppState Load(DateTime? date = null)
    {
        var state = LoadStored();
        var now = date ?? DateTime.Now;
        state.RollOver(now);
        RestoreEthernetLog(state, now);
        return state;
    }
    private AppState LoadStored()
    {
        var directory = Path.GetDirectoryName(path)!;
        if (!File.Exists(path) && !File.Exists(path + ".bak") && (!Directory.Exists(directory) || !Directory.EnumerateFiles(directory, "usage-*.log").Any())) return new();
        try { return Read(path); }
        catch (Exception ex)
        {
            Warning = "累计数据读取失败：" + ex.Message;
            try { var state = Read(path + ".bak"); Warning = "已从备份恢复累计数据。"; return state; }
            catch
            {
                try { var state = ReadLatestLog(); Warning = "已从本地用量日志恢复累计数据。"; return state; }
                catch { return new(); }
            }
        }
    }
    private static AppState Read(string path)
    {
        var state = JsonSerializer.Deserialize<AppState>(File.ReadAllText(path)) ?? throw new InvalidDataException("记录为空");
        if (!ValidState(state))
            throw new InvalidDataException("记录格式无效");
        return state;
    }
    private static bool ValidUsage(Usage? usage) => usage != null && usage.Upload >= 0 && usage.Download >= 0 && usage.MonthUpload >= 0 && usage.MonthDownload >= 0 && usage.TodayUpload >= 0 && usage.TodayDownload >= 0;
    private static bool ValidState(AppState state) => state.Adapters != null && state.EthernetAdapterIds != null && state.EthernetMonthlyLimitBytes >= 0 && state.Adapters.Values.All(ValidUsage);
    public void Save(AppState state, bool writeLog = false, DateTime? date = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var now = date ?? DateTime.Now;
        state.SavedAt = new DateTimeOffset(now);
        if (writeLog)
        {
            // Flush the independent record first so it can recover even a failed state save.
            try { AppendEthernetLog(state, now); EthernetLogWarning = null; }
            catch (Exception ex) { EthernetLogWarning = "以太网独立日志保存失败：" + ex.Message; }
        }
        string temporary = path + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, state, new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(temporary, path, true);
        if (writeLog)
        {
            try { AppendLog(state, now); LogWarning = null; }
            catch (Exception ex) { LogWarning = "日志追加失败：" + ex.Message; }
        }
    }
    public void AppendLog(AppState state, DateTime? date = null)
    {
        var now = date ?? DateTime.Now;
        var logPath = Path.Combine(Path.GetDirectoryName(path)!, "usage-" + now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture) + ".log");
        var line = JsonSerializer.Serialize(new UsageLog { RecordedAt = new DateTimeOffset(now), State = state });
        using var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
        if (stream.Length > 0) writer.WriteLine(); // Isolate a torn tail from the next snapshot.
        writer.WriteLine(line); writer.Flush(); stream.Flush(true);
    }
    private AppState ReadLatestLog()
    {
        var directory = Path.GetDirectoryName(path)!;
        foreach (var log in Directory.GetFiles(directory, "usage-*.log").OrderByDescending(File.GetLastWriteTimeUtc))
        foreach (var line in ReadLinesReverse(log))
        {
            try
            {
                var record = JsonSerializer.Deserialize<UsageLog>(line);
                if (record?.State != null && ValidState(record.State)) return record.State;
            }
            catch { }
        }
        throw new InvalidDataException("没有有效的用量日志记录");
    }
    private sealed class UsageLog { public DateTimeOffset RecordedAt { get; set; } public AppState State { get; set; } = new(); }

    public void AppendEthernetLog(AppState state, DateTime date)
    {
        var month = date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        var adapters = state.EthernetAdapterIds.Where(state.Adapters.ContainsKey)
            .ToDictionary(id => id, id => state.Adapters[id]);
        long up = adapters.Values.Where(x => x.MonthKey == month).Sum(x => x.MonthUpload);
        long down = adapters.Values.Where(x => x.MonthKey == month).Sum(x => x.MonthDownload);
        var record = new EthernetUsageLog
        {
            RecordedAt = new DateTimeOffset(date), MonthKey = month,
            MonthlyLimitBytes = state.EthernetMonthlyLimitBytes,
            MonthUploadBytes = up, MonthDownloadBytes = down, MonthUsedBytes = up + down,
            RemainingBytes = state.EthernetMonthlyLimitBytes > 0 ? Math.Max(0, state.EthernetMonthlyLimitBytes - up - down) : null,
            Adapters = adapters
        };
        Directory.CreateDirectory(Path.GetDirectoryName(EthernetLogPath)!);
        using var stream = new FileStream(EthernetLogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
        // A newline also isolates a partially written tail left by a sudden power loss.
        if (stream.Length > 0) writer.WriteLine();
        writer.WriteLine(JsonSerializer.Serialize(record)); writer.Flush(); stream.Flush(true);
    }
    private void RestoreEthernetLog(AppState state, DateTime date)
    {
        if (!File.Exists(EthernetLogPath)) return;
        try
        {
            EthernetUsageLog? record = null;
            foreach (var line in ReadLinesReverse(EthernetLogPath))
            {
                try
                {
                    var candidate = JsonSerializer.Deserialize<EthernetUsageLog>(line);
                    if (candidate is { FormatVersion: 1 } && candidate.MonthlyLimitBytes >= 0 && candidate.Adapters != null && candidate.Adapters.Values.All(ValidUsage)
                        && candidate.RecordedAt != default && candidate.MonthKey == candidate.RecordedAt.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)
                        && candidate.MonthUploadBytes >= 0 && candidate.MonthDownloadBytes >= 0
                        && candidate.Adapters.Values.Where(x => x.MonthKey == candidate.MonthKey).Sum(x => x.MonthUpload) == candidate.MonthUploadBytes
                        && candidate.Adapters.Values.Where(x => x.MonthKey == candidate.MonthKey).Sum(x => x.MonthDownload) == candidate.MonthDownloadBytes
                        && candidate.MonthUsedBytes == candidate.MonthUploadBytes + candidate.MonthDownloadBytes
                        && candidate.RemainingBytes == (candidate.MonthlyLimitBytes > 0 ? Math.Max(0, candidate.MonthlyLimitBytes - candidate.MonthUsedBytes) : (long?)null))
                    { record = candidate; break; }
                }
                catch { }
            }
            if (record == null) { EthernetLogWarning = "以太网独立日志中没有有效记录。"; return; }
            bool recovered = false;
            bool missingEthernet = state.EthernetAdapterIds.Count == 0 && !record.Adapters.Keys.Any(state.Adapters.ContainsKey);
            if ((record.RecordedAt > state.SavedAt || missingEthernet) && state.EthernetMonthlyLimitBytes != record.MonthlyLimitBytes)
            { state.EthernetMonthlyLimitBytes = record.MonthlyLimitBytes; recovered = true; }
            var month = date.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            if (record.MonthKey == month)
            foreach (var (id, saved) in record.Adapters)
            {
                if (saved.MonthKey != month) continue;
                state.EthernetAdapterIds.Add(id);
                if (!state.Adapters.TryGetValue(id, out var current))
                {
                    saved.RollOver(date); state.Adapters[id] = saved; recovered = true;
                }
                else if (saved.MonthUpload > current.MonthUpload || saved.MonthDownload > current.MonthDownload)
                {
                    current.MonthUpload = Math.Max(current.MonthUpload, saved.MonthUpload);
                    current.MonthDownload = Math.Max(current.MonthDownload, saved.MonthDownload);
                    if (saved.Day == date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))
                    {
                        current.TodayUpload = Math.Max(current.TodayUpload, saved.TodayUpload);
                        current.TodayDownload = Math.Max(current.TodayDownload, saved.TodayDownload);
                    }
                    current.RollOver(date); recovered = true;
                }
            }
            if (recovered) Warning = "已从以太网独立日志恢复本月用量或月限额。";
        }
        catch (Exception ex) { EthernetLogWarning = "以太网独立日志读取失败：" + ex.Message; }
    }
    private static IEnumerable<string> ReadLinesReverse(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[4096]; var line = new List<byte>();
        long position = stream.Length;
        while (position > 0)
        {
            int count = (int)Math.Min(buffer.Length, position); position -= count;
            stream.Position = position; stream.ReadExactly(buffer.AsSpan(0, count));
            for (int i = count - 1; i >= 0; i--)
            {
                if (buffer[i] == (byte)'\n')
                {
                    if (line.Count > 0) { line.Reverse(); yield return System.Text.Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r'); line.Clear(); }
                }
                else line.Add(buffer[i]);
            }
        }
        if (line.Count > 0) { line.Reverse(); yield return System.Text.Encoding.UTF8.GetString(line.ToArray()).TrimEnd('\r'); }
    }
    private sealed class EthernetUsageLog
    {
        public int FormatVersion { get; set; } = 1;
        public DateTimeOffset RecordedAt { get; set; }
        public string MonthKey { get; set; } = "";
        public long MonthlyLimitBytes { get; set; }
        public long MonthUploadBytes { get; set; }
        public long MonthDownloadBytes { get; set; }
        public long MonthUsedBytes { get; set; }
        public long? RemainingBytes { get; set; }
        public Dictionary<string, Usage> Adapters { get; set; } = new();
    }
}

internal sealed class CounterSampler
{
    private (long up, long down, double time)? last;
    public (long up, long down, long upRate, long downRate) Sample(long up, long down, double time)
    {
        var prior = last;
        last = (up, down, time);
        if (prior == null || time <= prior.Value.time) return default;
        long u = up >= prior.Value.up ? up - prior.Value.up : 0;
        long d = down >= prior.Value.down ? down - prior.Value.down : 0;
        double seconds = time - prior.Value.time;
        return (u, d, (long)(u / seconds), (long)(d / seconds));
    }
    public void Reset() => last = null;
}

internal static class StartupManager
{
    public static string TaskName => StartupTaskService.TaskName;
    public static bool IsEnabled() => StartupTaskService.IsEnabled();
    public static void SetEnabled(bool enabled) => StartupTaskService.SetEnabled(enabled, Path.Combine(AppContext.BaseDirectory, "NetworkMonitor.exe"));
}
