using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Controls;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot;

/// <summary>
/// Browse — three-pane navigator: type tree, subject list, BROWSE-2 detail.
/// Dedicated tabs and Browse presets are two views of the same subjects.
/// </summary>
internal sealed class BrowseView : UserControl, IPilotView
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly TreeView _tree = new();
    private readonly DataGridView _grid = new();
    private readonly DetailPane _detail;
    private readonly Label _status = Ui.Muted("");
    private readonly CheckBox _archived = new() { Text = "Archived", AutoSize = true };
    private readonly SplitContainer _outer = new() { Orientation = Orientation.Vertical };
    private readonly SplitContainer _inner = new() { Orientation = Orientation.Vertical };
    private Guid _pendingSelect;
    private bool _restored;

    public BrowseView(SubjectReader reader, BskCli? bsk, Navigator navigator)
    {
        _reader = reader;
        _bsk = bsk;
        _detail = new DetailPane(reader, bsk, navigator);
        _detail.Changed += (_, _) => Reload();

        _tree.Dock = DockStyle.Fill;
        _tree.HideSelection = false;
        _tree.BorderStyle = BorderStyle.None;
        _tree.AfterSelect += OnTypeSelected;

        Ui.ConfigureGrid(_grid);
        _grid.SelectionChanged += OnSubjectSelected;
        _archived.CheckedChanged += (_, _) => LoadTypes();

        var newButton = Ui.Action("New…");
        newButton.Enabled = bsk is not null;
        newButton.Click += (_, _) => DoNew();
        var refresh = Ui.Action("Refresh");
        refresh.Click += (_, _) => Reload();

        _outer.Dock = DockStyle.Fill;
        _outer.FixedPanel = FixedPanel.Panel1;
        _outer.Panel1MinSize = 60;
        _outer.Panel1.Controls.Add(_tree);
        _outer.Panel2.Controls.Add(_inner);
        _inner.Dock = DockStyle.Fill;

        var listToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        listToolbar.Controls.Add(newButton);
        _inner.Panel1.Controls.Add(_grid);
        _inner.Panel1.Controls.Add(listToolbar);
        _inner.Panel2.Controls.Add(_detail);

        var banner = Ui.Toolbar();
        banner.Controls.Add(refresh);
        banner.Controls.Add(_archived);
        banner.Controls.Add(_status);
        Controls.Add(_outer);
        Controls.Add(banner);
        _outer.Layout += OnOuterLayout;
    }

    public bool IsDirty => false;

    public bool TrySaveEdits() => true;

    public void DiscardEdits()
    {
    }

    public void Reload()
    {
        var selectedType = _tree.SelectedNode?.Tag as string;
        var keep = _grid.CurrentRow?.DataBoundItem is SubjectListItem item ? item.Id : _pendingSelect;
        LoadTypes();
        if (selectedType is not null)
        {
            SelectType(selectedType);
        }

        if (keep != Guid.Empty)
        {
            SelectSubject(keep);
        }
    }

    public void SelectSubject(Guid id)
    {
        _pendingSelect = id;
        var subject = _reader.GetSubject(id);
        if (subject is null)
        {
            _detail.Clear();
            return;
        }

        SelectType(subject.Type);
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is SubjectListItem item && item.Id == id)
            {
                var visible = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
                if (visible is not null)
                {
                    _grid.CurrentCell = row.Cells[visible.Index];
                }

                _detail.LoadSubject(id);
                return;
            }
        }

        _detail.Clear();
    }

    public void SaveViewState(ViewStateStore store)
    {
        store.Set(Destinations.Browse, "archived", _archived.Checked ? "1" : "0");
        store.Set(Destinations.Browse, "type", _tree.SelectedNode?.Tag as string);
        store.Set(Destinations.Browse, "selected", _detail.SelectedId == Guid.Empty ? null : _detail.SelectedId.ToString());
    }

    public void RestoreViewState(ViewStateStore store)
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        _archived.Checked = store.Get(Destinations.Browse, "archived") == "1";
        var type = store.Get(Destinations.Browse, "type");
        if (type is not null)
        {
            SelectType(type);
        }

        if (Guid.TryParse(store.Get(Destinations.Browse, "selected"), out var id))
        {
            _pendingSelect = id;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyBrowseSplits();
        LoadTypes();
        if (_pendingSelect != Guid.Empty)
        {
            SelectSubject(_pendingSelect);
        }
    }

    private void OnOuterLayout(object? sender, LayoutEventArgs e)
    {
        if (_outer.Width < 400)
        {
            return;
        }

        _outer.Layout -= OnOuterLayout;
        ApplyBrowseSplits();
    }

    /// <summary>
    /// Type tree was 150px and the list 460px — both too tight. Prefer 1.5×
    /// for the tree (225) and a slightly tighter list (650), clamped when the
    /// window cannot hold the full pair plus a usable detail pane.
    /// </summary>
    private void ApplyBrowseSplits()
    {
        try
        {
            const int treeWidth = 225;
            const int gridWidth = 650;
            const int treeMin = 120;
            const int gridMin = 280;
            const int detailMin = 220;

            var outerUsable = _outer.Width - _outer.SplitterWidth;
            if (outerUsable >= treeMin + gridMin + detailMin)
            {
                _outer.SplitterDistance = (int)Math.Clamp(treeWidth, treeMin, outerUsable - gridMin - detailMin);
                _outer.Panel1MinSize = treeMin;
            }

            var innerUsable = _inner.Width - _inner.SplitterWidth;
            if (innerUsable >= gridMin + detailMin)
            {
                _inner.SplitterDistance = (int)Math.Clamp(gridWidth, gridMin, innerUsable - detailMin);
                _inner.Panel1MinSize = gridMin;
                _inner.Panel2MinSize = detailMin;
            }
        }
        catch (InvalidOperationException)
        {
            // Window too small to honour the preferred split; leave the default.
        }
    }

    private void LoadTypes()
    {
        try
        {
            var selectedType = _tree.SelectedNode?.Tag as string;
            var counts = _reader.GetTypeCounts(_archived.Checked);
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            var total = 0L;
            foreach (var count in counts)
            {
                total += count.N;
                var label = $"{PilotVocab.Label(count.Type)} ({count.N})";
                _tree.Nodes.Add(new TreeNode(label) { Tag = count.Type });
            }

            _tree.EndUpdate();
            var mode = _bsk is null ? " · read-only" : "";
            _status.Text = $"Connected · {counts.Count} types · {total} subjects{mode}";
            if (selectedType is not null)
            {
                SelectType(selectedType);
            }
        }
        catch (Exception ex)
        {
            _status.Text = "Not connected";
            Ui.ShowError(this, "Read failed", ex);
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
            var subjects = _reader.GetSubjects(type, _archived.Checked).ToList();
            _grid.DataSource = subjects;
            BindColumns();
            if (subjects.Count == 0)
            {
                _detail.Clear();
            }
        }
        catch (Exception ex)
        {
            Ui.ShowError(this, "Read failed", ex);
        }
    }

    private void BindColumns()
    {
        Ui.HideAllColumns(_grid);
        Ui.ShowColumn(_grid, "Title", 240, 0);
        Ui.ShowColumn(_grid, "DisplayStatus", 100, 1, "Status");
        Ui.ShowColumn(_grid, "Due", 92, 2);
        Ui.ShowColumn(_grid, "AreaName", 110, 3, "Area");
        Ui.ShowColumn(_grid, "Tags", 120, 4);
        Ui.ShowColumn(_grid, "Urn", 92, 5);
    }

    private void OnSubjectSelected(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow?.DataBoundItem is SubjectListItem item)
        {
            _detail.LoadSubject(item.Id);
        }
    }

    private void DoNew()
    {
        if (_bsk is null)
        {
            Ui.WarnNoBsk(this);
            return;
        }

        var type = _tree.SelectedNode?.Tag as string;
        var created = type is null
            ? NewSubjectDialog.ShowNew(this, _reader, _bsk)
            : NewSubjectDialog.ShowNewOfType(this, _reader, _bsk, type);
        if (created is null)
        {
            return;
        }

        LoadTypes();
        SelectType(created.Type);
        var matches = _grid.Rows.Cast<DataGridViewRow>()
            .Any(r => r.DataBoundItem is SubjectListItem item && item.Id == created.Id);
        if (matches)
        {
            SelectSubject(created.Id);
        }
        else
        {
            MessageBox.Show(this, $"Created {PilotVocab.Label(created.Type)} “{created.Title}”. It is hidden by the current filters.",
                "Created", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
}
