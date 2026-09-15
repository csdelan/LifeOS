using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Controls;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Views;

/// <summary>
/// Shared list + detail for a single subject type (Goals, Projects, and similar).
/// Filters + archive toggle persist via <see cref="IPilotView"/>.
/// </summary>
internal sealed class TypedListView : UserControl, IPilotView
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly string _type;
    private readonly string _destination;
    private readonly DataGridView _grid = new();
    private readonly DetailPane _detail;
    private readonly CheckBox _archived = new() { Text = "Archived", AutoSize = true };
    private readonly ComboBox _statusFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly Label _status = Ui.Muted("");
    private Guid _pendingSelect;
    private bool _restored;
    private bool _reloading;

    public TypedListView(SubjectReader reader, BskCli? bsk, Navigator navigator, string type, string destination)
    {
        _reader = reader;
        _bsk = bsk;
        _type = type;
        _destination = destination;
        _detail = new DetailPane(reader, bsk, navigator);
        _detail.Changed += (_, _) => Reload();
        Ui.ConfigureGrid(_grid);
        _grid.SelectionChanged += OnSelect;
        _archived.CheckedChanged += (_, _) =>
        {
            if (!_reloading)
            {
                Reload();
            }
        };
        _statusFilter.Items.Add("All statuses");
        foreach (var status in PilotVocab.StatusesFor(type))
        {
            _statusFilter.Items.Add(status);
        }

        _statusFilter.SelectedIndex = 0;
        _statusFilter.SelectedIndexChanged += (_, _) =>
        {
            if (!_reloading)
            {
                Reload();
            }
        };

        var newButton = Ui.Action($"New {PilotVocab.Label(type)}…");
        newButton.Enabled = bsk is not null;
        newButton.Click += (_, _) =>
        {
            if (_bsk is null)
            {
                return;
            }

            var created = NewSubjectDialog.ShowNewOfType(this, _reader, _bsk, _type);
            if (created is not null)
            {
                Reload();
                SelectSubject(created.Id);
            }
        };

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_detail);
        Ui.PrepareListDetailSplit(split);

        var banner = Ui.Toolbar();
        banner.Controls.Add(newButton);
        banner.Controls.Add(_statusFilter);
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
        try
        {
            var keep = _grid.CurrentRow?.DataBoundItem is SubjectListItem item ? item.Id : _pendingSelect;
            var rows = _reader.GetSubjects(_type, _archived.Checked).AsEnumerable();
            if (_statusFilter.SelectedIndex > 0)
            {
                var wanted = _statusFilter.SelectedItem?.ToString();
                rows = rows.Where(r => string.Equals(r.DisplayStatus, wanted, StringComparison.OrdinalIgnoreCase));
            }

            var list = rows.ToList();
            _grid.DataSource = list;
            BindColumns();
            _status.Text = $"{list.Count} {PilotVocab.Label(_type)}(s)";
            if (keep != Guid.Empty)
            {
                SelectSubject(keep);
            }
            else if (list.Count == 0)
            {
                _detail.Clear();
            }
        }
        catch (Exception ex)
        {
            Ui.ShowError(this, "Read failed", ex);
        }
    }

    public void SelectSubject(Guid id)
    {
        _pendingSelect = id;
        if (_grid.DataSource is null)
        {
            return;
        }

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is SubjectListItem item && item.Id == id)
            {
                _grid.ClearSelection();
                row.Selected = true;
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
        store.Set(_destination, "archived", _archived.Checked ? "1" : "0");
        store.Set(_destination, "status", _statusFilter.SelectedItem?.ToString());
        store.Set(_destination, "selected", _detail.SelectedId == Guid.Empty ? null : _detail.SelectedId.ToString());
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
            _archived.Checked = store.Get(_destination, "archived") == "1";
            var status = store.Get(_destination, "status");
            if (status is not null)
            {
                var index = _statusFilter.Items.IndexOf(status);
                if (index >= 0)
                {
                    _statusFilter.SelectedIndex = index;
                }
            }

            if (Guid.TryParse(store.Get(_destination, "selected"), out var id))
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

    private void OnSelect(object? sender, EventArgs e)
    {
        if (_grid.CurrentRow?.DataBoundItem is SubjectListItem item)
        {
            _detail.LoadSubject(item.Id);
        }
    }

    private void BindColumns()
    {
        Ui.HideAllColumns(_grid);
        Ui.ShowColumn(_grid, "Title", 280, 0);
        Ui.ShowColumn(_grid, "DisplayStatus", 110, 1, "Status");
        Ui.ShowColumn(_grid, "TargetDate", 100, 2, "Target");
        Ui.ShowColumn(_grid, "Due", 100, 3);
        Ui.ShowColumn(_grid, "AreaName", 120, 4, "Area");
        Ui.ShowColumn(_grid, "Tags", 140, 5);
    }
}
