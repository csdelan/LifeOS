using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot;

/// <summary>
/// Inbox — the clarify step. Lists items flagged for triage (INBOX-1's <c>v_inbox</c>:
/// captured notes plus Ideas/Problems flagged on creation) and resolves each with a
/// GTD decision on two axes (attention vs. status, 0021): Promote it into tracked work,
/// Relate a capture to the subject it concerns, Dismiss it (attention-only clear, no
/// status change), or Drop it (a subject's Status → its terminal "it's nothing"). Drop
/// is subject-only; an event is cleared with Dismiss. Tagging/relating alone do not
/// resolve (INBOX-4) — only Promote / Dismiss / Drop take an item out of the Inbox.
/// Reads go through <see cref="SubjectReader"/>; writes shell out to <see cref="BskCli"/>.
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
    private readonly Button _dismissButton = new() { Text = "Dismiss", AutoSize = true, Enabled = false };
    private readonly Button _dropButton = new() { Text = "Drop…", AutoSize = true, Enabled = false };

    private InboxItem? _current;

    public InboxView(SubjectReader reader, BskCli? bsk)
    {
        _reader = reader;
        _bsk = bsk;

        BuildGrid();
        Ui.SelectRowOnRightClick(_grid);
        _grid.ContextMenuStrip = BuildProblemIdeaMenu();

        _content.Dock = DockStyle.Fill;
        _content.ReadOnly = true;
        _content.BorderStyle = BorderStyle.None;
        _content.Font = new Font(FontFamily.GenericMonospace, 9.5f);

        _newButton.Click += (_, _) => DoNewNote();
        _refreshButton.Click += (_, _) => LoadInbox();
        _promoteButton.Click += (_, _) => DoPromote();
        _relateButton.Click += (_, _) => DoRelate();
        _dismissButton.Click += (_, _) => DoResolve("dismiss", "Dismiss");
        _dropButton.Click += (_, _) => DoDrop();

        _status.AutoSize = true;
        _status.Padding = new Padding(10, 8, 0, 0);
        _status.ForeColor = SystemColors.GrayText;

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            Margin = new Padding(0)
        };
        actions.Controls.Add(_promoteButton);
        actions.Controls.Add(_relateButton);
        actions.Controls.Add(_dismissButton);
        actions.Controls.Add(_dropButton);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(6, 6, 16, 6) };
        right.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.Controls.Add(_content, 0, 0);
        right.Controls.Add(actions, 0, 1);

        _split.Dock = DockStyle.Fill;
        _split.Panel1.Controls.Add(_grid);
        _split.Panel2.Controls.Add(right);
        Ui.PrepareListDetailSplit(_split, setDistance: false);

        var banner = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        banner.Controls.Add(_newButton);
        banner.Controls.Add(_refreshButton);
        banner.Controls.Add(_status);

        Controls.Add(_split);   // Fill first…
        Controls.Add(banner);   // …Top last.
    }

    private void BuildGrid()
    {
        Ui.ConfigureGrid(_grid);
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

        Ui.HideAllColumns(_grid);
        SetColumn("Kind", 84, 0);
        SetColumn("TriagedAt", 130, 1, "When");
        SetColumn("Preview", 360, 2);
    }

    private void SetColumn(string name, int width, int displayIndex, string? header = null)
        => Ui.ShowColumn(_grid, name, width, displayIndex, header);

    private void OnItemSelected(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow?.DataBoundItem is InboxItem item)
        {
            _current = item;
            _content.Text = item.Content;
            var canWrite = _bsk is not null;
            _promoteButton.Enabled = canWrite;
            _relateButton.Enabled = canWrite && item.IsEvent; // relate a capture to a subject
            _dismissButton.Enabled = canWrite; // attention-only clear, for subjects and events
            // Drop is a Status verb (sets a terminal status), so only for subjects; an
            // event has no status and is cleared with Dismiss instead (0021).
            _dropButton.Enabled = canWrite && !item.IsEvent;
        }
    }

    private void ClearSelection()
    {
        _current = null;
        _content.Clear();
        _promoteButton.Enabled = false;
        _relateButton.Enabled = false;
        _dismissButton.Enabled = false;
        _dropButton.Enabled = false;
    }

    private ContextMenuStrip BuildProblemIdeaMenu()
    {
        var menu = new ContextMenuStrip();
        var newIdea = new ToolStripMenuItem("New idea…");
        newIdea.Click += (_, _) => DoNewIdeaFromProblem();
        menu.Items.Add(newIdea);
        menu.Opening += (_, e) =>
        {
            e.Cancel = _bsk is null || CurrentProblem() is null;
        };
        return menu;
    }

    private InboxItem? CurrentProblem()
        => _grid.CurrentRow?.DataBoundItem is InboxItem { IsEvent: false, SubjectType: PilotVocab.Problem } item
            && !string.IsNullOrWhiteSpace(item.SubjectUrn)
            ? item
            : null;

    private void DoNewIdeaFromProblem()
    {
        if (_bsk is null)
        {
            WarnNoBsk();
            return;
        }

        if (CurrentProblem() is not { } problem || problem.SubjectUrn is null)
        {
            return;
        }

        var subject = _reader.GetSubjectByUrn(problem.SubjectUrn);
        var created = NewSubjectDialog.ShowIdeaForProblem(
            this, _reader, _bsk,
            problem.SubjectUrn,
            problem.SubjectTitle ?? problem.Content,
            subject?.Area);
        if (created is null)
        {
            return;
        }

        LoadInbox();
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
        // For a subject, Drop sets its Status to the type's dismiss terminal (0021); name
        // that status so the user sees the resolution being recorded, not a silent removal.
        var dismissStatus = _current.IsEvent ? "" : PilotVocab.DismissStatusFor(_current.SubjectType ?? "");
        var prompt = dismissStatus.Length > 0
            ? $"Drop this {_current.Kind}? Its status will be set to \"{dismissStatus}\" and it leaves the Inbox."
            : "Drop this item out of the Inbox? It is kept in history but marked resolved.";

        var confirm = MessageBox.Show(
            this, prompt, "Confirm Drop", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        Write("Drop failed", () =>
        {
            _bsk.Run("drop", _current.Ref);
            // A subject Drop is a status change; fold it into subject_current so Browse and
            // the other views show the new status (the Inbox itself reads a live view).
            if (dismissStatus.Length > 0)
            {
                _bsk.Run("rebuild");
            }
        });
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
