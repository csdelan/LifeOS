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
    private readonly Dictionary<string, Control> _fields = new(StringComparer.Ordinal);
    private readonly AreaSelector? _area;
    private readonly ComboBox? _personKind;
    private readonly CheckBox? _allowsPartial;
    private readonly CheckBox? _allDay;
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

        CheckBox? allDay = null;
        TimeField? startTime = null;
        TimeField? endTime = null;
        foreach (var (label, key, kind) in FieldsFor(_type))
        {
            var height = key == "why_it_matters" ? 160 : 70;
            Control control = kind switch
            {
                FieldKind.Date => new DateField { IsoDate = attrs.GetValueOrDefault(key) },
                FieldKind.Time => TimeBox(attrs.GetValueOrDefault(key), optional: key == "end"),
                FieldKind.Multiline => Box(attrs.GetValueOrDefault(key), height),
                _ => Box(attrs.GetValueOrDefault(key), 0)
            };
            _fields[key] = control;
            AddLabeled(stack, label, control);

            if (control is TimeField time)
            {
                if (key == "start")
                {
                    startTime = time;
                }
                else if (key == "end")
                {
                    endTime = time;
                }
            }

            if (_type == PilotVocab.Appointment && key == "date")
            {
                allDay = new CheckBox
                {
                    Text = "All day",
                    AutoSize = true,
                    Checked = attrs.GetValueOrDefault("all_day") is "true" or "t"
                };
                stack.Controls.Add(allDay);
            }
        }

        _allDay = allDay;
        if (_allDay is not null && startTime is not null && endTime is not null)
        {
            var allDayBox = _allDay;
            var start = startTime;
            var end = endTime;
            void SyncTimes()
            {
                start.Enabled = !allDayBox.Checked;
                end.Enabled = !allDayBox.Checked;
            }

            allDayBox.CheckedChanged += (_, _) => SyncTimes();
            SyncTimes();
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
            foreach (var (key, control) in _fields)
            {
                values[key] = control switch
                {
                    DateField date => date.IsoDate ?? "",
                    TimeField time => time.IsoTime ?? "",
                    TextBox box => box.Text.Trim(),
                    _ => ""
                };
            }

            if (_allDay is not null)
            {
                values["all_day"] = _allDay.Checked ? "true" : "false";
                if (_allDay.Checked)
                {
                    values["start"] = "";
                    values["end"] = "";
                }
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

    private enum FieldKind { Text, Multiline, Date, Time }

    private static IEnumerable<(string Label, string Key, FieldKind Kind)> FieldsFor(string type) => type switch
    {
        PilotVocab.Value => [("Why it matters:", "why_it_matters", FieldKind.Multiline), ("Notes:", "notes", FieldKind.Multiline)],
        PilotVocab.Goal =>
        [
            ("Desired end state:", "desired_end_state", FieldKind.Multiline),
            ("Target date:", "target_date", FieldKind.Date),
            ("Description:", "description", FieldKind.Multiline),
            ("Motivation:", "motivation", FieldKind.Multiline)
        ],
        PilotVocab.Project =>
        [
            ("Description / scope:", "description", FieldKind.Multiline),
            ("Start date:", "start_date", FieldKind.Date),
            ("Target / due date:", "target_date", FieldKind.Date),
            ("Notes:", "notes", FieldKind.Multiline)
        ],
        PilotVocab.Task =>
        [
            ("Description / notes:", "description", FieldKind.Multiline),
            ("Due date:", "due", FieldKind.Date),
            ("Scheduled / do date:", "scheduled", FieldKind.Date),
            ("Estimated duration:", "estimated_duration", FieldKind.Text)
        ],
        PilotVocab.Problem =>
        [
            ("Description:", "description", FieldKind.Multiline),
            ("Date identified:", "date_identified", FieldKind.Date),
            ("Impact:", "impact", FieldKind.Multiline)
        ],
        PilotVocab.Decision =>
        [
            ("Description:", "description", FieldKind.Multiline),
            ("Decision date:", "decision_date", FieldKind.Date)
        ],
        PilotVocab.Person =>
        [
            ("Role or relationship:", "role", FieldKind.Text),
            ("Description:", "description", FieldKind.Multiline),
            ("Contact / reference:", "contact", FieldKind.Text),
            ("Notes:", "notes", FieldKind.Multiline)
        ],
        PilotVocab.Area => [("Description:", "description", FieldKind.Multiline), ("Notes:", "notes", FieldKind.Multiline)],
        PilotVocab.Habit =>
        [
            ("Cue:", "cue", FieldKind.Text),
            ("Routine:", "routine", FieldKind.Multiline),
            ("Reward:", "reward", FieldKind.Text),
            ("Start date:", "start", FieldKind.Date),
            ("End date:", "end", FieldKind.Date)
        ],
        PilotVocab.Appointment =>
        [
            ("Date:", "date", FieldKind.Date),
            ("Start time:", "start", FieldKind.Time),
            ("End time:", "end", FieldKind.Time),
            ("Location:", "location", FieldKind.Text),
            ("Meeting link:", "meeting_link", FieldKind.Text),
            ("Notes:", "notes", FieldKind.Multiline)
        ],
        _ => [("Description:", "description", FieldKind.Multiline), ("Notes:", "notes", FieldKind.Multiline)]
    };

    private static TimeField TimeBox(string? value, bool optional)
    {
        var field = new TimeField(optional);
        field.SetTime(value);
        return field;
    }

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
