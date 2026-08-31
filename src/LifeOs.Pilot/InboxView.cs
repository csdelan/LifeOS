using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;

namespace LifeOs.Pilot;

/// <summary>
/// Inbox — the clarify step. Lists untriaged captures (notes/journals not yet promoted
/// or related) and lets you act on each: promote it into a tracked subject, or relate
/// it to the subject it concerns. Acting on a capture removes it from the list. Reads
/// go through <see cref="SubjectReader"/>; writes shell out to <see cref="BskCli"/>.
/// </summary>
public sealed class InboxView : UserControl
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;

    private readonly SplitContainer _split = new() { Orientation = Orientation.Vertical };
    private readonly DataGridView _grid = new();
    private readonly RichTextBox _content = new();
    private readonly Label _status = new();
    private readonly Button _newButton = new() { Text = "New note…", AutoSize = true };
    private readonly Button _refreshButton = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _promoteButton = new() { Text = "Promote…", AutoSize = true, Enabled = false };
    private readonly Button _relateButton = new() { Text = "Relate to…", AutoSize = true, Enabled = false };

    private Guid _currentCaptureId;

    public InboxView(SubjectReader reader, BskCli? bsk)
    {
        _reader = reader;
        _bsk = bsk;

        BuildGrid();

        _content.Dock = DockStyle.Fill;
        _content.ReadOnly = true;
        _content.BorderStyle = BorderStyle.None;
        _content.Font = new Font(FontFamily.GenericMonospace, 9.5f);

        _newButton.Click += (_, _) => DoNewNote();
        _refreshButton.Click += (_, _) => LoadCaptures();
        _promoteButton.Click += (_, _) => DoPromote();
        _relateButton.Click += (_, _) => DoRelate();

        _status.AutoSize = true;
        _status.Padding = new Padding(10, 8, 0, 0);
        _status.ForeColor = SystemColors.GrayText;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0)
        };
        actions.Controls.Add(_promoteButton);
        actions.Controls.Add(_relateButton);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(6) };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.Controls.Add(_content, 0, 0);
        right.Controls.Add(actions, 0, 1);

        _split.Dock = DockStyle.Fill;
        _split.Panel1.Controls.Add(_grid);
        _split.Panel2.Controls.Add(right);

        var banner = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        banner.Controls.Add(_newButton);
        banner.Controls.Add(_refreshButton);
        banner.Controls.Add(_status);

        Controls.Add(_split);   // Fill first…
        Controls.Add(banner);   // …Top last.
    }

    private void BuildGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.BorderStyle = BorderStyle.None;
        _grid.SelectionChanged += OnCaptureSelected;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        try
        {
            _split.SplitterDistance = 440;
        }
        catch (InvalidOperationException)
        {
            // Window too small to honour the preferred split; leave the default.
        }

        LoadCaptures();
    }

    /// <summary>Re-reads the untriaged captures — called when the Inbox tab is shown.</summary>
    public void Reload() => LoadCaptures();

    private void LoadCaptures()
    {
        try
        {
            var captures = _reader.GetUnprocessedCaptures().ToList();
            _grid.DataSource = captures;
            ConfigureColumns();
            if (captures.Count == 0)
            {
                ClearSelection();
            }

            _status.Text = _bsk is null
                ? $"{captures.Count} unprocessed · read-only (bsk.exe not found)"
                : $"{captures.Count} unprocessed capture(s)";
        }
        catch (Exception ex)
        {
            ShowError("Read failed", ex);
        }
    }

    private void ConfigureColumns()
    {
        if (_grid.Columns.Count == 0)
        {
            return;
        }

        foreach (var hidden in new[] { "Id", "Content" })
        {
            if (_grid.Columns.Contains(hidden))
            {
                _grid.Columns[hidden]!.Visible = false;
            }
        }

        SetColumn("Kind", 72, 0);
        SetColumn("OccurredAt", 130, 1, "When");
        SetColumn("Preview", 380, 2);
    }

    private void SetColumn(string name, int width, int displayIndex, string? header = null)
    {
        if (!_grid.Columns.Contains(name))
        {
            return;
        }

        var column = _grid.Columns[name]!;
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        column.Width = width;
        column.DisplayIndex = displayIndex;
        if (header is not null)
        {
            column.HeaderText = header;
        }
    }

    private void OnCaptureSelected(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow?.DataBoundItem is CaptureItem item)
        {
            _currentCaptureId = item.Id;
            _content.Text = item.Content ?? "";
            _promoteButton.Enabled = _bsk is not null;
            _relateButton.Enabled = _bsk is not null;
        }
    }

    private void ClearSelection()
    {
        _currentCaptureId = Guid.Empty;
        _content.Clear();
        _promoteButton.Enabled = false;
        _relateButton.Enabled = false;
    }

    private void DoNewNote()
    {
        if (_bsk is null)
        {
            WarnNoBsk();
            return;
        }

        var text = InputDialog.Show(this, "New note", "Capture a note:");
        if (text is null)
        {
            return;
        }

        try
        {
            _bsk.Run("capture", text);
            LoadCaptures();
        }
        catch (BskException ex)
        {
            ShowError("Capture failed", ex);
        }
    }

    private void DoPromote()
    {
        if (_bsk is null || _currentCaptureId == Guid.Empty)
        {
            return;
        }

        using var dialog = new PromoteDialog(FirstLine(_content.Text));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.TitleText.Length == 0)
        {
            return;
        }

        try
        {
            _bsk.Run("promote", _currentCaptureId.ToString(), dialog.SubjectType, dialog.TitleText);
            LoadCaptures();
        }
        catch (BskException ex)
        {
            ShowError("Promote failed", ex);
        }
    }

    private void DoRelate()
    {
        if (_bsk is null || _currentCaptureId == Guid.Empty)
        {
            return;
        }

        var target = InputDialog.Show(
            this, "Relate to subject", "Subject this capture concerns (title, URN, or short id):");
        if (target is null)
        {
            return;
        }

        try
        {
            _bsk.Run("relate", _currentCaptureId.ToString(), target);
            LoadCaptures();
        }
        catch (BskException ex)
        {
            ShowError("Relate failed", ex);
        }
    }

    private static string FirstLine(string text)
    {
        var line = text.ReplaceLineEndings("\n").Split('\n', 2)[0].Trim();
        return line.Length > 80 ? line[..80] : line;
    }

    private void WarnNoBsk()
        => MessageBox.Show(this,
            "bsk.exe was not found, so writes are disabled. Build the solution (./run.ps1) or set BSK_EXE.",
            "Writes unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void ShowError(string caption, Exception ex)
        => MessageBox.Show(this, ex.Message, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
