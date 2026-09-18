using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Controls;

/// <summary>
/// Subject→subject links via <c>bsk link</c> / <c>bsk unlink</c>, kept visually
/// separate from tags (GEN-1). Immediate when a subject URN is bound; deferred
/// (create) otherwise.
/// </summary>
internal sealed class RelationshipEditor : UserControl
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly ComboBox _relation = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox _targetType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly SubjectPicker _target;
    private readonly CheckBox _archived = new() { Text = "Archived", AutoSize = true, Padding = new Padding(8, 6, 4, 0) };
    private readonly Button _add = Ui.Action("Add");
    private readonly Button _remove = Ui.Action("Remove");
    private readonly Label _hint = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        Text = "Choose a type to filter, then pick an existing item. Archived items are hidden unless you check Archived. This is a link between two items, not a tag.",
        ForeColor = SystemColors.GrayText,
        Padding = new Padding(0, 0, 0, 4)
    };
    private string? _fromUrn;
    private Guid _fromId;
    private readonly List<PendingLink> _pending = [];

    // Project/Task first — the usual link targets — then the rest of CreatableTypes.
    private static readonly string[] LinkTargetTypes =
    [
        PilotVocab.Project, PilotVocab.Task, PilotVocab.Goal, PilotVocab.Value,
        PilotVocab.Problem, PilotVocab.Decision, PilotVocab.Idea, PilotVocab.Person,
        PilotVocab.Area, PilotVocab.Habit, PilotVocab.Appointment, PilotVocab.Commitment
    ];

    public RelationshipEditor(SubjectReader reader, BskCli? bsk)
    {
        _reader = reader;
        _bsk = bsk;
        _target = new SubjectPicker(reader);
        _relation.Items.AddRange(PilotVocab.AlignmentRelations);
        _relation.SelectedIndex = 0;
        FillTargetTypes();
        _targetType.SelectedIndexChanged += (_, _) => RefreshTarget();
        _archived.CheckedChanged += (_, _) => RefreshTarget();
        _add.Click += (_, _) => Add();
        _remove.Click += (_, _) => Remove();
        _remove.Enabled = false;
        _list.SelectedIndexChanged += (_, _) => _remove.Enabled = SelectedRow?.Removable == true;

        var entry = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 2)
        };
        entry.Controls.Add(new Label { Text = "This item", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        entry.Controls.Add(_relation);
        entry.Controls.Add(new Label { Text = "this existing", AutoSize = true, Padding = new Padding(8, 6, 4, 0) });
        entry.Controls.Add(_targetType);
        entry.Controls.Add(_target);
        entry.Controls.Add(_archived);
        entry.Controls.Add(_add);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 2, 0, 0)
        };
        actions.Controls.Add(_remove);

        Controls.Add(_list);
        Controls.Add(actions);
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

    protected override Size DefaultSize => new(420, 200);

    public IReadOnlyList<(string Relation, string OtherUrn, bool Incoming)> Pending
        => _pending.Select(p => (p.Relation, p.OtherUrn, p.Incoming)).ToList();

    public event EventHandler? Changed;

    /// <summary>
    /// Pre-populate a deferred link shown in the create form. <paramref name="incoming"/>
    /// means the existing item is the edge's <c>from</c> (e.g. Problem <c>results_in</c>
    /// this new Idea); otherwise this new item is <c>from</c>.
    /// </summary>
    public void SeedPending(string relation, string otherUrn, string label, bool incoming = false)
    {
        if (_pending.Any(p => p.Relation == relation && p.OtherUrn == otherUrn))
        {
            ApplyPresetUi(relation, otherUrn);
            Reload();
            return;
        }

        _pending.Add(new PendingLink(relation, otherUrn, label, incoming));
        ApplyPresetUi(relation, otherUrn);
        Reload();
    }

    public void ApplyPresetUi(string relation, string otherUrn)
    {
        SelectRelation(relation);
        // Any-type so a preset target is visible regardless of the last type filter.
        if (_targetType.SelectedIndex != 0)
        {
            _targetType.SelectedIndex = 0;
        }

        _target.SelectUrn(otherUrn);
    }

    public void SelectRelation(string relation)
    {
        var index = _relation.FindStringExact(relation);
        if (index >= 0)
        {
            _relation.SelectedIndex = index;
        }
    }

    private EdgeRow? SelectedRow => _list.SelectedItem as EdgeRow;

    public void Bind(Guid id, string? urn)
    {
        _fromId = id;
        _fromUrn = urn;
        _pending.Clear();
        RefreshTarget();
        Reload();
    }

    public void ClearDeferred()
    {
        _fromId = Guid.Empty;
        _fromUrn = null;
        _pending.Clear();
        _list.Items.Clear();
        _remove.Enabled = false;
        RefreshTarget();
    }

    public void Reload()
    {
        _list.Items.Clear();
        if (_fromId == Guid.Empty)
        {
            for (var i = 0; i < _pending.Count; i++)
            {
                var pending = _pending[i];
                var display = pending.Incoming
                    ? $"{pending.Label} {pending.Relation} this (pending save)"
                    : $"{pending.Relation} → {pending.Label} (pending save)";
                _list.Items.Add(new EdgeRow(display) { PendingIndex = i });
            }

            if (_pending.Count == 0)
            {
                _list.Items.Add(new EdgeRow("(none yet — assigned on Save)"));
            }

            _remove.Enabled = SelectedRow?.Removable == true;
            return;
        }

        try
        {
            foreach (var edge in _reader.GetServes(_fromId))
            {
                _list.Items.Add(new EdgeRow($"{edge.Relation} → {PilotVocab.Label(edge.Type)}: {edge.Title ?? edge.Urn}")
                {
                    FromUrn = _fromUrn,
                    Relation = edge.Relation,
                    ToUrn = edge.Urn
                });
            }

            foreach (var edge in _reader.GetServedBy(_fromId))
            {
                _list.Items.Add(new EdgeRow($"{PilotVocab.Label(edge.Type)}: {edge.Title ?? edge.Urn} {edge.Relation} this")
                {
                    FromUrn = edge.Urn,
                    Relation = edge.Relation,
                    ToUrn = _fromUrn
                });
            }

            if (_list.Items.Count == 0)
            {
                _list.Items.Add(new EdgeRow("(no relationships)"));
            }
        }
        catch (Exception ex)
        {
            _list.Items.Add(new EdgeRow($"Read failed: {ex.Message}"));
        }

        _remove.Enabled = SelectedRow?.Removable == true;
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
            if (_pending.Any(p => p.Relation == relation && p.OtherUrn == target))
            {
                _target.ClearSelection();
                Reload();
                return;
            }

            _pending.Add(new PendingLink(relation, target, label, Incoming: false));
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

    private void Remove()
    {
        if (SelectedRow is not { Removable: true } row)
        {
            return;
        }

        // Deferred (pre-save) pending link: just drop it from the list.
        if (row.PendingIndex >= 0)
        {
            if (row.PendingIndex < _pending.Count)
            {
                _pending.RemoveAt(row.PendingIndex);
                Reload();
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        if (_bsk is null || row.FromUrn is null || row.Relation is null || row.ToUrn is null)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"Remove this relationship?\n\n{row}\n\nThe items are kept; only the link between them is removed.",
            "Remove relationship", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm != DialogResult.OK)
        {
            return;
        }

        try
        {
            _bsk.Run("unlink", row.FromUrn, row.Relation, row.ToUrn);
            Reload();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (BskException ex)
        {
            Ui.ShowError(this, "Remove failed", ex);
        }
    }

    private void FillTargetTypes()
    {
        _targetType.DropDownWidth = 180;
        _targetType.Items.Add(new TypeChoice(null, "item"));
        foreach (var type in LinkTargetTypes)
        {
            _targetType.Items.Add(new TypeChoice(type, PilotVocab.Label(type)));
        }

        _targetType.SelectedIndex = 0;
    }

    private string? SelectedTargetType => (_targetType.SelectedItem as TypeChoice)?.Type;

    private void RefreshTarget()
        => _target.Reload(_fromId, SelectedTargetType, _archived.Checked);

    private sealed record TypeChoice(string? Type, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record PendingLink(string Relation, string OtherUrn, string Label, bool Incoming);

    // A list row that carries what it takes to unlink the edge it displays. A
    // placeholder / error row leaves the edge fields null and is not Removable.
    private sealed record EdgeRow(string Display)
    {
        public string? FromUrn { get; init; }
        public string? Relation { get; init; }
        public string? ToUrn { get; init; }
        public int PendingIndex { get; init; } = -1;

        public bool Removable
            => PendingIndex >= 0 || (FromUrn is not null && Relation is not null && ToUrn is not null);

        public override string ToString() => Display;
    }
}
