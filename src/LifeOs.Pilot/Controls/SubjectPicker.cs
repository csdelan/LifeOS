using System.ComponentModel;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Controls;

/// <summary>Dropdown of existing subjects for <c>bsk link</c> targets.</summary>
internal sealed class SubjectPicker : ComboBox
{
    private readonly SubjectReader _reader;
    private readonly List<SubjectListItem> _items = [];

    public SubjectPicker(SubjectReader reader)
    {
        _reader = reader;
        DropDownStyle = ComboBoxStyle.DropDownList;
        Width = 280;
        IntegralHeight = false;
        DropDownWidth = 420;
        Reload();
    }

    public void Reload(Guid excludeId = default)
    {
        var selected = SelectedUrn;
        _items.Clear();
        try
        {
            _items.AddRange(_reader.QuerySubjects(null).Where(s => s.Id != excludeId));
        }
        catch (Exception)
        {
            // Leave empty on read failure.
        }

        BeginUpdate();
        Items.Clear();
        Items.Add("(choose an existing item…)");
        foreach (var item in _items)
        {
            Items.Add($"{PilotVocab.Label(item.Type)}: {item.Title}");
        }

        EndUpdate();
        SelectUrn(selected);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? SelectedUrn
    {
        get
        {
            if (SelectedIndex <= 0)
            {
                return null;
            }

            return _items[SelectedIndex - 1].Urn;
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? SelectedLabel
    {
        get
        {
            if (SelectedIndex <= 0)
            {
                return null;
            }

            var item = _items[SelectedIndex - 1];
            return $"{PilotVocab.Label(item.Type)}: {item.Title}";
        }
    }

    public void ClearSelection()
    {
        if (Items.Count > 0)
        {
            SelectedIndex = 0;
        }
    }

    public void SelectUrn(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            SelectedIndex = Items.Count > 0 ? 0 : -1;
            return;
        }

        var index = _items.FindIndex(s => s.Urn == urn);
        SelectedIndex = index >= 0 ? index + 1 : 0;
    }
}
