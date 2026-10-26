using System.Globalization;
using System.Text.Json.Serialization;

namespace NetworkMonitor;

internal readonly record struct BillingPeriod(DateOnly Start, DateOnly End)
{
    public string Key(int day) => Start.ToString(day == 1 ? "yyyy-MM" : "yyyy-MM-dd", CultureInfo.InvariantCulture);
    public static BillingPeriod For(DateTime date, int startDay)
    {
        int day = Math.Clamp(startDay, 1, 31);
        var today = DateOnly.FromDateTime(date);
        DateOnly Anchor(int year, int month) => new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));
        var start = Anchor(today.Year, today.Month);
        if (today < start) { var previous = today.AddMonths(-1); start = Anchor(previous.Year, previous.Month); }
        var next = start.AddMonths(1);
        return new(start, Anchor(next.Year, next.Month));
    }
    public bool Contains(DateOnly day) => day >= Start && day < End;
}

internal sealed class DailyTraffic
{
    public long Upload { get; set; }
    public long Download { get; set; }
    [JsonIgnore] public long Total => Upload + Download;
}

internal sealed class LegacyTraffic
{
    public string Start { get; set; } = "";
    public string Through { get; set; } = "";
    public long Upload { get; set; }
    public long Download { get; set; }
}

internal readonly record struct DailyPoint(DateOnly Date, long EthernetUpload, long EthernetDownload, long WlanUpload, long WlanDownload)
{
    public long Ethernet => EthernetUpload + EthernetDownload;
    public long Wlan => WlanUpload + WlanDownload;
}
