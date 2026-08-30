using System.Runtime.InteropServices;
using System.Text.Json;

namespace LifeOs.Pilot;

/// <summary>
/// Persists a form's exact on-screen placement — position, size, and
/// maximized/normal state — across runs, using the Win32
/// GetWindowPlacement/SetWindowPlacement pair.
///
/// Placement is stored in the virtual-desktop coordinate space, so the monitor
/// the window was on is remembered as part of the coordinates (no separate
/// "which display" bookkeeping needed). SetWindowPlacement also clamps a window
/// back onto a connected monitor when the saved display is gone, so restoring
/// stays safe after a monitor is unplugged or the desktop layout changes.
/// </summary>
internal static class WindowPlacement
{
    private const int SwShowNormal = 1;
    private const int SwShowMinimized = 2;

    /// <summary>Loads the saved placement (if any) and applies it to <paramref name="form"/>.</summary>
    /// <returns><c>true</c> if a placement was found and applied.</returns>
    public static bool Restore(Form form, string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var dto = JsonSerializer.Deserialize<PlacementDto>(File.ReadAllText(path));
            if (dto is null)
            {
                return false;
            }

            var native = new NativePlacement
            {
                Length = Marshal.SizeOf<NativePlacement>(),
                Flags = dto.Flags,
                // Never relaunch minimized — reopen in the window's restore state instead.
                ShowCmd = dto.ShowCmd == SwShowMinimized ? SwShowNormal : dto.ShowCmd,
                MinPosition = new Point(dto.MinX, dto.MinY),
                MaxPosition = new Point(dto.MaxX, dto.MaxY),
                NormalPosition = new Rect(dto.Left, dto.Top, dto.Right, dto.Bottom),
            };

            return SetWindowPlacement(form.Handle, ref native);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable placement file must never block startup;
            // fall back to the form's default StartPosition.
            return false;
        }
    }

    /// <summary>Captures <paramref name="form"/>'s current placement and writes it to <paramref name="path"/>.</summary>
    public static void Save(Form form, string path)
    {
        try
        {
            var native = new NativePlacement { Length = Marshal.SizeOf<NativePlacement>() };
            if (!GetWindowPlacement(form.Handle, ref native))
            {
                return;
            }

            var dto = new PlacementDto(
                native.Flags,
                native.ShowCmd,
                native.MinPosition.X,
                native.MinPosition.Y,
                native.MaxPosition.X,
                native.MaxPosition.Y,
                native.NormalPosition.Left,
                native.NormalPosition.Top,
                native.NormalPosition.Right,
                native.NormalPosition.Bottom);

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(dto));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort persistence; failing to save placement should not crash a clean shutdown.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref NativePlacement placement);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref NativePlacement placement);

    /// <summary>Serializable snapshot of the fields we round-trip through the WINDOWPLACEMENT struct.</summary>
    private sealed record PlacementDto(
        int Flags,
        int ShowCmd,
        int MinX,
        int MinY,
        int MaxX,
        int MaxY,
        int Left,
        int Top,
        int Right,
        int Bottom);

    // Mirrors the Win32 WINDOWPLACEMENT / POINT / RECT layouts exactly.
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public Point MinPosition;
        public Point MaxPosition;
        public Rect NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect(int left, int top, int right, int bottom)
    {
        public int Left = left;
        public int Top = top;
        public int Right = right;
        public int Bottom = bottom;
    }
}
