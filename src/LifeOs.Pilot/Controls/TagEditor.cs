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
    // TextBox + CustomSource, not ComboBox + ListItems: with any existing
    // universe entries, ComboBox autocomplete swallows keystrokes that don't
    // continue a match, so a new tag like "newtag" arrives as "ne".
    private readonly TextBox _input = new();
    private readonly Button _add = Ui.Action("Add tag");
    private readonly List<string> _tags = [];
    private string? _itemRef;
    private bool _suppress;

    protected override Size DefaultSize => new(460, 80);

    public TagEditor(SubjectReader reader, BskCli? bsk)
    {
        _reader = reader;
        _bsk = bsk;
        Height = 80;
        MinimumSize = new Size(240, 72);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var entry = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        entry.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _input.Dock = DockStyle.Fill;
        _input.Margin = new Padding(0, 3, 6, 0);
        _input.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _input.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _input.AutoCompleteCustomSource = [];
        _add.CausesValidation = false;
        _add.Margin = new Padding(0);
        _add.Click += (_, _) => AddFromInput();
        _input.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                AddFromInput();
            }
        };
        entry.Controls.Add(_input, 0, 0);
        entry.Controls.Add(_add, 1, 0);
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

    /// <summary>
    /// Fold typed-but-not-added text into the tag list. The New dialog's Save is
    /// the form AcceptButton, so Enter / click-Save can fire without Add.
    /// </summary>
    public void CommitPending() => AddFromInput();

    /// <summary>
    /// Enter in the entry box must add a tag, not click the parent form's Save.
    /// ProcessCmdKey runs before Form.ProcessDialogKey (AcceptButton).
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && (_input.Focused || _add.Focused))
        {
            AddFromInput();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static string Normalize(string tag) => tag.Trim().ToLowerInvariant();

    private void ReloadUniverse()
    {
        try
        {
            var items = _reader.GetTagUniverse().Select(t => t.Tag).ToArray();
            var source = new AutoCompleteStringCollection();
            source.AddRange(items);
            _input.AutoCompleteCustomSource = source;
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
