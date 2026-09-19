using System.Runtime.InteropServices;

namespace LifeOs.Pilot.Shell;

/// <summary>
/// Tints the Windows 11 title bar (caption background + text) so the running
/// environment is obvious from the OS chrome, not only the client area. A no-op
/// on Windows older than 11 (build 22000), where the DWM attributes are ignored.
/// </summary>
internal static class TitleBarTint
{
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public static void Apply(Form form, Color caption, Color text)
    {
        if (!form.IsHandleCreated)
        {
            return;
        }

        var captionRef = ToColorRef(caption);
        var textRef = ToColorRef(text);
        try
        {
            _ = DwmSetWindowAttribute(form.Handle, DwmwaCaptionColor, ref captionRef, sizeof(int));
            _ = DwmSetWindowAttribute(form.Handle, DwmwaTextColor, ref textRef, sizeof(int));
        }
        catch (DllNotFoundException)
        {
            // dwmapi.dll absent (not expected on Windows 10+); leave the default chrome.
        }
    }

    // COLORREF is 0x00BBGGRR.
    private static int ToColorRef(Color color) => color.R | (color.G << 8) | (color.B << 16);

    [DllImport("dwmapi.dll", SetLastError = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
