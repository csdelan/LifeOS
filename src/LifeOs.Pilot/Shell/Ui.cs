using LifeOs.Pilot.Cli;

namespace LifeOs.Pilot.Shell;

/// <summary>Shared WinForms helpers so each screen does not reinvent grids, errors, and dates.</summary>
internal static class Ui
{
    public static void ConfigureGrid(DataGridView grid)
    {
        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.RowHeadersVisible = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.BorderStyle = BorderStyle.None;
        grid.ShowCellToolTips = true;
    }

    public static void HideAllColumns(DataGridView grid)
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.Visible = false;
        }
    }

    public static void ShowColumn(DataGridView grid, string name, int width, int displayIndex, string? header = null)
    {
        if (!grid.Columns.Contains(name))
        {
            return;
        }

        var column = grid.Columns[name]!;
        column.Visible = true;
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        column.Width = width;
        column.DisplayIndex = displayIndex;
        if (header is not null)
        {
            column.HeaderText = header;
        }
    }

    public static void WarnNoBsk(IWin32Window owner)
        => MessageBox.Show(owner,
            "bsk.exe was not found, so writes are disabled. Build the solution (./run.ps1) or set BSK_EXE.",
            "Writes unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static void ShowError(IWin32Window owner, string caption, Exception ex)
        => MessageBox.Show(owner, ex.Message, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static Label Muted(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(10, 8, 0, 0)
        };

    public static Label Heading(string text)
        => new()
        {
            Text = text,
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 8, 0, 4)
        };

    public static Button Action(string text)
        => new() { Text = text, AutoSize = true };

    public static FlowLayoutPanel Toolbar()
        => new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(6)
        };

    public static bool TryParseDate(string? text, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        return DateOnly.TryParse(text.Trim(), out date);
    }

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    public static string TodayIso => Today.ToString("yyyy-MM-dd");

    /// <summary>
    /// NAV-1 dirty-nav prompt. Returns true when navigation may proceed.
    /// Save runs <paramref name="save"/>; a failed save stays put.
    /// </summary>
    public static bool ConfirmLeave(IWin32Window owner, IPilotView view)
    {
        if (!view.IsDirty)
        {
            return true;
        }

        var result = MessageBox.Show(
            owner,
            "This screen has unsaved edits. Save them before leaving?",
            "Unsaved edits",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Question);

        switch (result)
        {
            case DialogResult.Yes:
                return view.TrySaveEdits();
            case DialogResult.No:
                view.DiscardEdits();
                return true;
            default:
                return false;
        }
    }

    public static void RunWrite(IWin32Window owner, BskCli? bsk, string caption, Action<BskCli> write)
    {
        if (bsk is null)
        {
            WarnNoBsk(owner);
            return;
        }

        try
        {
            write(bsk);
        }
        catch (BskException ex)
        {
            ShowError(owner, caption, ex);
        }
    }
}
