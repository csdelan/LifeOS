using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Controls;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Dialogs;

/// <summary>
/// Explicit edit of an existing subject (BROWSE-2): title + type-specific attributes
/// via <c>bsk set</c>. Status is not edited here — use Change status. Cancel writes nothing.
/// </summary>
internal sealed class SubjectEditDialog : Form
{
    private readonly BskCli _bsk;
    private readonly string _urn;
    private readonly string _type;
    private readonly TextBox _title;
    private readonly Dictionary<string, TextBox> _fields = new(StringComparer.Ordinal);
    private readonly AreaSelector? _area;
    private readonly ComboBox? _personKind;
    private readonly CheckBox? _allowsPartial;
    private readonly RecurrenceEditor? _recurrence;

    public SubjectEditDialog(SubjectReader reader, BskCli bsk, Guid id)
    {
        var subject = reader.GetSubject(id) ?? throw new InvalidOperationException("Subject not found.");
        _bsk = bsk;
        _urn = subject.Urn;
        _type = subject.Type;
        var attrs = DetailPane.ParseAttrs(subject.Attributes).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"Edit {PilotVocab.Label(_type)}";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(540, 640);
        MinimumSize = new Size(480, 400);
        Padding = new Padding(12);

        _title = new TextBox { Text = subject.Title, Width = 460, Anchor = AnchorStyles.Left | AnchorStyles.Right };
        var stack = new TableLayoutPanel { AutoScroll = true, Dock = DockStyle.Fill, ColumnCount = 1 };
        AddLabeled(stack, "Title:", _title);

        if (_type == PilotVocab.Value)
        {
            var statement = Box(attrs.GetValueOrDefault("statement") ?? subject.Statement, 110);
            _fields["statement"] = statement;
            AddLabeled(stack, "Statement:", statement);
        }

        foreach (var (label, key, multiline) in FieldsFor(_type))
        {
            var box = Box(attrs.GetValueOrDefault(key), multiline ? 70 : 0);
            _fields[key] = box;
            AddLabeled(stack, label, box);
        }

        if (_type != PilotVocab.Area)
        {
            _area = new AreaSelector(reader);
            _area.SelectUrn(subject.Area);
            AddLabeled(stack, "Area:", _area);
        }

        if (_type == PilotVocab.Person)
        {
            _personKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            _personKind.Items.AddRange(["human", "ai"]);
            var kind = attrs.GetValueOrDefault("person_kind") ?? "human";
            _personKind.SelectedItem = kind;
            if (_personKind.SelectedIndex < 0)
            {
                _personKind.SelectedIndex = 0;
            }

            AddLabeled(stack, "Human / AI:", _personKind);
        }

        if (_type == PilotVocab.Habit)
        {
            _allowsPartial = new CheckBox
            {
                Text = "Allows partial credit",
                AutoSize = true,
                Checked = attrs.GetValueOrDefault("allows_partial") == "true"
            };
            stack.Controls.Add(_allowsPartial);
            _recurrence = new RecurrenceEditor();
            _recurrence.LoadFromJson(attrs.GetValueOrDefault("recurrence"));
            AddLabeled(stack, "Recurrence:", _recurrence);
        }

        if (_type == PilotVocab.Appointment)
        {
            _recurrence = new RecurrenceEditor();
            _recurrence.LoadFromJson(attrs.GetValueOrDefault("recurrence"));
            AddLabeled(stack, "Recurrence:", _recurrence);
        }

        _title.ReadOnly = true; // title is a first-class column; bsk set only patches attributes
        var save = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        save.Click += (_, _) => DoSave();
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        Controls.Add(stack);
        Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void DoSave()
    {
        try
        {
            var values = new Dictionary<string, string?>();
            foreach (var (key, box) in _fields)
            {
                values[key] = box.Text.Trim();
            }

            if (_area is not null)
            {
                values["area"] = _area.SelectedUrn ?? "";
            }

            if (_personKind is not null)
            {
                values["person_kind"] = _personKind.SelectedItem?.ToString() ?? "human";
            }

            if (_allowsPartial is not null)
            {
                values["allows_partial"] = _allowsPartial.Checked ? "true" : "false";
            }

            BskWrites.SetAttributes(_bsk, _urn, values);
            if (_recurrence is not null)
            {
                BskWrites.ApplyRecurrence(_bsk, _urn, _recurrence.ToCliArgs());
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (BskException ex)
        {
            Ui.ShowError(this, "Save failed", ex);
        }
    }

    private static IEnumerable<(string Label, string Key, bool Multiline)> FieldsFor(string type) => type switch
    {
        PilotVocab.Value => [("Why it matters:", "why_it_matters", true), ("Notes:", "notes", true)],
        PilotVocab.Goal =>
        [
            ("Desired end state:", "desired_end_state", true),
            ("Target date:", "target_date", false),
            ("Description:", "description", true),
            ("Motivation:", "motivation", true)
        ],
        PilotVocab.Project =>
        [
            ("Description / scope:", "description", true),
            ("Start date:", "start_date", false),
            ("Target / due date:", "target_date", false),
            ("Notes:", "notes", true)
        ],
        PilotVocab.Task =>
        [
            ("Description / notes:", "description", true),
            ("Due date:", "due", false),
            ("Scheduled / do date:", "scheduled", false),
            ("Estimated duration:", "estimated_duration", false)
        ],
        PilotVocab.Problem =>
        [
            ("Description:", "description", true),
            ("Date identified:", "date_identified", false),
            ("Impact:", "impact", true)
        ],
        PilotVocab.Decision =>
        [
            ("Description:", "description", true),
            ("Decision date:", "decision_date", false)
        ],
        PilotVocab.Person =>
        [
            ("Role or relationship:", "role", false),
            ("Description:", "description", true),
            ("Contact / reference:", "contact", false),
            ("Notes:", "notes", true)
        ],
        PilotVocab.Area => [("Description:", "description", true), ("Notes:", "notes", true)],
        PilotVocab.Habit =>
        [
            ("Cue:", "cue", false),
            ("Routine:", "routine", true),
            ("Reward:", "reward", false),
            ("Start date:", "start", false),
            ("End date:", "end", false)
        ],
        PilotVocab.Appointment =>
        [
            ("Date:", "date", false),
            ("Start time:", "start", false),
            ("End time:", "end", false),
            ("All day (true/false):", "all_day", false),
            ("Location:", "location", false),
            ("Meeting link:", "meeting_link", false),
            ("Notes:", "notes", true)
        ],
        _ => [("Description:", "description", true), ("Notes:", "notes", true)]
    };

    private static TextBox Box(string? value, int multilineHeight)
    {
        var box = new TextBox { Text = value ?? "", Width = 460 };
        if (multilineHeight > 0)
        {
            box.Multiline = true;
            box.Height = multilineHeight;
            box.AcceptsReturn = true;
            box.ScrollBars = ScrollBars.Vertical;
        }

        return box;
    }

    private static void AddLabeled(TableLayoutPanel stack, string label, Control control)
    {
        stack.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 2) });
        stack.Controls.Add(control);
    }
}
