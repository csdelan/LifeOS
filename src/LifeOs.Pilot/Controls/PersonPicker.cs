using LifeOs.Pilot.Reader;

namespace LifeOs.Pilot.Controls;

/// <summary>People/Agents selector (GEN-15) used by involve / attendees / waiting-for.</summary>
internal sealed class PersonPicker : ComboBox
{
    private readonly SubjectReader _reader;
    private readonly List<PersonRow> _people = [];

    public PersonPicker(SubjectReader reader)
    {
        _reader = reader;
        DropDownStyle = ComboBoxStyle.DropDownList;
        Width = 260;
        Reload();
    }

    public void Reload()
    {
        var selected = SelectedUrn;
        _people.Clear();
        try
        {
            _people.AddRange(_reader.GetPeople());
        }
        catch (Exception)
        {
            // Leave empty on read failure.
        }

        Items.Clear();
        Items.Add("(none)");
        foreach (var person in _people)
        {
            var kind = string.Equals(person.PersonKind, "ai", StringComparison.OrdinalIgnoreCase) ? "AI" : "Human";
            Items.Add($"{person.Title} ({kind})");
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

            return _people[SelectedIndex - 1].Urn;
        }
    }

    public void SelectUrn(string? urn)
    {
        if (string.IsNullOrWhiteSpace(urn))
        {
            SelectedIndex = Items.Count > 0 ? 0 : -1;
            return;
        }

        var index = _people.FindIndex(p => p.Urn == urn);
        SelectedIndex = index >= 0 ? index + 1 : 0;
    }
}
