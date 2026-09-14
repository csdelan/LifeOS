using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Controls;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Views;

/// <summary>GEN-2 Areas master list + grouped related work. Areas are permanent (no archive).</summary>
internal sealed class AreasView : UserControl, IPilotView
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly Navigator _navigator;
    private readonly DataGridView _grid = new();
    private readonly DetailPane _detail;
    private readonly FlowLayoutPanel _related = new()
    {
        Dock = DockStyle.Fill,
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false
    };
    private Guid _pendingSelect;
    private bool _restored;

    public AreasView(SubjectReader reader, BskCli? bsk, Navigator navigator)
    {
        _reader = reader;
        _bsk = bsk;
        _navigator = navigator;
        _detail = new DetailPane(reader, bsk, navigator);
        _detail.Changed += (_, _) => Reload();
        Ui.ConfigureGrid(_grid);
        _grid.SelectionChanged += OnSelect;

        var create = Ui.Action("New Area…");
        create.Enabled = bsk is not null;
        create.Click += (_, _) =>
        {
            if (_bsk is null)
            {
                return;
            }

            var created = NewSubjectDialog.ShowNewOfType(this, _reader, _bsk, PilotVocab.Area);
            if (created is not null)
            {
                Reload();
                SelectSubject(created.Id);
            }
        };

        var fromArea = Ui.Action("New item in Area…");
        fromArea.Enabled = bsk is not null;
        fromArea.Click += CreateFromArea;

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        right.Panel1.Controls.Add(_detail);
        right.Panel2.Controls.Add(_related);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(right);

        var banner = Ui.Toolbar();
        banner.Controls.Add(create);
        banner.Controls.Add(fromArea);
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
            var areas = _reader.GetAreas().ToList();
            _grid.DataSource = areas;
            Ui.HideAllColumns(_grid);
            Ui.ShowColumn(_grid, "Name", 200, 0);
            Ui.ShowColumn(_grid, "Description", 280, 1);
            if (keep != Guid.Empty)
            {
                SelectSubject(keep);
            }
            else if (areas.Count == 0)
            {
                _detail.Clear();
                _related.Controls.Clear();
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
            if (row.DataBoundItem is AreaRow item && item.Id == id)
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
        => store.Set(Destinations.Areas, "selected", Current()?.Id.ToString());

    public void RestoreViewState(ViewStateStore store)
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        if (Guid.TryParse(store.Get(Destinations.Areas, "selected"), out var id))
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
        if (Current() is { } area)
        {
            Show(area);
        }
    }

    private AreaRow? Current() => _grid.CurrentRow?.DataBoundItem as AreaRow;

    private void Show(AreaRow area)
    {
        _detail.LoadSubject(area.Id);
        _related.Controls.Clear();
        var related = _reader.QuerySubjects(null)
            .Where(s => s.Area == area.Urn && s.Type != PilotVocab.Area)
            .GroupBy(s => s.Type)
            .OrderBy(g => g.Key);
        foreach (var group in related)
        {
            _related.Controls.Add(Ui.Heading(PilotVocab.Label(group.Key)));
            foreach (var item in group.OrderBy(i => i.Title))
            {
                var captured = item;
                var link = new LinkLabel { Text = item.Title, AutoSize = true };
                link.LinkClicked += (_, _) => _navigator.OpenSubject(captured.Type, captured.Id);
                _related.Controls.Add(link);
            }
        }

        if (_related.Controls.Count == 0)
        {
            _related.Controls.Add(new Label
            {
                Text = "Nothing linked to this Area yet.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText
            });
        }
    }

    private void CreateFromArea(object? sender, EventArgs e)
    {
        if (_bsk is null || Current() is not { } area)
        {
            return;
        }

        var type = PickChildType();
        if (type is null)
        {
            return;
        }

        var created = NewSubjectDialog.ShowNewOfType(this, _reader, _bsk, type, area.Urn);
        if (created is not null)
        {
            Show(area);
        }
    }

    private string? PickChildType()
    {
        var types = new[] { PilotVocab.Value, PilotVocab.Goal, PilotVocab.Project, PilotVocab.Task, PilotVocab.Habit };
        using var dialog = new Form
        {
            Text = "New item in Area",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false
        };
        var list = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        foreach (var type in types)
        {
            list.Items.Add(PilotVocab.Label(type));
        }

        list.SelectedIndex = 0;
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        dialog.Controls.Add(new FlowLayoutPanel { AutoSize = true, Controls = { list, ok } });
        dialog.AcceptButton = ok;
        return dialog.ShowDialog(this) == DialogResult.OK ? types[list.SelectedIndex] : null;
    }
}
