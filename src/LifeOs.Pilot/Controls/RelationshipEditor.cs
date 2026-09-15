using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Controls;

/// <summary>
/// Subject→subject links via <c>bsk link</c>, kept visually separate from tags (GEN-1).
/// Immediate when a subject URN is bound; deferred (create) otherwise.
/// </summary>
internal sealed class RelationshipEditor : UserControl
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ComboBox _relation = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly SubjectPicker _target;
    private readonly Button _add = Ui.Action("Add");
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        Text = "Pick an existing Goal, Identity Statement, Project, or other item. This is a link between two items, not a tag.",
        ForeColor = SystemColors.GrayText,
        Padding = new Padding(0, 0, 0, 4)
    };
    private string? _fromUrn;
    private Guid _fromId;
    private readonly List<PendingLink> _pending = [];

    public RelationshipEditor(SubjectReader reader, BskCli? bsk)
    {
        _reader = reader;
        _bsk = bsk;
        _target = new SubjectPicker(reader);
        _relation.Items.AddRange(PilotVocab.AlignmentRelations);
        _relation.SelectedIndex = 0;
        _add.Click += (_, _) => Add();

        var entry = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 2)
        };
        entry.Controls.Add(new Label { Text = "This item", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        entry.Controls.Add(_relation);
        entry.Controls.Add(new Label { Text = "this existing item:", AutoSize = true, Padding = new Padding(8, 6, 4, 0) });
        entry.Controls.Add(_target);
        entry.Controls.Add(_add);

        Controls.Add(_list);
        Controls.Add(_hint);
        Controls.Add(entry);
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        var w = Math.Max(60, DisplayRectangle.Width);
        if (_hint.MaximumSize.Width != w)
        {
            _hint.MaximumSize = new Size(w, 0);
        }

        base.OnLayout(e);
    }

    protected override Size DefaultSize => new(420, 180);

    public IReadOnlyList<(string Relation, string Target)> Pending
        => _pending.Select(p => (p.Relation, p.Target)).ToList();

    public event EventHandler? Changed;

    public void Bind(Guid id, string? urn)
    {
        _fromId = id;
        _fromUrn = urn;
        _pending.Clear();
        _target.Reload(excludeId: id);
        Reload();
    }

    public void ClearDeferred()
    {
        _fromId = Guid.Empty;
        _fromUrn = null;
        _pending.Clear();
        _list.Items.Clear();
        _target.Reload();
    }

    public void Reload()
    {
        _list.Items.Clear();
        if (_fromId == Guid.Empty)
        {
            foreach (var pending in _pending)
            {
                _list.Items.Add($"{pending.Relation} → {pending.Label} (pending save)");
            }

            if (_pending.Count == 0)
            {
                _list.Items.Add("(none yet — assigned on Save)");
            }

            return;
        }

        try
        {
            foreach (var edge in _reader.GetServes(_fromId))
            {
                _list.Items.Add($"{edge.Relation} → {PilotVocab.Label(edge.Type)}: {edge.Title ?? edge.Urn}");
            }

            foreach (var edge in _reader.GetServedBy(_fromId))
            {
                _list.Items.Add($"{PilotVocab.Label(edge.Type)}: {edge.Title ?? edge.Urn} {edge.Relation} this");
            }

            if (_list.Items.Count == 0)
            {
                _list.Items.Add("(no relationships)");
            }
        }
        catch (Exception ex)
        {
            _list.Items.Add($"Read failed: {ex.Message}");
        }
    }

    private void Add()
    {
        var target = _target.SelectedUrn;
        var label = _target.SelectedLabel;
        var relation = _relation.SelectedItem?.ToString() ?? PilotVocab.Serves;
        if (target is null || label is null)
        {
            return;
        }

        if (_fromUrn is null)
        {
            _pending.Add(new PendingLink(relation, target, label));
            _target.ClearSelection();
            Reload();
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_bsk is null)
        {
            Ui.WarnNoBsk(this);
            return;
        }

        try
        {
            _bsk.Run("link", _fromUrn, relation, target);
            _target.ClearSelection();
            Reload();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (BskException ex)
        {
            Ui.ShowError(this, "Link failed", ex);
        }
    }

    private sealed record PendingLink(string Relation, string Target, string Label);
}
