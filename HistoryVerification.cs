using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Text.Json;

namespace NetworkMonitor;

internal static class HistoryVerification
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var root = Path.Combine(directory, "fixtures-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var results = new List<string>();
        void Check(bool value, string name) { if (!value) throw new Exception(name); results.Add("PASS " + name); }
        var date = new DateTime(2026, 10, 26, 12, 0, 0);
        var state = new AppState { CycleStartDay = 5, EthernetMonthlyLimitBytes = 1000 };
        state.EthernetAdapterIds.Add("ethernet");
        void Add(string id, DateTime at, long up, long down) => state.GetUsage(id, at).Add(up, down, at);
        Add("ethernet", new DateTime(2026, 10, 4), 100, 100);
        Add("ethernet", new DateTime(2026, 10, 5), 100, 300);
        Add("ethernet", date, 20, 30); Add("wlan", date, 40, 60);
        Check(state.EthernetMonth == 450 && state.GetUsage("ethernet", date).Today == 50, "custom cycle sums daily records from its start date");
        state.CycleStartDay = 1; state.RollOver(date);
        Check(state.EthernetMonth == 650, "changing the start date recomputes known daily history");
        state.CycleStartDay = 5; state.RollOver(date);
        var daily = state.DailySeries(date, 30);
        Check(daily.Count == 30 && daily[0].Date == DateOnly.FromDateTime(date).AddDays(-29) && daily[^1].Date == DateOnly.FromDateTime(date), "30-day statistics use one dated point per calendar day");
        Check(daily.Single(point => point.Date == new DateOnly(2026, 10, 4)).Ethernet == 200 && daily.Count(point => point.Ethernet == 0 && point.Wlan == 0) == 27, "daily statistics retain prior-cycle usage and fill missing days with zero");
        Check(BillingPeriod.For(new DateTime(2026, 2, 27), 31) == new BillingPeriod(new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28)), "31st-day cycle ends on February's last day");
        Check(BillingPeriod.For(new DateTime(2026, 2, 28), 31) == new BillingPeriod(new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31)), "short-month cycle starts once and returns to the selected date next month");
        Check(BillingPeriod.For(new DateTime(2028, 2, 29), 31).Start == new DateOnly(2028, 2, 29), "leap-year cycle clamps to February 29");
        Check(BillingPeriod.For(new DateTime(2027, 1, 1), 5).Start == new DateOnly(2026, 12, 5), "billing cycle crosses a year boundary");
        var path = Path.Combine(root, "state.json"); var store = new StateStore(path);
        store.Save(state, true, date);
        state.ResetCurrent(date);
        Check(state.EthernetMonth == 0 && state.GetUsage("wlan", date).Month == 0 && state.EthernetMonthlyLimitBytes == 1000, "reset clears both current counters and preserves the quota");
        Check(state.DailySeries(date, 30).Sum(point => point.Ethernet) == 650, "reset preserves factual daily history");
        Add("ethernet", date, 5, 7); Add("wlan", date, 2, 3);
        Check(state.EthernetMonth == 12 && state.GetUsage("wlan", date).Month == 5 && state.GetUsage("ethernet", date).Today == 62, "traffic after reset counts only once while today's full usage remains available");
        state.ShowWlan = false; state.HistoryDays = 30; state.HistoryBars = false;
        store.Save(state, true, date.AddMinutes(1));
        var recovered = store.Load(date);
        Check(recovered.EthernetMonth == 12 && recovered.CycleStartDay == 5 && !recovered.ShowWlan && recovered.HistoryDays == 30 && !recovered.HistoryBars, "restart preserves reset baseline cycle and display preferences");
        File.WriteAllText(path, "broken primary");
        recovered = store.Load(date);
        Check(recovered.EthernetMonth == 12 && recovered.GetUsage("wlan", date).Month == 5 && recovered.DailySeries(date, 30).Sum(point => point.Ethernet) == 662, "daily independent record restores latest data instead of reviving the pre-reset backup");
        File.WriteAllText(store.DailyHistoryPath, "broken history"); File.WriteAllText(store.DailyHistoryPath + ".bak", "broken history backup");
        File.WriteAllText(path + ".bak", "broken primary backup");
        recovered = store.Load(date);
        Check(recovered.EthernetMonth == 12 && recovered.GetUsage("wlan", date).Month == 5 && recovered.CycleStartDay == 5, "compact monthly logs recover reset and cycle metadata without reviving old totals");
        foreach (var log in Directory.GetFiles(root, "usage-*.log")) File.Delete(log);
        recovered = store.Load(date);
        Check(recovered.EthernetMonth == 12 && recovered.GetUsage("wlan", date).Month == 5 && !recovered.ShowWlan, "Ethernet independent log also recovers the new summary metadata");
        recovered.RollOver(new DateTime(2026, 11, 5)); recovered.GetUsage("ethernet", new DateTime(2026, 11, 5)).Add(3, 4, new DateTime(2026, 11, 5));
        Check(recovered.EthernetMonth == 7, "a new cycle drops the old reset baseline");
        var shortCycle = new AppState { CycleStartDay = 31 }; shortCycle.EthernetAdapterIds.Add("ethernet");
        shortCycle.GetUsage("ethernet", new DateTime(2026, 3, 1)).Add(10, 20, new DateTime(2026, 3, 1));
        shortCycle.ResetCurrent(new DateTime(2026, 3, 1)); shortCycle.CycleStartDay = 28; shortCycle.RollOver(new DateTime(2026, 3, 1));
        Check(shortCycle.EthernetMonth == 30, "changing a short-month cycle does not reuse another start date's reset baseline");
        var clockPath = Path.Combine(root, "clock", "state.json"); var clockStore = new StateStore(clockPath);
        var clockState = new AppState(); clockState.EthernetAdapterIds.Add("ethernet");
        clockState.GetUsage("ethernet", date).Add(10, 20, date); clockStore.Save(clockState, true, date.AddHours(1));
        clockState.ResetCurrent(date); clockStore.Save(clockState, true, date);
        File.WriteAllText(clockPath, "broken primary after clock adjustment");
        Check(clockStore.Load(date).EthernetMonth == 0, "recovery keeps the latest reset even if the system clock moved backward");
        var legacyRoot = Path.Combine(root, "legacy"); Directory.CreateDirectory(legacyRoot);
        var legacyPath = Path.Combine(legacyRoot, "state.json");
        var legacy = new AppState { EthernetMonthlyLimitBytes = 2000 }; legacy.EthernetAdapterIds.Add("ethernet");
        legacy.Adapters["ethernet"] = new Usage { MonthKey = "2026-10", Day = "2026-10-26", MonthUpload = 300, MonthDownload = 600, Upload = 300, Download = 600, TodayUpload = 100, TodayDownload = 400 };
        File.WriteAllText(legacyPath, JsonSerializer.Serialize(legacy));
        var oldDay = new AppState(); oldDay.Adapters["ethernet"] = new Usage { Day = "2026-10-25", TodayUpload = 200, TodayDownload = 200 };
        var line = JsonSerializer.Serialize(new { RecordedAt = date.AddDays(-1), State = oldDay });
        File.WriteAllText(Path.Combine(legacyRoot, "usage-2026-10.log"), line + "\n" + line + "\nbroken tail");
        var migrated = new StateStore(legacyPath).Load(date);
        Check(migrated.EthernetMonth == 900 && migrated.DailySeries(date, 7).Sum(point => point.Ethernet) == 900, "upgrade reconstructs daily records from old snapshots without inventing dates");
        Check(migrated.Adapters["ethernet"].Legacy.Sum(item => item.Upload + item.Download) == 0, "backfilled daily bytes replace the unallocated legacy carry without double counting");
        migrated.GetUsage("ethernet", date).Add(1, 2, date);
        Check(migrated.EthernetMonth == 903, "new traffic adds to a migrated ledger without consuming old carry twice");
        var summary = migrated.Summary(); summary.RollOver(date); summary.MergeDaily(migrated); summary.RollOver(date);
        Check(summary.EthernetMonth == 903, "summary restoration merges known daily records without duplication");
        var undated = new Usage { MonthKey = "2026-10", MonthUpload = 1000 };
        undated.RollOver(date); undated.Backfill("2026-10-25", 500, 0); undated.RollOver(date);
        Check(undated.Month == 1000 && undated.Daily["2026-10-25"].Upload == 500, "backfilling an undated legacy month replaces carry rather than double counting");

        var uiRoot = Path.Combine(root, "ui"); var uiPath = Path.Combine(uiRoot, "state.json");
        var now = DateTime.Now;
        var uiState = new AppState { ShowFloating = true, GithubWelcomeShown = true, EthernetMonthlyLimitBytes = 50L * 1024 * 1024 * 1024 };
        uiState.EthernetAdapterIds.Add("ethernet");
        for (int i = 39; i >= 0; i--)
        {
            var at = now.Date.AddDays(-i);
            uiState.GetUsage("ethernet", at).Add((40 - i) * 1024L * 1024, (i % 6 + 1) * 170L * 1024 * 1024, at);
            uiState.GetUsage("wlan", at).Add(10L * 1024 * 1024, (i % 4 + 1) * 60L * 1024 * 1024, at);
        }
        new StateStore(uiPath).Save(uiState, true, now);
        using (var form = new MainForm(preview: true, statePath: uiPath, persistPreview: true))
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var liveState = (AppState)typeof(MainForm).GetField("state", flags)!.GetValue(form)!;
            var samplers = (Dictionary<string, CounterSampler>)typeof(MainForm).GetField("samplers", flags)!.GetValue(form)!;
            var ether = new FixtureAdapter("ethernet", NetworkInterfaceType.Ethernet); var wifi = new FixtureAdapter("wlan", NetworkInterfaceType.Wireless80211);
            foreach (var nic in new[] { ether, wifi })
            {
                typeof(MainForm).GetMethod("AddAdapter", flags)!.Invoke(form, new object[] { nic });
                samplers[nic.Id].Sample(nic.Upload, nic.Download, Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency - 1);
            }
            form.Show(); Application.DoEvents();
            form.SetWlanVisible(false); Application.DoEvents();
            Check(form.Floating is { Visible: true } && !((RateComparison)form.Floating.Controls[0]).ShowWlan, "WLAN hiding synchronizes to the live floating window");
            long before = liveState.GetUsage("wlan").Month;
            wifi.Upload += 10; wifi.Download += 20;
            typeof(MainForm).GetMethod("RefreshData", flags)!.Invoke(form, null);
            Check(liveState.GetUsage("wlan").Month == before + 30, "hidden WLAN keeps accumulating through the actual sampling path");
            void Snapshot(Control window, string name)
            {
                using var bitmap = new Bitmap(window.Width, window.Height);
                window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(directory, name));
            }
            Snapshot(form, "ethernet-only.png"); Snapshot(form.Floating!, "floating-ethernet-only.png");
            form.SetWlanVisible(true); Application.DoEvents();
            Check(((RateComparison)form.Floating!.Controls[0]).ShowWlan && liveState.GetUsage("wlan").Month == before + 30, "WLAN reopening restores its recorded data");
            IEnumerable<Control> Children(Control rootControl) => rootControl.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Children(child)));
            var pages = Children(form).OfType<TabControl>().Single(control => control.Name == "DashboardPages"); pages.SelectedIndex = 1; Application.DoEvents();
            var chart = Children(form).OfType<DailyHistoryChart>().Single();
            Check(chart.Points.Count == 7 && chart.Bars, "daily statistics initially show seven daily bars");
            Snapshot(form, "daily-7-bars.png");
            Children(form).OfType<RoundedToggle>().Single(control => control.Text == "近 30 日").PerformClick();
            Children(form).OfType<RoundedToggle>().Single(control => control.Text == "折线图").PerformClick(); Application.DoEvents();
            Check(chart.Points.Count == 30 && !chart.Bars, "history controls switch to thirty daily line points"); Snapshot(form, "daily-30-lines.png");
            form.ApplyCycleDay(15);
            Check(liveState.EthernetMonth == liveState.DailySeries(now, now.Day >= 15 ? now.Day - 14 : now.Day + DateTime.DaysInMonth(now.AddMonths(-1).Year, now.AddMonths(-1).Month) - 14).Sum(point => point.Ethernet), "applying the start date refreshes live counters from the ledger");
            long factual = liveState.DailySeries(now, 30).Sum(point => point.Ethernet);
            form.ResetPeriodUsage();
            Check(liveState.EthernetMonth == 0 && liveState.GetUsage("wlan").Month == 0 && liveState.DailySeries(now, 30).Sum(point => point.Ethernet) == factual, "the UI reset action clears both period counters while preserving chart history");
            Snapshot(form, "daily-after-reset.png");
            form.SetWlanVisible(false); form.ExitApplication();
        }
        var uiReload = new StateStore(uiPath).Load();
        Check(uiReload.EthernetMonth == 0 && uiReload.CycleStartDay == 15 && !uiReload.ShowWlan && uiReload.HistoryDays == 30 && !uiReload.HistoryBars, "UI cycle reset WLAN visibility and history selections survive exit and restart");
        File.WriteAllLines(Path.Combine(directory, "history-verification.txt"), results);
    }

    private sealed class FixtureAdapter(string id, NetworkInterfaceType kind) : NetworkInterface
    {
        public long Upload = 1000, Download = 2000;
        public override string Id => id;
        public override string Name => id == "ethernet" ? "以太网" : "校园 WLAN";
        public override string Description => "History verification adapter";
        public override NetworkInterfaceType NetworkInterfaceType => kind;
        public override OperationalStatus OperationalStatus => OperationalStatus.Up;
        public override IPInterfaceStatistics GetIPStatistics() => new FixtureStatistics(Upload, Download);
    }
    private sealed class FixtureStatistics(long upload, long download) : IPInterfaceStatistics
    {
        public override long BytesSent => upload;
        public override long BytesReceived => download;
        public override long IncomingPacketsDiscarded => 0;
        public override long IncomingPacketsWithErrors => 0;
        public override long IncomingUnknownProtocolPackets => 0;
        public override long NonUnicastPacketsReceived => 0;
        public override long NonUnicastPacketsSent => 0;
        public override long OutgoingPacketsDiscarded => 0;
        public override long OutgoingPacketsWithErrors => 0;
        public override long OutputQueueLength => 0;
        public override long UnicastPacketsReceived => 0;
        public override long UnicastPacketsSent => 0;
    }
}
