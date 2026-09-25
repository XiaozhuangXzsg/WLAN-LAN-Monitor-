using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace NetworkMonitor;

internal readonly record struct FlowKey(byte Protocol, string LocalIp, int LocalPort, string RemoteIp, int RemotePort);
internal readonly record struct FlowRow(int Pid, string RemoteIp, long Upload, long Download);
internal sealed class CaptureSnapshot { public List<FlowRow> Rows { get; } = new(); }

internal sealed class Capture : IDisposable
{
    private readonly Socket socket;
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<FlowKey, Traffic> traffic = new();
    private readonly ConcurrentDictionary<string, string> domains;
    private readonly string localIp;

    public Capture(IPAddress address, ConcurrentDictionary<string, string> domains)
    {
        localIp = address.ToString(); this.domains = domains;
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP);
        socket.Bind(new IPEndPoint(address, 0));
        socket.IOControl(IOControlCode.ReceiveAll, BitConverter.GetBytes(1), null);
        _ = Receive();
    }

    private async Task Receive()
    {
        var buffer = new byte[65535];
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var n = await socket.ReceiveAsync(buffer, SocketFlags.None, stop.Token);
                Parse(buffer.AsSpan(0, n));
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch { await Task.Delay(200); }
        }
    }

    private void Parse(ReadOnlySpan<byte> p)
    {
        if (p.Length < 20 || (p[0] >> 4) != 4) return;
        int header = (p[0] & 15) * 4;
        if (header < 20 || p.Length < header + 4) return;
        byte proto = p[9];
        if (proto != 6 && proto != 17) return;
        // Fragmented packets after the first fragment have no transport ports.
        if (((p[6] & 0x1f) | p[7]) != 0) return;
        string src = new IPAddress(p.Slice(12, 4)).ToString();
        string dst = new IPAddress(p.Slice(16, 4)).ToString();
        bool upload = src == localIp;
        if (!upload && dst != localIp) return;
        int srcPort = (p[header] << 8) | p[header + 1];
        int dstPort = (p[header + 2] << 8) | p[header + 3];
        int bytes = Math.Min(p.Length, (p[2] << 8) | p[3]);
        if (bytes <= 0) return;
        var key = upload ? new FlowKey(proto, src, srcPort, dst, dstPort) : new FlowKey(proto, dst, dstPort, src, srcPort);
        var t = traffic.GetOrAdd(key, _ => new Traffic());
        if (upload) Interlocked.Add(ref t.Up, bytes); else Interlocked.Add(ref t.Down, bytes);
        if (proto == 17 && srcPort == 53 && !upload && p.Length >= header + 8)
            ParseDns(p[(header + 8)..]);
    }

    private void ParseDns(ReadOnlySpan<byte> data)
    {
        try
        {
            if (data.Length < 12 || (data[2] & 0x80) == 0) return;
            int questions = (data[4] << 8) | data[5], answers = (data[6] << 8) | data[7], pos = 12;
            string name = "";
            for (int i = 0; i < Math.Min(questions, 20); i++)
            {
                name = ReadName(data, ref pos);
                pos += 4;
            }
            for (int i = 0; i < Math.Min(answers, 50) && pos < data.Length; i++)
            {
                var answerName = ReadName(data, ref pos);
                if (pos + 10 > data.Length) return;
                int type = (data[pos] << 8) | data[pos + 1];
                int len = (data[pos + 8] << 8) | data[pos + 9];
                pos += 10;
                if (pos + len > data.Length) return;
                if (type == 1 && len == 4)
                {
                    var ip = new IPAddress(data.Slice(pos, 4)).ToString();
                    domains[ip] = string.IsNullOrWhiteSpace(name) ? answerName : name;
                }
                pos += len;
            }
        }
        catch { /* A malformed DNS response must not stop packet capture. */ }
    }

    private static string ReadName(ReadOnlySpan<byte> data, ref int position)
    {
        var parts = new List<string>();
        int pos = position, jumps = 0;
        bool redirected = false;
        while (pos < data.Length && jumps++ < 40)
        {
            int len = data[pos++];
            if (len == 0) { if (!redirected) position = pos; break; }
            if ((len & 0xc0) == 0xc0)
            {
                if (pos >= data.Length) break;
                int next = ((len & 0x3f) << 8) | data[pos++];
                if (!redirected) position = pos;
                pos = next; redirected = true; continue;
            }
            if (len > 63 || pos + len > data.Length) break;
            parts.Add(Encoding.ASCII.GetString(data.Slice(pos, len)));
            pos += len;
            if (!redirected) position = pos;
        }
        return string.Join('.', parts);
    }

    public CaptureSnapshot Drain(Dictionary<FlowKey, int> owners)
    {
        var result = new CaptureSnapshot();
        foreach (var (key, value) in traffic.ToArray())
        {
            long up = Interlocked.Exchange(ref value.Up, 0), down = Interlocked.Exchange(ref value.Down, 0);
            if (up == 0 && down == 0) continue;
            if (!owners.TryGetValue(key, out int pid))
                owners.TryGetValue(key with { RemoteIp = "0.0.0.0", RemotePort = 0 }, out pid);
            if (pid == 0)
                owners.TryGetValue(key with { LocalIp = "0.0.0.0", RemoteIp = "0.0.0.0", RemotePort = 0 }, out pid);
            result.Rows.Add(new FlowRow(pid, key.RemoteIp, up, down));
        }
        return result;
    }

    public void Dispose() { stop.Cancel(); socket.Dispose(); stop.Dispose(); }
}

internal static class ConnectionOwners
{
    private const int Tcp = 2, Udp = 1;
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

    public static Dictionary<FlowKey, int> Read()
    {
        var map = new Dictionary<FlowKey, int>();
        ReadTable(true, map); ReadTable(false, map);
        return map;
    }

    private static void ReadTable(bool tcp, Dictionary<FlowKey, int> map)
    {
        int size = 0;
        if (tcp) GetExtendedTcpTable(IntPtr.Zero, ref size, true, 2, Tcp, 0);
        else GetExtendedUdpTable(IntPtr.Zero, ref size, true, 2, Udp, 0);
        if (size < 4) return;
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            uint result = tcp ? GetExtendedTcpTable(ptr, ref size, true, 2, Tcp, 0)
                : GetExtendedUdpTable(ptr, ref size, true, 2, Udp, 0);
            if (result != 0) return;
            int count = Marshal.ReadInt32(ptr);
            int stride = tcp ? 24 : 12;
            for (int i = 0; i < count && 4L + (long)(i + 1) * stride <= size; i++)
            {
                IntPtr row = IntPtr.Add(ptr, 4 + i * stride);
                int offset = tcp ? 4 : 0;
                string localIp = Ip(Marshal.ReadInt32(row, offset));
                int localPort = Port(Marshal.ReadInt32(row, offset + 4));
                string remoteIp = tcp ? Ip(Marshal.ReadInt32(row, 12)) : "0.0.0.0";
                int remotePort = tcp ? Port(Marshal.ReadInt32(row, 16)) : 0;
                int pid = Marshal.ReadInt32(row, tcp ? 20 : 8);
                map[new FlowKey((byte)(tcp ? 6 : 17), localIp, localPort, remoteIp, remotePort)] = pid;
            }
        }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    private static string Ip(int raw) => new IPAddress(BitConverter.GetBytes(raw)).ToString();
    private static int Port(int raw) => (raw & 0xff) << 8 | ((raw >> 8) & 0xff);
}
