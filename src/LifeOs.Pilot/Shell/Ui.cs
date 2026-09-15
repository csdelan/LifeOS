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
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.BorderStyle = BorderStyle.None;
        grid.ShowCellToolTips = true;
        grid.ScrollBars = ScrollBars.Vertical;
    }

    public static void HideAllColumns(DataGridView grid)
    {
        foreach (DataGridViewColumn column in grid.Columns)
        {
            column.Visible = false;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.FillWeight = 1;
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
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        column.FillWeight = Math.Max(width, 40);
        column.MinimumWidth = Math.Clamp(width / 3, 40, 80);
        column.DisplayIndex = displayIndex;
        if (header is not null)
        {
            column.HeaderText = header;
        }
    }

    /// <summary>
    /// Windows 11 visual styles draw TabControl headers as unmarked text.
    /// Owner-draw gives each tab a border and a selected highlight.
    /// </summary>
    public static void ConfigureTabs(TabControl tabs)
    {
        tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        tabs.SizeMode = TabSizeMode.Normal;
        tabs.ItemSize = new Size(1, 28);
        tabs.Padding = new Point(14, 6);
        tabs.HotTrack = true;
        tabs.DrawItem -= DrawTabHeader;
        tabs.DrawItem += DrawTabHeader;
    }

    private static void DrawTabHeader(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabCount)
        {
            return;
        }

        var selected = e.Index == tabs.SelectedIndex;
        var bounds = e.Bounds;
        bounds.Inflate(-2, 0);
        if (!selected)
        {
            bounds.Y += 3;
            bounds.Height -= 3;
        }

        using (var fill = new SolidBrush(selected ? SystemColors.Window : SystemColors.Control))
        {
            e.Graphics.FillRectangle(fill, bounds);
        }

        using (var border = new Pen(selected ? SystemColors.ControlDarkDark : SystemColors.ControlDark))
        {
            e.Graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);
        }

        if (selected)
        {
            using var accent = new Pen(SystemColors.Highlight, 3);
            e.Graphics.DrawLine(accent, bounds.Left + 1, bounds.Top + 2, bounds.Right - 2, bounds.Top + 2);
        }

        Font? bold = null;
        var font = tabs.Font;
        if (selected)
        {
            bold = new Font(tabs.Font, FontStyle.Bold);
            font = bold;
        }

        TextRenderer.DrawText(
            e.Graphics,
            tabs.TabPages[e.Index].Text,
            font,
            bounds,
            SystemColors.ControlText,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        bold?.Dispose();
    }

    /// <summary>
    /// Right-hand detail gets padding so header/buttons are not clipped, and the
    /// splitter opens with a usable list/detail split on first layout.
    /// Min sizes are deferred: a SplitContainer's default width is ~150px, so
    /// assigning them in a constructor throws <see cref="InvalidOperationException"/>.
    /// </summary>
    public static void PrepareListDetailSplit(SplitContainer split, float listShare = 0.52f, bool setDistance = true)
    {
        const int panel1Min = 200;
        const int panel2Min = 260;

        split.SplitterWidth = Math.Max(split.SplitterWidth, 6);

        void ApplyWhenWideEnough(object? sender, EventArgs e)
        {
            var usable = split.Width - split.SplitterWidth;
            if (usable < panel1Min + panel2Min)
            {
                return;
            }

            split.Layout -= ApplyWhenWideEnough;
            split.SizeChanged -= ApplyWhenWideEnough;

            try
            {
                // SplitterDistance must sit between the new mins *before* they are applied.
                var distance = setDistance
                    ? (int)(usable * listShare)
                    : split.SplitterDistance;
                split.SplitterDistance = (int)Math.Clamp(distance, panel1Min, usable - panel2Min);
                split.Panel1MinSize = panel1Min;
                split.Panel2MinSize = panel2Min;
            }
            catch (InvalidOperationException)
            {
                split.Layout += ApplyWhenWideEnough;
                split.SizeChanged += ApplyWhenWideEnough;
            }
        }

        split.Layout += ApplyWhenWideEnough;
        split.SizeChanged += ApplyWhenWideEnough;
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

    /// <summary>OK/Cancel confirmation for Archive and Restore (D9).</summary>
    public static bool ConfirmArchive(IWin32Window owner, string label, bool restoring)
    {
        var caption = restoring ? "Restore" : "Archive";
        var body = restoring
            ? $"Restore “{label}”? It will show up in default views again."
            : $"Archive “{label}”? It will be hidden from default views. You can restore it later with the Archived filter.";
        return MessageBox.Show(owner, body, caption, MessageBoxButtons.OKCancel, MessageBoxIcon.Question)
            == DialogResult.OK;
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
