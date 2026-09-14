using LifeOs.Pilot.Reader;

namespace LifeOs.Pilot.Controls;

/// <summary>Area picker over <c>v_area</c> (GEN-2). Zero-or-one Area per item.</summary>
internal sealed class AreaSelector : ComboBox
{
    private readonly SubjectReader _reader;
    private readonly List<AreaRow> _areas = [];

    public AreaSelector(SubjectReader reader)
    {
        _reader = reader;
        DropDownStyle = ComboBoxStyle.DropDownList;
        Width = 240;
        Reload();
    }

    public void Reload()
    {
        var selected = SelectedUrn;
        _areas.Clear();
        _areas.AddRange(_reader.GetAreas());
        Items.Clear();
        Items.Add("(none)");
        foreach (var area in _areas)
        {
            Items.Add(area.Name);
        }

        SelectUrn(selected);
    }

    public string? SelectedUrn
    {
        get
        {
            if (SelectedIndex <= 0)
            {
                return null;
            }

            return _areas[SelectedIndex - 1].Urn;
        }
    }

    public void SelectUrn(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            SelectedIndex = Items.Count > 0 ? 0 : -1;
            return;
        }

        var index = _areas.FindIndex(a => a.Urn == urn);
        SelectedIndex = index >= 0 ? index + 1 : 0;
    }
}
