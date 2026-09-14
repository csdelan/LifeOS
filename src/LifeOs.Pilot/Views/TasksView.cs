using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Controls;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Views;

/// <summary>
/// TASKS-1: Overdue / Today / Upcoming / Unscheduled grouping, always-visible
/// title-only quick entry, inline Complete / In progress, filters.
/// </summary>
internal sealed class TasksView : UserControl, IPilotView
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly DataGridView _grid = new();
    private readonly DetailPane _detail;
    private readonly TextBox _quick = new() { Width = 360, PlaceholderText = "New task title — Enter to create" };
    private readonly ComboBox _statusFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
    private readonly ComboBox _areaFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    private readonly CheckBox _archived = new() { Text = "Archived", AutoSize = true };
    private readonly Label _status = Ui.Muted("");
    private readonly List<AreaRow> _areas = [];
    private Guid _pendingSelect;
    private bool _restored;
    private bool _reloading;

    public TasksView(SubjectReader reader, BskCli? bsk, Navigator navigator)
    {
        _reader = reader;
        _bsk = bsk;
        _detail = new DetailPane(reader, bsk, navigator);
        _detail.Changed += (_, _) => Reload();
        Ui.ConfigureGrid(_grid);
        _grid.SelectionChanged += OnSelect;

        _statusFilter.Items.Add("Active (default)");
        foreach (var status in PilotVocab.StatusesFor(PilotVocab.Task))
        {
            _statusFilter.Items.Add(status);
        }

        _statusFilter.Items.Add("All statuses");
        _statusFilter.SelectedIndex = 0;
        _statusFilter.SelectedIndexChanged += (_, _) =>
        {
            if (!_reloading)
            {
                Reload();
            }
        };
        _areaFilter.SelectedIndexChanged += (_, _) =>
        {
            if (!_reloading)
            {
                Reload();
            }
        };
        _archived.CheckedChanged += (_, _) =>
        {
            if (!_reloading)
            {
                Reload();
            }
        };

        _quick.Enabled = bsk is not null;
        _quick.KeyDown += OnQuickKey;
        var expand = Ui.Action("Full form…");
        expand.Enabled = bsk is not null;
        expand.Click += (_, _) =>
        {
            if (_bsk is null)
            {
                return;
            }

            var created = NewSubjectDialog.ShowNewOfType(this, _reader, _bsk, PilotVocab.Task);
            if (created is not null)
            {
                Reload();
                SelectSubject(created.Id);
            }
        };

        var complete = Ui.Action("Complete");
        var progress = Ui.Action("In progress");
        complete.Enabled = bsk is not null;
        progress.Enabled = bsk is not null;
        complete.Click += (_, _) => SetStatus("Completed");
        progress.Click += (_, _) => SetStatus("In progress");

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_detail);

        var banner = Ui.Toolbar();
        banner.Controls.Add(new Label { Text = "Quick:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        banner.Controls.Add(_quick);
        banner.Controls.Add(expand);
        banner.Controls.Add(complete);
        banner.Controls.Add(progress);
        banner.Controls.Add(_statusFilter);
        banner.Controls.Add(_areaFilter);
        banner.Controls.Add(_archived);
        banner.Controls.Add(_status);
        Controls.Add(split);
        Controls.Add(banner);
    }

    public bool IsDirty => false;

    public bool TrySaveEdits() => true;

    public void DiscardEdits()
    {
    }

    public void Reload()
    {
        if (_reloading)
        {
            return;
        }

        _reloading = true;
        try
        {
            LoadAreas();
            var keep = _grid.CurrentRow?.DataBoundItem is TaskRow row ? row.Id : _pendingSelect;
            var today = Ui.Today;
            var tasks = _reader.GetSubjects(PilotVocab.Task, _archived.Checked);
            IEnumerable<SubjectListItem> filtered = tasks;

            if (_statusFilter.SelectedIndex == 0)
            {
                filtered = filtered.Where(t => PilotVocab.IsActiveWorkStatus(PilotVocab.Task, t.Status));
            }
            else if (_statusFilter.SelectedItem is string wanted && wanted != "All statuses")
            {
                filtered = filtered.Where(t => string.Equals(t.DisplayStatus, wanted, StringComparison.OrdinalIgnoreCase));
            }

            if (_areaFilter.SelectedIndex > 0 && _areaFilter.SelectedIndex <= _areas.Count)
            {
                var urn = _areas[_areaFilter.SelectedIndex - 1].Urn;
                filtered = filtered.Where(t => t.Area == urn);
            }

            var grouped = filtered.Select(t => ToRow(t, today)).OrderBy(t => t.GroupOrder).ThenBy(t => t.Title).ToList();
            _grid.DataSource = grouped;
            BindColumns();
            _status.Text = $"{grouped.Count} task(s)";
            if (keep != Guid.Empty)
            {
                SelectSubject(keep);
            }
            else if (grouped.Count == 0)
            {
                _detail.Clear();
            }
        }
        catch (Exception ex)
        {
            Ui.ShowError(this, "Read failed", ex);
        }
        finally
        {
            _reloading = false;
        }
    }

    public void SelectSubject(Guid id)
    {
        _pendingSelect = id;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is TaskRow item && item.Id == id)
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
    }

    public void SaveViewState(ViewStateStore store)
    {
        store.Set(Destinations.Tasks, "archived", _archived.Checked ? "1" : "0");
        store.Set(Destinations.Tasks, "status", _statusFilter.SelectedIndex.ToString());
        store.Set(Destinations.Tasks, "area", _areaFilter.SelectedIndex.ToString());
        store.Set(Destinations.Tasks, "selected", _detail.SelectedId == Guid.Empty ? null : _detail.SelectedId.ToString());
    }

    public void RestoreViewState(ViewStateStore store)
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        _reloading = true;
        try
        {
            _archived.Checked = store.Get(Destinations.Tasks, "archived") == "1";
        if (int.TryParse(store.Get(Destinations.Tasks, "status"), out var s) && s >= 0 && s < _statusFilter.Items.Count)
        {
            _statusFilter.SelectedIndex = s;
        }

        LoadAreas();
        if (int.TryParse(store.Get(Destinations.Tasks, "area"), out var a) && a >= 0 && a < _areaFilter.Items.Count)
        {
            _areaFilter.SelectedIndex = a;
        }

        if (Guid.TryParse(store.Get(Destinations.Tasks, "selected"), out var id))
        {
            _pendingSelect = id;
        }
        }
        finally
        {
            _reloading = false;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Reload();
    }

    private void LoadAreas()
    {
        var selected = _areaFilter.SelectedIndex;
        _areas.Clear();
        _areas.AddRange(_reader.GetAreas());
        _areaFilter.Items.Clear();
        _areaFilter.Items.Add("All areas");
        foreach (var area in _areas)
        {
            _areaFilter.Items.Add(area.Name);
        }

        _areaFilter.SelectedIndex = selected >= 0 && selected < _areaFilter.Items.Count ? selected : 0;
    }

    private void OnQuickKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || _bsk is null)
        {
            return;
        }

        e.SuppressKeyPress = true;
        var title = _quick.Text.Trim();
        if (title.Length == 0)
        {
            return;
        }

        try
        {
            var created = _bsk.RunJson<CreatedSubject>("new", "Task", title, "--attr", "priority=Medium");
            _quick.Clear();
            Reload();
            SelectSubject(created.Id);
        }
        catch (BskException ex)
        {
            Ui.ShowError(this, "New Task failed", ex);
        }
    }

    private void SetStatus(string status)
    {
        if (_bsk is null || _grid.CurrentRow?.DataBoundItem is not TaskRow row)
        {
            return;
        }

        Ui.RunWrite(this, _bsk, "Status change failed", bsk =>
        {
            BskWrites.ChangeStatus(bsk, row.Urn, status);
            Reload();
            SelectSubject(row.Id);
        });
    }

    private void OnSelect(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow?.DataBoundItem is TaskRow row)
        {
            _detail.LoadSubject(row.Id);
        }
    }

    private void BindColumns()
    {
        Ui.HideAllColumns(_grid);
        Ui.ShowColumn(_grid, "Group", 100, 0);
        Ui.ShowColumn(_grid, "Title", 280, 1);
        Ui.ShowColumn(_grid, "DisplayStatus", 100, 2, "Status");
        Ui.ShowColumn(_grid, "Due", 100, 3);
        Ui.ShowColumn(_grid, "Scheduled", 100, 4);
        Ui.ShowColumn(_grid, "AreaName", 120, 5, "Area");
        Ui.ShowColumn(_grid, "Tags", 140, 6);
    }

    private static TaskRow ToRow(SubjectListItem t, DateOnly today)
    {
        var due = Parse(t.Due);
        var scheduled = Parse(t.Scheduled);
        string group;
        int order;
        if (due is { } d && d < today)
        {
            group = "Overdue";
            order = 0;
        }
        else if ((due is { } d2 && d2 == today) || (scheduled is { } s && s == today))
        {
            group = "Today";
            order = 1;
        }
        else if ((due is { } d3 && d3 > today) || (scheduled is { } s2 && s2 > today))
        {
            group = "Upcoming";
            order = 2;
        }
        else
        {
            group = "Unscheduled";
            order = 3;
        }

        return new TaskRow
        {
            Id = t.Id,
            Urn = t.Urn,
            Title = t.Title,
            DisplayStatus = t.DisplayStatus,
            Due = t.Due,
            Scheduled = t.Scheduled,
            AreaName = t.AreaName,
            Tags = t.Tags,
            Group = group,
            GroupOrder = order
        };
    }

    private static DateOnly? Parse(string? text)
        => Ui.TryParseDate(text, out var date) ? date : null;

    public sealed class TaskRow
    {
        public Guid Id { get; set; }
        public string Urn { get; set; } = "";
        public string Title { get; set; } = "";
        public string DisplayStatus { get; set; } = "";
        public string? Due { get; set; }
        public string? Scheduled { get; set; }
        public string? AreaName { get; set; }
        public string? Tags { get; set; }
        public string Group { get; set; } = "";
        public int GroupOrder { get; set; }
    }
}
