using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot;

/// <summary>
/// Inbox — the clarify step. Lists items flagged for triage (INBOX-1's <c>v_inbox</c>:
/// captured notes plus Ideas/Problems flagged on creation) and resolves each with a
/// GTD decision: Promote it into tracked work, Relate a capture to the subject it
/// concerns, File it as reference, or Drop it. Tagging/relating alone do not resolve
/// (INBOX-4) — only Promote / File / Drop take an item out of the Inbox. Reads go
/// through <see cref="SubjectReader"/>; writes shell out to <see cref="BskCli"/>.
/// </summary>
internal sealed class InboxView : UserControl, IPilotView
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
    private readonly Button _fileButton = new() { Text = "File", AutoSize = true, Enabled = false };
    private readonly Button _dropButton = new() { Text = "Drop…", AutoSize = true, Enabled = false };

    private InboxItem? _current;

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
        _refreshButton.Click += (_, _) => LoadInbox();
        _promoteButton.Click += (_, _) => DoPromote();
        _relateButton.Click += (_, _) => DoRelate();
        _fileButton.Click += (_, _) => DoResolve("file", "File");
        _dropButton.Click += (_, _) => DoDrop();

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
        actions.Controls.Add(_fileButton);
        actions.Controls.Add(_dropButton);

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
        _grid.SelectionChanged += OnItemSelected;
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

        LoadInbox();
    }

    /// <summary>Re-reads the inbox — called when the Inbox tab is shown.</summary>
    public void Reload() => LoadInbox();

    public bool IsDirty => false;

    public bool TrySaveEdits() => true;

    public void DiscardEdits()
    {
    }

    public void SelectSubject(Guid id)
    {
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is InboxItem item && !item.IsEvent
                && _reader.GetSubjectByUrn(item.SubjectUrn ?? "")?.Id == id)
            {
                var visible = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
                if (visible is not null)
                {
                    _grid.CurrentCell = row.Cells[visible.Index];
                }

                return;
            }
        }
    }

    public void SaveViewState(ViewStateStore store)
    {
    }

    public void RestoreViewState(ViewStateStore store)
    {
    }

    private void LoadInbox()
    {
        try
        {
            var items = _reader.GetInbox().ToList();
            _grid.DataSource = items;
            ConfigureColumns();
            if (items.Count == 0)
            {
                ClearSelection();
            }

            _status.Text = _bsk is null
                ? $"{items.Count} to triage · read-only (bsk.exe not found)"
                : items.Count == 0 ? "Inbox Zero ✓" : $"{items.Count} item(s) to triage";
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

        // Show only Kind / When / Preview; hide every other bound + computed property.
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.Visible = false;
        }

        SetColumn("Kind", 84, 0);
        SetColumn("TriagedAt", 130, 1, "When");
        SetColumn("Preview", 360, 2);
    }

    private void SetColumn(string name, int width, int displayIndex, string? header = null)
    {
        if (!_grid.Columns.Contains(name))
        {
            return;
        }

        var column = _grid.Columns[name]!;
        column.Visible = true;
        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
        column.Width = width;
        column.DisplayIndex = displayIndex;
        if (header is not null)
        {
            column.HeaderText = header;
        }
    }

    private void OnItemSelected(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow?.DataBoundItem is InboxItem item)
        {
            _current = item;
            _content.Text = item.Content;
            var canWrite = _bsk is not null;
            _promoteButton.Enabled = canWrite;
            _relateButton.Enabled = canWrite && item.IsEvent; // relate a capture to a subject
            _fileButton.Enabled = canWrite;
            _dropButton.Enabled = canWrite;
        }
    }

    private void ClearSelection()
    {
        _current = null;
        _content.Clear();
        _promoteButton.Enabled = false;
        _relateButton.Enabled = false;
        _fileButton.Enabled = false;
        _dropButton.Enabled = false;
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

        Write("Capture failed", () => _bsk.Run("capture", text));
    }

    private void DoPromote()
    {
        if (_bsk is null || _current is null)
        {
            return;
        }

        using var dialog = new PromoteDialog(FirstLine(_content.Text));
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.TitleText.Length == 0)
        {
            return;
        }

        // `bsk promote` dispatches on the source: an event id promotes the capture into
        // a subject; a subject ref (an Idea) promotes it into new work. Either resolves
        // the item out of the inbox.
        Write("Promote failed", () => _bsk.Run("promote", _current.Ref, dialog.SubjectType, dialog.TitleText));
    }

    private void DoRelate()
    {
        if (_bsk is null || _current is null || !_current.IsEvent)
        {
            return;
        }

        var target = InputDialog.Show(
            this, "Relate to subject", "Subject this capture concerns (title, URN, or short id):");
        if (target is null)
        {
            return;
        }

        // Relating organizes but does NOT resolve (INBOX-4): file/drop still needed.
        Write("Relate failed", () => _bsk.Run("relate", _current.ItemId.ToString(), target));
    }

    private void DoResolve(string verb, string caption)
    {
        if (_bsk is null || _current is null)
        {
            return;
        }

        Write($"{caption} failed", () => _bsk.Run(verb, _current.Ref));
    }

    private void DoDrop()
    {
        if (_bsk is null || _current is null)
        {
            return;
        }

        // Drop requires confirmation (INBOX-4) — it is a deliberate "this is nothing".
        var confirm = MessageBox.Show(
            this, "Drop this item out of the Inbox? It is kept in history but marked resolved.",
            "Confirm Drop", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        Write("Drop failed", () => _bsk.Run("drop", _current.Ref));
    }

    // Runs a write, refreshes the list on success, and surfaces a clean error otherwise.
    private void Write(string caption, Action write)
    {
        try
        {
            write();
            LoadInbox();
        }
        catch (BskException ex)
        {
            ShowError(caption, ex);
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
