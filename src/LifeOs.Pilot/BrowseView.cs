using System.Text;
using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;

namespace LifeOs.Pilot;

/// <summary>
/// Browse — a three-pane navigator over the kernel: type tree, subject list, and a
/// detail pane (attributes, status history, concerning events, and navigable
/// serves/served-by edges). Reads go through <see cref="SubjectReader"/> (bsk_reader);
/// the New / Link / Change-status buttons write by shelling out to <see cref="BskCli"/>.
/// </summary>
public sealed class BrowseView : UserControl
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;

    private readonly TreeView _tree = new();
    private readonly DataGridView _grid = new();
    private readonly RichTextBox _detail = new();
    private readonly ListBox _edges = new();
    private readonly Label _detailHeader = new();
    private readonly Label _status = new();
    private readonly SplitContainer _outer = new() { Orientation = Orientation.Vertical };
    private readonly SplitContainer _inner = new() { Orientation = Orientation.Vertical };

    private readonly Button _refreshButton = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _newButton = new() { Text = "New…", AutoSize = true };
    private readonly Button _statusButton = new() { Text = "Change status…", AutoSize = true, Enabled = false };
    private readonly Button _linkButton = new() { Text = "Link…", AutoSize = true, Enabled = false };
    private readonly Button _datesButton = new() { Text = "Set dates…", AutoSize = true, Enabled = false };

    private Guid _currentId;
    private string? _currentUrn;
    private string? _currentTitle;
    private string? _currentDue;
    private string? _currentReview;
    private string? _currentCadence;

    public BrowseView(SubjectReader reader, BskCli? bsk)
    {
        _reader = reader;
        _bsk = bsk;

        BuildTree();
        BuildGrid();
        BuildDetailPane();

        _refreshButton.Click += (_, _) => LoadTypes();
        _newButton.Click += (_, _) => DoNew();
        _statusButton.Click += (_, _) => DoStatus();
        _linkButton.Click += (_, _) => DoLink();
        _datesButton.Click += (_, _) => DoDates();

        _status.AutoSize = true;
        _status.Padding = new Padding(10, 8, 0, 0);
        _status.ForeColor = SystemColors.GrayText;
        _status.Text = _bsk is null ? "Read-only (bsk.exe not found)" : "Ready";

        // Left pane stays narrow and fixed; the middle/detail split flexes.
        _outer.Dock = DockStyle.Fill;
        _outer.FixedPanel = FixedPanel.Panel1;
        _outer.Panel1MinSize = 60;
        _outer.Panel1.Controls.Add(_tree);
        _outer.Panel2.Controls.Add(_inner);

        _inner.Dock = DockStyle.Fill;

        var listToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        listToolbar.Controls.Add(_newButton);
        _inner.Panel1.Controls.Add(_grid);       // Fill first…
        _inner.Panel1.Controls.Add(listToolbar); // …Top last, so it reserves the strip above.

        // Dock order: the Fill control (outer) first, the Top banner last.
        Controls.Add(_outer);
        Controls.Add(BuildBanner());
    }

    private FlowLayoutPanel BuildBanner()
    {
        var banner = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        banner.Controls.Add(_refreshButton);
        banner.Controls.Add(_status);
        return banner;
    }

    private void BuildTree()
    {
        _tree.Dock = DockStyle.Fill;
        _tree.HideSelection = false;
        _tree.BorderStyle = BorderStyle.None;
        _tree.AfterSelect += OnTypeSelected;
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
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.BorderStyle = BorderStyle.None;
        _grid.ShowCellToolTips = true;
        _grid.SelectionChanged += OnSubjectSelected;
        _grid.CellToolTipTextNeeded += OnCellToolTip;
    }

    private void BuildDetailPane()
    {
        _detailHeader.Dock = DockStyle.Fill;
        _detailHeader.AutoEllipsis = true;
        _detailHeader.Font = new Font(Font, FontStyle.Bold);
        _detailHeader.TextAlign = ContentAlignment.MiddleLeft;
        _detailHeader.Text = "— select a subject —";

        var detailToolbar = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Margin = new Padding(0)
        };
        detailToolbar.Controls.Add(_statusButton);
        detailToolbar.Controls.Add(_linkButton);
        detailToolbar.Controls.Add(_datesButton);

        _detail.Dock = DockStyle.Fill;
        _detail.ReadOnly = true;
        _detail.BorderStyle = BorderStyle.None;
        _detail.Font = new Font(FontFamily.GenericMonospace, 9f);

        _edges.Dock = DockStyle.Fill;
        _edges.BorderStyle = BorderStyle.FixedSingle;
        _edges.IntegralHeight = false;
        _edges.DoubleClick += OnEdgeNavigate;

        var edgesLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Links — double-click to navigate",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText
        };

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(6) };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));  // header
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // toolbar — grows to the buttons
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // detail
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // links label
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 160)); // links list
        right.Controls.Add(_detailHeader, 0, 0);
        right.Controls.Add(detailToolbar, 0, 1);
        right.Controls.Add(_detail, 0, 2);
        right.Controls.Add(edgesLabel, 0, 3);
        right.Controls.Add(_edges, 0, 4);

        _inner.Panel2.Controls.Add(right);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        try
        {
            _outer.SplitterDistance = 150;  // narrow left pane
            _inner.SplitterDistance = 460;  // roomy middle list
        }
        catch (InvalidOperationException)
        {
            // Too narrow to honour the preferred split; leave the defaults.
        }

        LoadTypes();
    }

    /// <summary>Re-reads from the store, keeping the currently selected type in view.</summary>
    public void Reload()
    {
        var selectedType = _tree.SelectedNode?.Tag as string;
        LoadTypes();
        if (selectedType is not null)
        {
            SelectType(selectedType);
        }
    }

    private void LoadTypes()
    {
        try
        {
            var counts = _reader.GetTypeCounts();
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            var total = 0L;
            foreach (var count in counts)
            {
                total += count.N;
                _tree.Nodes.Add(new TreeNode($"{count.Type} ({count.N})") { Tag = count.Type });
            }

            _tree.EndUpdate();
            var mode = _bsk is null ? " · read-only" : "";
            _status.Text = $"Connected · {counts.Count} types · {total} subjects{mode}";
        }
        catch (Exception ex)
        {
            _status.Text = "Not connected";
            ShowError("Read failed", ex);
        }
    }

    private void OnTypeSelected(object? sender, TreeViewEventArgs e)
    {
        if (e.Node?.Tag is string type)
        {
            LoadSubjects(type);
        }
    }

    private void LoadSubjects(string type)
    {
        try
        {
            var subjects = _reader.GetSubjects(type).ToList();
            _grid.DataSource = subjects;
            ConfigureColumns(subjects);
            if (subjects.Count == 0)
            {
                ClearDetail();
            }
        }
        catch (Exception ex)
        {
            ShowError("Read failed", ex);
        }
    }

    // Title is the column that matters: sized to the widest title (capped), so it
    // reads without dragging. URN is narrow with a tooltip; the rest are compact.
    private void ConfigureColumns(IReadOnlyList<SubjectListItem> subjects)
    {
        if (_grid.Columns.Count == 0)
        {
            return;
        }

        if (_grid.Columns.Contains("Id"))
        {
            _grid.Columns["Id"]!.Visible = false;
            _grid.Columns["Id"]!.DisplayIndex = 6;
        }

        const int minTitle = 160;
        const int maxTitle = 440;
        var widest = TextRenderer.MeasureText("Title", _grid.Font).Width;
        foreach (var subject in subjects)
        {
            var width = TextRenderer.MeasureText(subject.Title, _grid.Font).Width;
            if (width > widest)
            {
                widest = width;
            }
        }

        var titleWidth = Math.Clamp(widest + 28, minTitle, maxTitle);

        SetColumn("Title", titleWidth, 0);
        SetColumn("Status", 80, 1);
        SetColumn("Due", 92, 2);
        SetColumn("ExpectedCadence", 90, 3, "Cadence");
        SetColumn("NextReviewAt", 96, 4, "Review");
        SetColumn("Urn", 92, 5);
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

    private void OnCellToolTip(object? sender, DataGridViewCellToolTipTextNeededEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        var name = _grid.Columns[e.ColumnIndex].Name;
        if (name is "Urn" or "Title")
        {
            e.ToolTipText = _grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value?.ToString() ?? "";
        }
    }

    private void OnSubjectSelected(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow?.DataBoundItem is SubjectListItem item)
        {
            LoadDetail(item.Id);
        }
    }

    private void LoadDetail(Guid id)
    {
        try
        {
            var subject = _reader.GetSubject(id);
            if (subject is null)
            {
                ClearDetail();
                return;
            }

            var serves = _reader.GetServes(id);
            var servedBy = _reader.GetServedBy(id);
            var concerns = _reader.GetConcerningEvents(id);
            var history = _reader.GetStatusHistory(id);

            var text = new StringBuilder();
            text.AppendLine(subject.Urn);
            text.AppendLine();
            if (!string.IsNullOrWhiteSpace(subject.Statement))
            {
                text.AppendLine("Statement");
                text.AppendLine($"  {subject.Statement}");
                text.AppendLine();
            }

            text.AppendLine($"Status    {subject.Status}");
            if (!string.IsNullOrWhiteSpace(subject.ExpectedCadence))
            {
                text.AppendLine($"Cadence   {subject.ExpectedCadence}");
            }

            if (subject.NextReviewAt is { } review)
            {
                text.AppendLine($"Review    {review:yyyy-MM-dd}");
            }

            if (!string.IsNullOrWhiteSpace(subject.Due))
            {
                text.AppendLine($"Due       {subject.Due}");
            }

            if (!string.IsNullOrWhiteSpace(subject.Scope))
            {
                text.AppendLine($"Scope     {subject.Scope}");
            }

            text.AppendLine($"Created   {subject.CreatedAt:yyyy-MM-dd}");
            text.AppendLine();
            text.AppendLine($"Concerning events ({concerns.Count})");
            foreach (var concern in concerns)
            {
                text.AppendLine($"  {concern.OccurredAt:yyyy-MM-dd}  {concern.Kind}");
            }

            if (concerns.Count == 0)
            {
                text.AppendLine("  (none)");
            }

            text.AppendLine();
            text.AppendLine($"Status history ({history.Count})");
            foreach (var entry in history)
            {
                text.AppendLine($"  {entry.OccurredAt:yyyy-MM-dd}  ->  {entry.Status}");
            }

            if (history.Count == 0)
            {
                text.AppendLine("  (none)");
            }

            _currentId = subject.Id;
            _currentUrn = subject.Urn;
            _currentTitle = subject.Title;
            _currentDue = subject.Due;
            _currentReview = subject.NextReviewAt?.ToString("yyyy-MM-dd");
            _currentCadence = subject.ExpectedCadence;
            _detailHeader.Text = $"{subject.Type} — {subject.Title}";
            _detail.Text = text.ToString();
            _statusButton.Enabled = _bsk is not null;
            _linkButton.Enabled = _bsk is not null;
            _datesButton.Enabled = _bsk is not null;

            _edges.BeginUpdate();
            _edges.Items.Clear();
            foreach (var edge in serves)
            {
                _edges.Items.Add(new EdgeItem(edge.SubjectId, $"serves ↑   {edge.Type}: {edge.Urn}"));
            }

            foreach (var edge in servedBy)
            {
                _edges.Items.Add(new EdgeItem(edge.SubjectId, $"served-by ↓   {edge.Type}: {edge.Urn}"));
            }

            if (_edges.Items.Count == 0)
            {
                _edges.Items.Add(new EdgeItem(Guid.Empty, "(no links)"));
            }

            _edges.EndUpdate();
        }
        catch (Exception ex)
        {
            ShowError("Read failed", ex);
        }
    }

    private void OnEdgeNavigate(object? sender, EventArgs e)
    {
        if (_edges.SelectedItem is EdgeItem edge && edge.TargetId != Guid.Empty)
        {
            LoadDetail(edge.TargetId);
        }
    }

    private void ClearDetail()
    {
        _currentId = Guid.Empty;
        _currentUrn = null;
        _currentTitle = null;
        _currentDue = null;
        _currentReview = null;
        _currentCadence = null;
        _detailHeader.Text = "— select a subject —";
        _detail.Clear();
        _edges.Items.Clear();
        _statusButton.Enabled = false;
        _linkButton.Enabled = false;
        _datesButton.Enabled = false;
    }

    // ---- writes (shell out to bsk) ----------------------------------------

    private void DoNew()
    {
        if (_bsk is null)
        {
            WarnNoBsk();
            return;
        }

        if (_tree.SelectedNode?.Tag is not string type)
        {
            MessageBox.Show(this, "Select a type in the left pane first.", "New",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // A Value is an identity statement: capture both the short handle (title)
        // and the full statement, and pass the statement through to the kernel.
        string[] args;
        if (type == "Value")
        {
            using var dialog = new ValueDialog();
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            args = ["new", "Value", dialog.HandleText, "--statement", dialog.Statement];
        }
        else
        {
            var title = InputDialog.Show(this, $"New {type}", $"Title for the new {type}:");
            if (title is null)
            {
                return;
            }

            args = ["new", type, title];
        }

        try
        {
            _bsk.Run(args);
            LoadTypes();
            SelectType(type);
        }
        catch (BskException ex)
        {
            ShowError("New failed", ex);
        }
    }

    private void DoStatus()
    {
        if (_bsk is null || _currentUrn is null)
        {
            return;
        }

        var status = InputDialog.Show(this, "Change status", $"New status for “{_currentTitle}”:");
        if (status is null)
        {
            return;
        }

        try
        {
            _bsk.Run("status", _currentUrn, status);
            _bsk.Run("rebuild"); // refresh the projection so the new status shows immediately
            var id = _currentId;
            if (_tree.SelectedNode?.Tag is string type)
            {
                LoadSubjects(type);
            }

            if (!SelectRowById(id))
            {
                LoadDetail(id);
            }
        }
        catch (BskException ex)
        {
            ShowError("Status change failed", ex);
        }
    }

    private void DoLink()
    {
        if (_bsk is null || _currentUrn is null)
        {
            return;
        }

        using var dialog = new LinkDialog(_currentTitle ?? _currentUrn);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Target.Length == 0)
        {
            return;
        }

        try
        {
            _bsk.Run("link", _currentUrn, dialog.Relation, dialog.Target);
            LoadDetail(_currentId);
        }
        catch (BskException ex)
        {
            ShowError("Link failed", ex);
        }
    }

    private void DoDates()
    {
        if (_bsk is null || _currentUrn is null)
        {
            return;
        }

        using var dialog = new AttributesDialog(
            _currentTitle ?? _currentUrn, _currentDue, _currentReview, _currentCadence);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Only send the keys that actually changed; a blank field clears its key.
        var args = new List<string> { "set", _currentUrn };
        AddIfChanged(args, "due", _currentDue, dialog.Due);
        AddIfChanged(args, "next_review_at", _currentReview, dialog.Review);
        AddIfChanged(args, "expected_cadence", _currentCadence, dialog.Cadence);
        if (args.Count == 2)
        {
            return; // nothing changed
        }

        try
        {
            _bsk.Run(args.ToArray());
            var id = _currentId;
            if (_tree.SelectedNode?.Tag is string type)
            {
                LoadSubjects(type);
            }

            if (!SelectRowById(id))
            {
                LoadDetail(id);
            }
        }
        catch (BskException ex)
        {
            ShowError("Update failed", ex);
        }
    }

    private static void AddIfChanged(List<string> args, string key, string? oldValue, string newValue)
    {
        if ((oldValue ?? string.Empty) != newValue)
        {
            args.Add($"{key}={newValue}");
        }
    }

    private void SelectType(string type)
    {
        foreach (TreeNode node in _tree.Nodes)
        {
            if (node.Tag is string tag && tag == type)
            {
                _tree.SelectedNode = node;
                return;
            }
        }
    }

    private bool SelectRowById(Guid id)
    {
        var firstVisible = FirstVisibleColumnIndex();
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is SubjectListItem item && item.Id == id)
            {
                _grid.CurrentCell = row.Cells[firstVisible];
                return true;
            }
        }

        return false;
    }

    private int FirstVisibleColumnIndex()
    {
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            if (column.Visible)
            {
                return column.Index;
            }
        }

        return 0;
    }

    private void WarnNoBsk()
        => MessageBox.Show(this,
            "bsk.exe was not found, so writes are disabled. Build the solution (./run.ps1) or set BSK_EXE.",
            "Writes unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void ShowError(string caption, Exception ex)
        => MessageBox.Show(this, ex.Message, caption, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    /// <summary>A navigable link in the detail pane: its display text and the subject it points at.</summary>
    private sealed class EdgeItem(Guid targetId, string display)
    {
        public Guid TargetId { get; } = targetId;

        private string Display { get; } = display;

        public override string ToString() => Display;
    }
}
