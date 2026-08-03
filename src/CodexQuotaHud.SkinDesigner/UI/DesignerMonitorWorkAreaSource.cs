using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;

namespace CodexQuotaHud.SkinDesigner.UI;

internal sealed record DesignerMonitorMetrics(Rect WorkAreaDip, DpiScale Dpi);

internal interface IDesignerMonitorWorkAreaSource
{
    DesignerMonitorMetrics GetCurrent(Window window);
}

internal sealed class WindowsDesignerMonitorWorkAreaSource :
    IDesignerMonitorWorkAreaSource
{
    private const uint MonitorDefaultToNearest = 2;

    public DesignerMonitorMetrics GetCurrent(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var dpi = VisualTreeHelper.GetDpi(window);
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return new DesignerMonitorMetrics(SystemParameters.WorkArea, dpi);
        }

        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var information = new MonitorInformation
        {
            Size = Marshal.SizeOf<MonitorInformation>()
        };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref information))
        {
            return new DesignerMonitorMetrics(SystemParameters.WorkArea, dpi);
        }

        var work = information.WorkArea;
        return new DesignerMonitorMetrics(
            ToDipWorkArea(
                work.Left,
                work.Top,
                work.Right,
                work.Bottom,
                dpi),
            dpi);
    }

    internal static Rect ToDipWorkArea(
        int left,
        int top,
        int right,
        int bottom,
        DpiScale dpi) =>
        new(
            left / dpi.DpiScaleX,
            top / dpi.DpiScaleY,
            (right - left) / dpi.DpiScaleX,
            (bottom - top) / dpi.DpiScaleY);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(
        IntPtr window,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        IntPtr monitor,
        ref MonitorInformation information);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInformation
    {
        internal int Size;
        internal NativeRect Monitor;
        internal NativeRect WorkArea;
        internal uint Flags;
    }
}
