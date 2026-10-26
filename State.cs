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
    public int CycleStartDay { get; set; } = 1;
    public int DailyVersion { get; set; }
    public Dictionary<string, DailyTraffic> Daily { get; set; } = new();
    public List<LegacyTraffic> Legacy { get; set; } = new();
    public string ResetPeriod { get; set; } = "";
    public int ResetCycleStartDay { get; set; } = 1;
    public long ResetUpload { get; set; }
    public long ResetDownload { get; set; }
    public long Total => Upload + Download;
    public long Today => TodayUpload + TodayDownload;
    public long Month => MonthUpload + MonthDownload;
    public bool RollOver(DateTime date)
    {
        EnsureDaily(date);
        var day = date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var period = BillingPeriod.For(date, CycleStartDay);
        var month = period.Key(CycleStartDay);
        bool changed = Day != day || MonthKey != month || Upload != MonthUpload || Download != MonthDownload;
        Day = day; MonthKey = month;
        Daily.TryGetValue(day, out var today);
        TodayUpload = today?.Upload ?? 0; TodayDownload = today?.Download ?? 0;
        var raw = RawPeriod(period);
        bool resetApplies = ResetPeriod == month && ResetCycleStartDay == CycleStartDay;
        MonthUpload = Math.Max(0, raw.up - (resetApplies ? ResetUpload : 0));
        MonthDownload = Math.Max(0, raw.down - (resetApplies ? ResetDownload : 0));
        Upload = MonthUpload; Download = MonthDownload;
        return changed;
    }
    public void Add(long up, long down, DateTime date)
    {
        RollOver(date);
        if (!Daily.TryGetValue(Day, out var today)) Daily[Day] = today = new();
        today.Upload += Math.Max(0, up); today.Download += Math.Max(0, down);
        RollOver(date);
    }
    private void EnsureDaily(DateTime? asOf = null)
    {
        if (DailyVersion >= 1) return;
        DailyVersion = 1;
        if (DateOnly.TryParseExact(Day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var recordedDay))
            Daily[Day] = new() { Upload = TodayUpload, Download = TodayDownload };
        var key = MonthKey.Length == 7 ? MonthKey + "-01" : MonthKey;
        if (!DateOnly.TryParseExact(key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var start)) return;
        if (recordedDay == default || recordedDay < start)
        {
            var end = MonthKey.Length == 7 ? new DateOnly(start.Year, start.Month, 1).AddMonths(1) : BillingPeriod.For(start.ToDateTime(TimeOnly.MinValue), CycleStartDay).End;
            var reference = DateOnly.FromDateTime(asOf ?? DateTime.Now);
            recordedDay = reference >= start && reference < end ? reference : end.AddDays(-1);
        }
        bool resetApplies = ResetPeriod == MonthKey && ResetCycleStartDay == CycleStartDay;
        long rawUp = MonthUpload + (resetApplies ? ResetUpload : 0);
        long rawDown = MonthDownload + (resetApplies ? ResetDownload : 0);
        var known = Daily.Where(pair => ParseDay(pair.Key) >= start && ParseDay(pair.Key) <= recordedDay).Select(pair => pair.Value).ToList();
        long remainingUp = Math.Max(0, rawUp - known.Sum(value => value.Upload));
        long remainingDown = Math.Max(0, rawDown - known.Sum(value => value.Download));
        if (remainingUp + remainingDown > 0) Legacy.Add(new() { Start = FormatDay(start), Through = FormatDay(recordedDay), Upload = remainingUp, Download = remainingDown });
    }
    public void Backfill(string day, long up, long down)
    {
        if (up < 0 || down < 0) return;
        EnsureDaily();
        if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)) return;
        if (!Daily.TryGetValue(day, out var entry)) Daily[day] = entry = new();
        long extraUp = Math.Max(0, up - entry.Upload), extraDown = Math.Max(0, down - entry.Download);
        entry.Upload += extraUp; entry.Download += extraDown;
        foreach (var carry in Legacy.Where(item => parsed >= ParseDay(item.Start) && parsed <= ParseDay(item.Through)))
        {
            long takeUp = Math.Min(extraUp, carry.Upload), takeDown = Math.Min(extraDown, carry.Download);
            carry.Upload -= takeUp; carry.Download -= takeDown; extraUp -= takeUp; extraDown -= takeDown;
        }
    }
    private (long up, long down) RawPeriod(BillingPeriod period)
    {
        var days = Daily.Where(pair => period.Contains(ParseDay(pair.Key))).Select(pair => pair.Value).ToList();
        var carry = Legacy.Where(item => period.Contains(ParseDay(item.Through))).ToList();
        return (days.Sum(item => item.Upload) + carry.Sum(item => item.Upload), days.Sum(item => item.Download) + carry.Sum(item => item.Download));
    }
    public void ResetCurrent(DateTime date)
    {
        RollOver(date);
        var raw = RawPeriod(BillingPeriod.For(date, CycleStartDay));
        ResetPeriod = MonthKey; ResetCycleStartDay = CycleStartDay; ResetUpload = raw.up; ResetDownload = raw.down;
        RollOver(date);
    }
    public Usage Summary() => new()
    {
        Upload = Upload, Download = Download, MonthUpload = MonthUpload, MonthDownload = MonthDownload,
        TodayUpload = TodayUpload, TodayDownload = TodayDownload, Day = Day, MonthKey = MonthKey,
        CycleStartDay = CycleStartDay, ResetPeriod = ResetPeriod, ResetCycleStartDay = ResetCycleStartDay, ResetUpload = ResetUpload, ResetDownload = ResetDownload
    };
    internal static DateOnly ParseDay(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
    internal static string FormatDay(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class AppState
{
    public string ThemeName { get; set; } = "原神暖白";
    public bool GithubWelcomeShown { get; set; }
    public bool ShowFloating { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowWlan { get; set; } = true;
    public int CycleStartDay { get; set; } = 1;
    public int HistoryDays { get; set; } = 7;
    public bool HistoryBars { get; set; } = true;
    public DateTimeOffset CounterResetAt { get; set; }
    public int? FloatingX { get; set; }
    public int? FloatingY { get; set; }
    public int? FloatingWidth { get; set; }
    public int? FloatingHeight { get; set; }
    public Dictionary<string, Usage> Adapters { get; set; } = new();
    public HashSet<string> EthernetAdapterIds { get; set; } = new();
    public DateTimeOffset SavedAt { get; set; }
    public long SaveSequence { get; set; }
    public long EthernetMonthlyLimitBytes { get; set; }
    public long EthernetMonth => EthernetAdapterIds.Where(Adapters.ContainsKey).Sum(id => Adapters[id].Month);
    public bool RollOver(DateTime date)
    {
        bool changed = false;
        CycleStartDay = Math.Clamp(CycleStartDay, 1, 31);
        foreach (var usage in Adapters.Values) { usage.CycleStartDay = CycleStartDay; changed |= usage.RollOver(date); }
        return changed;
    }
    public Usage GetUsage(string id, DateTime? date = null)
    {
        if (!Adapters.TryGetValue(id, out var usage)) Adapters[id] = usage = new Usage();
        usage.CycleStartDay = CycleStartDay; usage.RollOver(date ?? DateTime.Now);
        return usage;
    }
    public void ResetCurrent(DateTime date)
    {
        RollOver(date);
        foreach (var usage in Adapters.Values) usage.ResetCurrent(date);
        CounterResetAt = DateTimeOffset.UtcNow;
    }
    public List<DailyPoint> DailySeries(DateTime end, int count)
    {
        var start = DateOnly.FromDateTime(end).AddDays(1 - count);
        return Enumerable.Range(0, count).Select(index =>
        {
            var date = start.AddDays(index); string key = Usage.FormatDay(date);
            long eu = 0, ed = 0, wu = 0, wd = 0;
            foreach (var (id, usage) in Adapters)
            {
                if (!usage.Daily.TryGetValue(key, out var daily)) continue;
                if (EthernetAdapterIds.Contains(id)) { eu += daily.Upload; ed += daily.Download; }
                else { wu += daily.Upload; wd += daily.Download; }
            }
            return new DailyPoint(date, eu, ed, wu, wd);
        }).ToList();
    }
    public AppState Summary()
    {
        var copy = (AppState)MemberwiseClone();
        copy.Adapters = Adapters.ToDictionary(pair => pair.Key, pair => pair.Value.Summary());
        copy.EthernetAdapterIds = new(EthernetAdapterIds);
        return copy;
    }
    public void MergeDaily(AppState older)
    {
        foreach (var (id, previous) in older.Adapters)
        {
            if (!Adapters.TryGetValue(id, out var current)) { Adapters[id] = previous; continue; }
            foreach (var (day, daily) in previous.Daily) current.Backfill(day, daily.Upload, daily.Download);
        }
        EthernetAdapterIds.UnionWith(older.EthernetAdapterIds);
    }
}

internal sealed class StateStore
{
    private readonly string path;
    private bool needsDailyBackfill, preserveDisplaySettings;
    private long saveSequence;
    public string? Warning { get; private set; }
    public string? LogWarning { get; private set; }
    public string? EthernetLogWarning { get; private set; }
    public string? DailyHistoryWarning { get; private set; }
    public string EthernetLogPath => Path.Combine(Path.GetDirectoryName(path)!, "ethernet-usage.log");
    public string DailyHistoryPath => Path.Combine(Path.GetDirectoryName(path)!, "daily-usage.json");
    public StateStore(string? path = null) => this.path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NetworkMonitor", "state.json");
    public AppState Load(DateTime? date = null)
    {
        var state = LoadStored();
        needsDailyBackfill = false;
        preserveDisplaySettings = false;
        try { preserveDisplaySettings = Read(path).Adapters.Count == 0; } catch { }
        var now = date ?? DateTime.Now;
        foreach (var historyPath in new[] { DailyHistoryPath, DailyHistoryPath + ".bak" })
        {
            try
            {
                var history = Read(historyPath);
                if (Newer(history, state) || (state.Adapters.Count == 0 && history.Adapters.Count > 0))
                { if (preserveDisplaySettings) KeepDisplaySettings(history, state); state = history; Warning = "已从独立每日记录恢复用量与统计设置。"; }
            }
            catch { }
        }
        bool migrate = state.Adapters.Values.Any(usage => usage.DailyVersion == 0);
        state.RollOver(now);
        state = RestoreEthernetLog(state, now);
        if (migrate || needsDailyBackfill || state.Adapters.Values.Any(usage => usage.DailyVersion == 0)) BackfillDailyLogs(state);
        state.RollOver(now);
        saveSequence = Math.Max(saveSequence, state.SaveSequence);
        return state;
    }
    private static bool Newer(AppState candidate, AppState current) => candidate.SaveSequence > current.SaveSequence
        || (candidate.SaveSequence == current.SaveSequence && candidate.SavedAt > current.SavedAt);
    private static void KeepDisplaySettings(AppState target, AppState current)
    {
        target.ThemeName = current.ThemeName; target.GithubWelcomeShown = current.GithubWelcomeShown;
        target.ShowFloating = current.ShowFloating; target.MinimizeToTray = current.MinimizeToTray;
        target.FloatingX = current.FloatingX; target.FloatingY = current.FloatingY;
        target.FloatingWidth = current.FloatingWidth; target.FloatingHeight = current.FloatingHeight;
        target.ShowWlan = current.ShowWlan; target.HistoryDays = current.HistoryDays; target.HistoryBars = current.HistoryBars;
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
    private static bool ValidUsage(Usage? usage) => usage != null && usage.Upload >= 0 && usage.Download >= 0 && usage.MonthUpload >= 0 && usage.MonthDownload >= 0 && usage.TodayUpload >= 0 && usage.TodayDownload >= 0
        && usage.Daily != null && usage.Legacy != null && usage.ResetUpload >= 0 && usage.ResetDownload >= 0
        && usage.Daily.All(pair => DateOnly.TryParseExact(pair.Key, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _) && pair.Value is { Upload: >= 0, Download: >= 0 })
        && usage.Legacy.All(item => item != null && item.Upload >= 0 && item.Download >= 0
            && DateOnly.TryParseExact(item.Start, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _)
            && DateOnly.TryParseExact(item.Through, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _));
    private static bool ValidState(AppState state) => state.Adapters != null && state.EthernetAdapterIds != null && state.EthernetMonthlyLimitBytes >= 0 && state.Adapters.Values.All(ValidUsage);
    public void Save(AppState state, bool writeLog = false, DateTime? date = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var now = date ?? DateTime.Now;
        state.SaveSequence = saveSequence = Math.Max(saveSequence, state.SaveSequence) + 1;
        state.SavedAt = new DateTimeOffset(now);
        // Keep daily history and reset/cycle metadata outside the install directory too.
        try { WriteState(DailyHistoryPath, state); DailyHistoryWarning = null; }
        catch (Exception ex) { DailyHistoryWarning = "独立每日记录保存失败：" + ex.Message; }
        if (writeLog)
        {
            // Flush the independent record first so it can recover even a failed state save.
            try { AppendEthernetLog(state, now); EthernetLogWarning = null; }
            catch (Exception ex) { EthernetLogWarning = "以太网独立日志保存失败：" + ex.Message; }
        }
        WriteState(path, state);
        if (writeLog)
        {
            try { AppendLog(state, now); LogWarning = null; }
            catch (Exception ex) { LogWarning = "日志追加失败：" + ex.Message; }
        }
    }
    private static void WriteState(string target, AppState state)
    {
        string temporary = target + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, state, new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(true);
        }
        if (File.Exists(target)) File.Copy(target, target + ".bak", true);
        File.Move(temporary, target, true);
    }
    public void AppendLog(AppState state, DateTime? date = null)
    {
        var now = date ?? DateTime.Now;
        var logPath = Path.Combine(Path.GetDirectoryName(path)!, "usage-" + now.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture) + ".log");
        // Daily ledgers are stored once in daily-usage.json; minute snapshots stay small.
        var line = JsonSerializer.Serialize(new UsageLog { RecordedAt = new DateTimeOffset(now), State = state.Summary() });
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

    private void BackfillDailyLogs(AppState state)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory)) return;
        foreach (var log in Directory.EnumerateFiles(directory, "usage-*.log"))
        foreach (var line in File.ReadLines(log))
        {
            try
            {
                using var json = JsonDocument.Parse(line);
                foreach (var adapter in json.RootElement.GetProperty("State").GetProperty("Adapters").EnumerateObject())
                {
                    if (!state.Adapters.TryGetValue(adapter.Name, out var usage)) continue;
                    var record = adapter.Value;
                    usage.Backfill(record.GetProperty("Day").GetString() ?? "", record.GetProperty("TodayUpload").GetInt64(), record.GetProperty("TodayDownload").GetInt64());
                }
            }
            catch { }
        }
    }

    public void AppendEthernetLog(AppState state, DateTime date)
    {
        var month = BillingPeriod.For(date, state.CycleStartDay).Key(state.CycleStartDay);
        var adapters = state.EthernetAdapterIds.Where(state.Adapters.ContainsKey)
            .ToDictionary(id => id, id => state.Adapters[id].Summary());
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
        record.FormatVersion = 2; record.CycleStartDay = state.CycleStartDay; record.Snapshot = state.Summary();
        Directory.CreateDirectory(Path.GetDirectoryName(EthernetLogPath)!);
        using var stream = new FileStream(EthernetLogPath, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
        // A newline also isolates a partially written tail left by a sudden power loss.
        if (stream.Length > 0) writer.WriteLine();
        writer.WriteLine(JsonSerializer.Serialize(record)); writer.Flush(); stream.Flush(true);
    }
    private AppState RestoreEthernetLog(AppState state, DateTime date)
    {
        if (!File.Exists(EthernetLogPath)) return state;
        try
        {
            EthernetUsageLog? record = null;
            foreach (var line in ReadLinesReverse(EthernetLogPath))
            {
                try
                {
                    var candidate = JsonSerializer.Deserialize<EthernetUsageLog>(line);
                    if (candidate is { FormatVersion: 1 or 2 } && candidate.MonthlyLimitBytes >= 0 && candidate.Adapters != null && candidate.Adapters.Values.All(ValidUsage)
                        && candidate.RecordedAt != default && candidate.MonthKey == BillingPeriod.For(candidate.RecordedAt.DateTime, candidate.CycleStartDay).Key(candidate.CycleStartDay)
                        && candidate.MonthUploadBytes >= 0 && candidate.MonthDownloadBytes >= 0
                        && candidate.Adapters.Values.Where(x => x.MonthKey == candidate.MonthKey).Sum(x => x.MonthUpload) == candidate.MonthUploadBytes
                        && candidate.Adapters.Values.Where(x => x.MonthKey == candidate.MonthKey).Sum(x => x.MonthDownload) == candidate.MonthDownloadBytes
                        && candidate.MonthUsedBytes == candidate.MonthUploadBytes + candidate.MonthDownloadBytes
                        && candidate.RemainingBytes == (candidate.MonthlyLimitBytes > 0 ? Math.Max(0, candidate.MonthlyLimitBytes - candidate.MonthUsedBytes) : (long?)null))
                    { record = candidate; break; }
                }
                catch { }
            }
            if (record == null) { EthernetLogWarning = "以太网独立日志中没有有效记录。"; return state; }
            if (record is { FormatVersion: 2, Snapshot: { } snapshot } && ValidState(snapshot)
                && (Newer(snapshot, state) || state.Adapters.Count == 0))
            {
                snapshot.RollOver(date);
                snapshot.MergeDaily(state);
                if (preserveDisplaySettings) KeepDisplaySettings(snapshot, state);
                state = snapshot;
                state.RollOver(date);
                Warning = "已从独立日志恢复用量、清零标记及统计周期。";
                needsDailyBackfill = true;
                return state;
            }
            if (record.FormatVersion == 2) return state;
            if (state.SaveSequence > 0 && state.CounterResetAt != default) return state;
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
                    saved.RollOver(date);
                    foreach (var (day, daily) in current.Daily) saved.Backfill(day, daily.Upload, daily.Download);
                    state.Adapters[id] = saved;
                    saved.RollOver(date); recovered = true;
                }
            }
            if (recovered) Warning = "已从以太网独立日志恢复本月用量或月限额。";
        }
        catch (Exception ex) { EthernetLogWarning = "以太网独立日志读取失败：" + ex.Message; }
        return state;
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
        public int CycleStartDay { get; set; } = 1;
        public AppState? Snapshot { get; set; }
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
