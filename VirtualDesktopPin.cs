using System.Runtime.InteropServices;

namespace NetworkMonitor;

// The shell exposes per-window pinning separately from IVirtualDesktopManager.
// Use only the stable collection/pinning slots, not the build-specific desktop manager.
internal sealed class VirtualDesktopPin : IDisposable
{
    private IShellServiceProvider? shell;
    private IApplicationViewCollection? views;
    private IVirtualDesktopPinnedApps? pins;
    private IntPtr ownedWindow;
    private DateTime retryAt;
    private bool disposed;
    internal bool IsPinned { get; private set; }
    internal string? Error { get; private set; }

    internal bool EnsurePinned(IntPtr window)
    {
        if (disposed || window == IntPtr.Zero) return false;
        if (DateTime.UtcNow < retryAt) return IsPinned;
        IntPtr view = IntPtr.Zero;
        try
        {
            Connect();
            Marshal.ThrowExceptionForHR(views!.GetViewForHwnd(window, out view));
            if (view == IntPtr.Zero) throw new InvalidOperationException("The shell has not registered the window yet.");
            Marshal.ThrowExceptionForHR(pins!.IsViewPinned(view, out int pinned));
            if (pinned == 0)
            {
                Marshal.ThrowExceptionForHR(pins.PinView(view));
                ownedWindow = window;
                Marshal.ThrowExceptionForHR(pins.IsViewPinned(view, out pinned));
            }
            IsPinned = pinned != 0;
            Error = IsPinned ? null : "Windows has not pinned the floating window yet.";
            retryAt = default;
            return IsPinned;
        }
        catch (Exception ex)
        {
            IsPinned = false; Error = ex.Message;
            Disconnect();
            retryAt = DateTime.UtcNow.AddSeconds(1);
            return false;
        }
        finally { if (view != IntPtr.Zero) Marshal.Release(view); }
    }

    internal bool IsWindowPinned(IntPtr window)
    {
        if (disposed || window == IntPtr.Zero) return false;
        IntPtr view = IntPtr.Zero;
        try
        {
            Connect();
            int result = views!.GetViewForHwnd(window, out view);
            if (result == unchecked((int)0x8002802B) || result == unchecked((int)0x80070057)) return false;
            Marshal.ThrowExceptionForHR(result);
            if (view == IntPtr.Zero) return false;
            Marshal.ThrowExceptionForHR(pins!.IsViewPinned(view, out int pinned));
            return pinned != 0;
        }
        finally { if (view != IntPtr.Zero) Marshal.Release(view); }
    }

    internal void ReleaseWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || ownedWindow != window) return;
        IntPtr view = IntPtr.Zero;
        try
        {
            if (views != null && pins != null && views.GetViewForHwnd(window, out view) >= 0 && view != IntPtr.Zero)
                pins.UnpinView(view);
        }
        catch { /* Explorer or the HWND may already have gone away during shutdown. */ }
        finally
        {
            if (view != IntPtr.Zero) Marshal.Release(view);
            ownedWindow = IntPtr.Zero; IsPinned = false;
        }
    }

    internal void ResetConnection()
    {
        Disconnect(); retryAt = default; IsPinned = false;
    }

    private void Connect()
    {
        if (views != null && pins != null) return;
        shell = (IShellServiceProvider)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239"), throwOnError: true)!)!;
        views = Query<IApplicationViewCollection>(typeof(IApplicationViewCollection).GUID);
        pins = Query<IVirtualDesktopPinnedApps>(new Guid("B5A399E7-1C87-46B8-88E9-FC5747B171BD"));
    }

    private T Query<T>(Guid service) where T : class
    {
        Guid iid = typeof(T).GUID;
        Marshal.ThrowExceptionForHR(shell!.QueryService(ref service, ref iid, out IntPtr pointer));
        try { return (T)Marshal.GetObjectForIUnknown(pointer); }
        finally { Marshal.Release(pointer); }
    }

    private void Disconnect()
    {
        if (pins != null) Marshal.ReleaseComObject(pins);
        if (views != null) Marshal.ReleaseComObject(views);
        if (shell != null) Marshal.ReleaseComObject(shell);
        pins = null; views = null; shell = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        ReleaseWindow(ownedWindow); Disconnect(); disposed = true;
    }

    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellServiceProvider
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid iid, out IntPtr result);
    }

    [ComImport, Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationViewCollection
    {
        [PreserveSig] int GetViews(out IntPtr array);
        [PreserveSig] int GetViewsByZOrder(out IntPtr array);
        [PreserveSig] int GetViewsByAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr array);
        [PreserveSig] int GetViewForHwnd(IntPtr window, out IntPtr view);
    }

    [ComImport, Guid("4CE81583-1E4C-4632-A621-07A53543148F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopPinnedApps
    {
        [PreserveSig] int IsAppIdPinned([MarshalAs(UnmanagedType.LPWStr)] string id, out int pinned);
        [PreserveSig] int PinAppId([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int UnpinAppId([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int IsViewPinned(IntPtr view, out int pinned);
        [PreserveSig] int PinView(IntPtr view);
        [PreserveSig] int UnpinView(IntPtr view);
    }
}
