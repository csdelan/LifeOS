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
    private readonly ComboBox _relation = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly TextBox _target = new() { Width = 220 };
    private readonly Button _add = Ui.Action("Relate to…");
    private string? _fromUrn;
    private Guid _fromId;
    private readonly List<PendingLink> _pending = [];

    public RelationshipEditor(SubjectReader reader, BskCli? bsk)
    {
        _reader = reader;
        _bsk = bsk;
        _relation.Items.AddRange(PilotVocab.AlignmentRelations);
        _relation.SelectedIndex = 0;
        _add.Click += (_, _) => Add();

        var entry = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true, Padding = new Padding(0, 4, 0, 0) };
        entry.Controls.Add(new Label { Text = "Relate to", AutoSize = true, Padding = new Padding(0, 6, 4, 0) });
        entry.Controls.Add(_relation);
        entry.Controls.Add(_target);
        entry.Controls.Add(_add);

        var hint = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            Text = "Target: title, URN, or short id. Tags are classification — use the Tags section.",
            ForeColor = SystemColors.GrayText
        };

        Controls.Add(_list);
        Controls.Add(hint);
        Controls.Add(entry);
    }

    public IReadOnlyList<(string Relation, string Target)> Pending
        => _pending.Select(p => (p.Relation, p.Target)).ToList();

    public event EventHandler? Changed;

    public void Bind(Guid id, string? urn)
    {
        _fromId = id;
        _fromUrn = urn;
        _pending.Clear();
        Reload();
    }

    public void ClearDeferred()
    {
        _fromId = Guid.Empty;
        _fromUrn = null;
        _pending.Clear();
        _list.Items.Clear();
    }

    public void Reload()
    {
        _list.Items.Clear();
        if (_fromId == Guid.Empty)
        {
            foreach (var pending in _pending)
            {
                _list.Items.Add($"{pending.Relation} → {pending.Target} (pending save)");
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
                _list.Items.Add($"{edge.Relation} → {edge.Type}: {edge.Title ?? edge.Urn}");
            }

            foreach (var edge in _reader.GetServedBy(_fromId))
            {
                _list.Items.Add($"{edge.Type}: {edge.Title ?? edge.Urn} {edge.Relation} this");
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
        var target = _target.Text.Trim();
        var relation = _relation.SelectedItem?.ToString() ?? PilotVocab.Serves;
        if (target.Length == 0)
        {
            return;
        }

        if (_fromUrn is null)
        {
            _pending.Add(new PendingLink(relation, target));
            _target.Clear();
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
            _target.Clear();
            Reload();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (BskException ex)
        {
            Ui.ShowError(this, "Link failed", ex);
        }
    }

    private sealed record PendingLink(string Relation, string Target);
}
