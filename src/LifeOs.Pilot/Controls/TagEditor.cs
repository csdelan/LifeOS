using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Controls;

/// <summary>
/// Tag chip list + autocomplete over <c>v_tag_universe</c> (GEN-1). Kept visually
/// separate from relationships. Immediate mode shells <c>bsk tag</c>; deferred mode
/// (create dialogs) accumulates tags until Save.
/// </summary>
internal sealed class TagEditor : UserControl
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly FlowLayoutPanel _chips = new() { AutoSize = true, WrapContents = true, Dock = DockStyle.Fill };
    private readonly ComboBox _input = new() { Width = 180 };
    private readonly Button _add = Ui.Action("Add tag");
    private readonly List<string> _tags = [];
    private string? _itemRef;
    private bool _suppress;

    public TagEditor(SubjectReader reader, BskCli? bsk)
    {
        _reader = reader;
        _bsk = bsk;
        Height = 72;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var entry = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        _input.DropDownStyle = ComboBoxStyle.DropDown;
        _input.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _input.AutoCompleteSource = AutoCompleteSource.ListItems;
        _add.Click += (_, _) => AddFromInput();
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                AddFromInput();
            }
        };
        entry.Controls.Add(_input);
        entry.Controls.Add(_add);
        root.Controls.Add(_chips, 0, 0);
        root.Controls.Add(entry, 0, 1);
        Controls.Add(root);
        ReloadUniverse();
    }

    public IReadOnlyList<string> Tags => _tags;

    public event EventHandler? TagsChanged;

    public void Bind(string? itemRef, IEnumerable<string> tags)
    {
        _itemRef = itemRef;
        _suppress = true;
        _tags.Clear();
        _tags.AddRange(tags.Select(Normalize).Where(t => t.Length > 0).Distinct());
        Render();
        _suppress = false;
        ReloadUniverse();
    }

    public void ClearDeferred() => Bind(null, []);

    private static string Normalize(string tag) => tag.Trim().ToLowerInvariant();

    private void ReloadUniverse()
    {
        try
        {
            var items = _reader.GetTagUniverse().Select(t => t.Tag).ToArray();
            _input.Items.Clear();
            _input.Items.AddRange(items);
        }
        catch (Exception)
        {
            // Autocomplete is best-effort; the rest of the control still works.
        }
    }

    private void AddFromInput()
    {
        var tag = Normalize(_input.Text);
        if (tag.Length == 0 || _tags.Contains(tag))
        {
            _input.Text = "";
            return;
        }

        if (_itemRef is not null)
        {
            if (_bsk is null)
            {
                Ui.WarnNoBsk(this);
                return;
            }

            try
            {
                _bsk.Run("tag", _itemRef, "--add", tag);
            }
            catch (BskException ex)
            {
                Ui.ShowError(this, "Tag failed", ex);
                return;
            }
        }

        _tags.Add(tag);
        _input.Text = "";
        Render();
        ReloadUniverse();
        if (!_suppress)
        {
            TagsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Remove(string tag)
    {
        if (_itemRef is not null)
        {
            if (_bsk is null)
            {
                Ui.WarnNoBsk(this);
                return;
            }

            try
            {
                _bsk.Run("tag", _itemRef, "--remove", tag);
            }
            catch (BskException ex)
            {
                Ui.ShowError(this, "Untag failed", ex);
                return;
            }
        }

        _tags.Remove(tag);
        Render();
        ReloadUniverse();
        if (!_suppress)
        {
            TagsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Render()
    {
        _chips.Controls.Clear();
        foreach (var tag in _tags)
        {
            var chip = Ui.Action(tag + " ×");
            var captured = tag;
            chip.Click += (_, _) => Remove(captured);
            _chips.Controls.Add(chip);
        }

        if (_tags.Count == 0)
        {
            _chips.Controls.Add(new Label { Text = "(no tags)", AutoSize = true, ForeColor = SystemColors.GrayText });
        }
    }
}
