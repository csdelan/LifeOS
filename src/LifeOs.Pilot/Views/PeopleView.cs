using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Controls;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Views;

/// <summary>GEN-15 People / Agents directory: All / Humans / AI, archive, grouped involvements.</summary>
internal sealed class PeopleView : UserControl, IPilotView
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly Navigator _navigator;
    private readonly DataGridView _grid = new();
    private readonly DetailPane _detail;
    private readonly FlowLayoutPanel _involvements = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false
    };
    private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
    private readonly CheckBox _archived = new() { Text = "Archived", AutoSize = true };
    private Guid _pendingSelect;
    private bool _restored;

    public PeopleView(SubjectReader reader, BskCli? bsk, Navigator navigator)
    {
        _reader = reader;
        _bsk = bsk;
        _navigator = navigator;
        _detail = new DetailPane(reader, bsk, navigator);
        _detail.Changed += (_, _) => Reload();
        Ui.ConfigureGrid(_grid);
        _grid.SelectionChanged += OnSelect;
        _kind.Items.AddRange(["All", "Humans", "AI agents"]);
        _kind.SelectedIndex = 0;
        _kind.SelectedIndexChanged += (_, _) => Reload();
        _archived.CheckedChanged += (_, _) => Reload();

        var create = Ui.Action("New Person / Agent…");
        create.Enabled = bsk is not null;
        create.Click += (_, _) =>
        {
            if (_bsk is null)
            {
                return;
            }

            var created = NewSubjectDialog.ShowNewOfType(this, _reader, _bsk, PilotVocab.Person);
            if (created is not null)
            {
                Reload();
                SelectSubject(created.Id);
            }
        };

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        right.Panel1.Controls.Add(_detail);
        right.Panel2.Controls.Add(_involvements);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(right);

        var banner = Ui.Toolbar();
        banner.Controls.Add(create);
        banner.Controls.Add(_kind);
        banner.Controls.Add(_archived);
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
            var keep = Current()?.Id ?? _pendingSelect;
            string? kind = _kind.SelectedIndex switch
            {
                1 => "human",
                2 => "ai",
                _ => null
            };
            var people = _reader.GetPeople(_archived.Checked, kind).ToList();
            _grid.DataSource = people;
            Ui.HideAllColumns(_grid);
            Ui.ShowColumn(_grid, "Title", 220, 0, "Name");
            Ui.ShowColumn(_grid, "PersonKind", 80, 1, "Kind");
            Ui.ShowColumn(_grid, "Role", 160, 2);
            if (keep != Guid.Empty)
            {
                SelectSubject(keep);
            }
            else if (people.Count == 0)
            {
                _detail.Clear();
                _involvements.Controls.Clear();
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
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is PersonRow item && item.Id == id)
            {
                var visible = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Visible);
                if (visible is not null)
                {
                    _grid.CurrentCell = row.Cells[visible.Index];
                }

                Show(item);
                return;
            }
        }
    }

    public void SaveViewState(ViewStateStore store)
    {
        store.Set(Destinations.People, "kind", _kind.SelectedIndex.ToString());
        store.Set(Destinations.People, "archived", _archived.Checked ? "1" : "0");
        store.Set(Destinations.People, "selected", Current()?.Id.ToString());
    }

    public void RestoreViewState(ViewStateStore store)
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        if (int.TryParse(store.Get(Destinations.People, "kind"), out var k) && k >= 0 && k < _kind.Items.Count)
        {
            _kind.SelectedIndex = k;
        }

        _archived.Checked = store.Get(Destinations.People, "archived") == "1";
        if (Guid.TryParse(store.Get(Destinations.People, "selected"), out var id))
        {
            _pendingSelect = id;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Reload();
    }

    private void OnSelect(object? sender, EventArgs e)
    {
        if (Current() is { } person)
        {
            Show(person);
        }
    }

    private PersonRow? Current() => _grid.CurrentRow?.DataBoundItem as PersonRow;

    private void Show(PersonRow person)
    {
        _detail.LoadSubject(person.Id);
        _involvements.Controls.Clear();
        var rows = _reader.GetInvolvements(person.Id).GroupBy(r => r.SubjectType).OrderBy(g => g.Key);
        foreach (var group in rows)
        {
            _involvements.Controls.Add(Ui.Heading(PilotVocab.Label(group.Key)));
            foreach (var item in group)
            {
                var captured = item;
                var link = new LinkLabel { Text = $"{item.SubjectTitle}  ({item.Role})", AutoSize = true };
                link.LinkClicked += (_, _) => _navigator.OpenSubject(captured.SubjectType, captured.SubjectId);
                _involvements.Controls.Add(link);
            }
        }

        if (_involvements.Controls.Count == 0)
        {
            _involvements.Controls.Add(new Label
            {
                Text = "Not involved in any items yet.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            });
        }
    }
}
