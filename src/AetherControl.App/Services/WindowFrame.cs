using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace AetherControl.App.Services;

/// <summary>
/// A resizable window's rect includes invisible resize borders that Windows draws inside it.
/// Sizing such a window to a display's work area therefore leaves visible gaps. Measured on the
/// test rig's portrait panel (2026-11-14, 125% scale): window = work area 768x1306, visible frame
/// 752x1298, i.e. 8 px short on the left, right and bottom. <see cref="Inflate"/> grows a target
/// rect by those borders so the *visible* window covers it exactly.
/// </summary>
internal static partial class WindowFrame
{
    private const int DwmwaExtendedFrameBounds = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);

    /// <summary><paramref name="target"/> grown by the window's invisible borders (unchanged if
    /// they can't be measured). Physical pixels, same units as AppWindow.</summary>
    public static RectInt32 Inflate(AppWindow window, RectInt32 target)
    {
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(window.Id);
        if (!GetWindowRect(hwnd, out var outer) ||
            DwmGetWindowAttribute(hwnd, DwmwaExtendedFrameBounds, out var visible, Marshal.SizeOf<Rect>()) != 0)
        {
            return target;
        }

        // Clamped: a minimised or off-screen window can report nonsense; real borders are a few px.
        int left = Math.Clamp(visible.Left - outer.Left, 0, 32), top = Math.Clamp(visible.Top - outer.Top, 0, 32);
        int right = Math.Clamp(outer.Right - visible.Right, 0, 32), bottom = Math.Clamp(outer.Bottom - visible.Bottom, 0, 32);
        return new RectInt32(target.X - left, target.Y - top, target.Width + left + right, target.Height + top + bottom);
    }
}
