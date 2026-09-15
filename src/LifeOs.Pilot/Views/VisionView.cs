using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Controls;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Views;

/// <summary>
/// GEN-16 Vision is a composed read: Who I am (Identity Statements) and Where I am
/// going (Goals with target_date &gt; ~2 years). Manual order via vision_order.
/// </summary>
internal sealed class VisionView : UserControl, IPilotView
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly DetailPane _detail;
    private readonly ListBox _who = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ListBox _where = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly CheckBox _includeInactive = new() { Text = "Include archived / inactive", AutoSize = true };
    private bool _restored;
    private List<SubjectListItem> _values = [];
    private List<SubjectListItem> _goals = [];

    public VisionView(SubjectReader reader, BskCli? bsk, Navigator navigator)
    {
        _reader = reader;
        _bsk = bsk;
        _detail = new DetailPane(reader, bsk, navigator);
        _detail.Changed += (_, _) => Reload();
        _who.SelectedIndexChanged += (_, _) => OnPick(_who, _values);
        _where.SelectedIndexChanged += (_, _) => OnPick(_where, _goals);
        _includeInactive.CheckedChanged += (_, _) => Reload();

        var up = Ui.Action("Move up");
        var down = Ui.Action("Move down");
        up.Enabled = bsk is not null;
        down.Enabled = bsk is not null;
        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(1);

        var whoPanel = Labeled("Who I am — Identity Statements", _who);
        var wherePanel = Labeled("Where I am going — long-term Goals (target > 2 years)", _where);
        var lists = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        lists.Panel1.Controls.Add(whoPanel);
        lists.Panel2.Controls.Add(wherePanel);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        split.Panel1.Controls.Add(lists);
        split.Panel2.Controls.Add(_detail);
        Ui.PrepareListDetailSplit(split);

        var banner = Ui.Toolbar();
        banner.Controls.Add(_includeInactive);
        banner.Controls.Add(up);
        banner.Controls.Add(down);
        banner.Controls.Add(new Label
        {
            Text = "Vision is composed — edit the underlying Identity Statement or Goal. No separate Vision object.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(8, 6, 0, 0)
        });
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
            var keep = _detail.SelectedId;
            _values = _reader.GetVisionValues(_includeInactive.Checked).ToList();
            _goals = _reader.GetLongTermGoals(_includeInactive.Checked).ToList();
            Fill(_who, _values);
            Fill(_where, _goals);
            if (keep != Guid.Empty)
            {
                SelectSubject(keep);
                if (!_values.Exists(v => v.Id == keep) && !_goals.Exists(g => g.Id == keep))
                {
                    _detail.Clear();
                }
            }
            else if (_values.Count + _goals.Count == 0)
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
        var valueIndex = _values.FindIndex(v => v.Id == id);
        if (valueIndex >= 0)
        {
            _who.SelectedIndex = valueIndex;
            return;
        }

        var goalIndex = _goals.FindIndex(g => g.Id == id);
        if (goalIndex >= 0)
        {
            _where.SelectedIndex = goalIndex;
        }
    }

    public void SaveViewState(ViewStateStore store)
        => store.Set(Destinations.Vision, "inactive", _includeInactive.Checked ? "1" : "0");

    public void RestoreViewState(ViewStateStore store)
    {
        if (_restored)
        {
            return;
        }

        _restored = true;
        _includeInactive.Checked = store.Get(Destinations.Vision, "inactive") == "1";
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Reload();
    }

    private void OnPick(ListBox box, List<SubjectListItem> items)
    {
        if (box.SelectedIndex >= 0 && box.SelectedIndex < items.Count)
        {
            _detail.LoadSubject(items[box.SelectedIndex].Id);
        }
    }

    private void MoveSelected(int delta)
    {
        if (_bsk is null)
        {
            return;
        }

        var (box, items) = _who.Focused || _who.SelectedIndex >= 0 && !_where.Focused
            ? (_who, _values)
            : (_where, _goals);
        var index = box.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= items.Count)
        {
            return;
        }

        (items[index], items[target]) = (items[target], items[index]);
        Ui.RunWrite(this, _bsk, "Reorder failed", bsk =>
        {
            for (var i = 0; i < items.Count; i++)
            {
                bsk.Run("set", items[i].Urn, $"vision_order={i + 1}");
            }

            Reload();
            box.SelectedIndex = target;
        });
    }

    private static void Fill(ListBox box, List<SubjectListItem> items)
    {
        box.Items.Clear();
        foreach (var item in items)
        {
            box.Items.Add(item.Title);
        }
    }

    private static Control Labeled(string title, Control body)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var label = Ui.Heading(title);
        label.Dock = DockStyle.Top;
        panel.Controls.Add(body);
        panel.Controls.Add(label);
        return panel;
    }
}
