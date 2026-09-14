using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Controls;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Dialogs;

/// <summary>
/// GEN-6 global New / GEN-7 parent-first child: type picker (when unlocked) then a
/// type-specific form. Save is one <c>bsk new</c> (plus follow-up tag/link/recur).
/// Cancel writes nothing. Switching type before save keeps still-applicable values.
/// </summary>
public sealed class NewSubjectDialog : Form
{
    private readonly SubjectReader _reader;
    private readonly BskCli _bsk;
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly Panel _fieldsHost = new() { AutoScroll = true, Dock = DockStyle.Fill };
    private readonly TextBox _title = new() { Width = 460 };
    private readonly TextBox _statement = Multiline(110);
    private readonly TextBox _description = Multiline(80);
    private readonly TextBox _notes = Multiline(60);
    private readonly TextBox _motivation = Multiline(60);
    private readonly TextBox _endState = Multiline(60);
    private readonly TextBox _targetDate = new() { Width = 160, PlaceholderText = "YYYY-MM-DD" };
    private readonly TextBox _startDate = new() { Width = 160, PlaceholderText = "YYYY-MM-DD" };
    private readonly TextBox _due = new() { Width = 160, PlaceholderText = "YYYY-MM-DD" };
    private readonly TextBox _scheduled = new() { Width = 160, PlaceholderText = "YYYY-MM-DD" };
    private readonly TextBox _decisionDate = new() { Width = 160, PlaceholderText = "YYYY-MM-DD" };
    private readonly TextBox _identified = new() { Width = 160, PlaceholderText = "YYYY-MM-DD" };
    private readonly TextBox _impact = Multiline(50);
    private readonly TextBox _why = Multiline(50);
    private readonly TextBox _duration = new() { Width = 160, PlaceholderText = "e.g. 30m" };
    private readonly TextBox _cue = new() { Width = 460 };
    private readonly TextBox _routine = Multiline(50);
    private readonly TextBox _reward = new() { Width = 460 };
    private readonly TextBox _habitStart = new() { Width = 160, PlaceholderText = "YYYY-MM-DD" };
    private readonly TextBox _habitEnd = new() { Width = 160, PlaceholderText = "YYYY-MM-DD" };
    private readonly CheckBox _allowsPartial = new() { Text = "Allows partial credit", AutoSize = true };
    private readonly TextBox _apptDate = new() { Width = 160, PlaceholderText = "YYYY-MM-DD" };
    private readonly TextBox _startTime = new() { Width = 100, PlaceholderText = "HH:MM" };
    private readonly TextBox _endTime = new() { Width = 100, PlaceholderText = "HH:MM" };
    private readonly CheckBox _allDay = new() { Text = "All day", AutoSize = true };
    private readonly TextBox _location = new() { Width = 460 };
    private readonly TextBox _meetingLink = new() { Width = 460 };
    private readonly ComboBox _personKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly TextBox _personRole = new() { Width = 460 };
    private readonly TextBox _contact = new() { Width = 460 };
    private readonly AreaSelector _area;
    private readonly RecurrenceEditor _recurrence = new();
    private readonly TagEditor _tags;
    private readonly RelationshipEditor _relationships;
    private readonly PersonPicker _attendee;
    private readonly Label _parentHint = new() { AutoSize = true, MaximumSize = new Size(520, 0) };
    private readonly Button _save = new() { Text = "Save", DialogResult = DialogResult.None, AutoSize = true };
    private readonly string? _lockedType;
    private readonly string? _parentUrn;
    private readonly string? _parentType;
    private readonly string? _parentTitle;
    private string _currentType;
    private bool _rebuilding;

    private NewSubjectDialog(
        SubjectReader reader,
        BskCli bsk,
        string? lockedType,
        string? parentUrn,
        string? parentType,
        string? parentTitle,
        string? presetArea)
    {
        _reader = reader;
        _bsk = bsk;
        _lockedType = lockedType;
        _parentUrn = parentUrn;
        _parentType = parentType;
        _parentTitle = parentTitle;
        _area = new AreaSelector(reader);
        _tags = new TagEditor(reader, bsk: null); // deferred until Save
        _relationships = new RelationshipEditor(reader, bsk: null);
        _attendee = new PersonPicker(reader);
        _personKind.Items.AddRange(["human", "ai"]);
        _personKind.SelectedIndex = 0;
        _currentType = lockedType ?? PilotVocab.Task;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = parentUrn is null ? "New" : $"New child of {_parentTitle ?? _parentType}";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(580, 720);
        MinimumSize = new Size(520, 480);
        Padding = new Padding(12);

        var types = lockedType is not null
            ? new[] { lockedType }
            : parentType is not null
                ? PilotVocab.ChildTypesFor(parentType).ToArray()
                : PilotVocab.CreatableTypes;

        foreach (var type in types)
        {
            _type.Items.Add(PilotVocab.Label(type));
        }

        _type.SelectedIndex = 0;
        _currentType = types[0];
        _type.Enabled = types.Length > 1 && lockedType is null;
        _type.SelectedIndexChanged += (_, _) => OnTypeChanged(types);

        if (presetArea is not null)
        {
            _area.SelectUrn(presetArea);
        }

        _save.Click += (_, _) => DoSave();
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        AcceptButton = _save;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_save);

        var typeRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
        typeRow.Controls.Add(new Label { Text = "Type", AutoSize = true, Padding = new Padding(0, 6, 8, 0) });
        typeRow.Controls.Add(_type);

        Controls.Add(_fieldsHost);
        Controls.Add(buttons);
        Controls.Add(typeRow);

        _allDay.CheckedChanged += (_, _) =>
        {
            _startTime.Enabled = !_allDay.Checked;
            _endTime.Enabled = !_allDay.Checked;
        };

        RebuildFields();
        _title.Focus();
    }

    public CreatedSubject? CreatedSubject { get; private set; }

    public static CreatedSubject? ShowNew(IWin32Window owner, SubjectReader reader, BskCli bsk)
    {
        using var dialog = new NewSubjectDialog(reader, bsk, null, null, null, null, null);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.CreatedSubject : null;
    }

    public static CreatedSubject? ShowNewOfType(
        IWin32Window owner, SubjectReader reader, BskCli bsk, string type, string? presetArea = null)
    {
        using var dialog = new NewSubjectDialog(reader, bsk, type, null, null, null, presetArea);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.CreatedSubject : null;
    }

    public static CreatedSubject? ShowChild(
        IWin32Window owner, SubjectReader reader, BskCli bsk,
        string parentUrn, string parentType, string parentTitle, string? childType = null)
    {
        var children = PilotVocab.ChildTypesFor(parentType);
        if (children.Count == 0)
        {
            MessageBox.Show(owner, "This item cannot have children (a Task is a leaf).", "New child",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        using var dialog = new NewSubjectDialog(
            reader, bsk, childType, parentUrn, parentType, parentTitle, presetArea: null);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.CreatedSubject : null;
    }

    /// <summary>Title-only Task quick create (GEN-10 / TASKS-1). Returns null if cancelled/blank.</summary>
    public static CreatedSubject? ShowQuickTask(IWin32Window owner, BskCli bsk, string? parentUrn = null)
    {
        var title = InputDialog.Show(owner, "New Task", "Title:");
        if (title is null)
        {
            return null;
        }

        try
        {
            var args = new List<string> { "new", "Task", title, "--attr", "priority=Medium" };
            if (parentUrn is not null)
            {
                args.Add("--parent");
                args.Add(parentUrn);
            }

            return bsk.RunJson<CreatedSubject>(args.ToArray());
        }
        catch (BskException ex)
        {
            Ui.ShowError(owner, "New Task failed", ex);
            return null;
        }
    }

    private void OnTypeChanged(string[] types)
    {
        if (_rebuilding || _type.SelectedIndex < 0)
        {
            return;
        }

        _currentType = types[_type.SelectedIndex];
        RebuildFields();
    }

    private void RebuildFields()
    {
        _rebuilding = true;
        _fieldsHost.Controls.Clear();
        var stack = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 4, 12, 4)
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        if (_parentUrn is not null)
        {
            var relation = PilotVocab.InferChildRelation(_currentType, _parentType ?? "") ?? "related";
            _parentHint.Text = $"Will be created under {_parentType} “{_parentTitle}” ({relation}).";
            AddRow(stack, _parentHint);
        }

        AddLabeled(stack, TitleLabel(), _title);

        switch (_currentType)
        {
            case PilotVocab.Value:
                AddLabeled(stack, "Statement / description (first-person identity):", _statement);
                AddLabeled(stack, "Why it matters:", _why);
                AddLabeled(stack, "Area:", _area);
                AddLabeled(stack, "Notes:", _notes);
                break;
            case PilotVocab.Goal:
                AddLabeled(stack, "Desired end state:", _endState);
                AddLabeled(stack, "Target date:", _targetDate);
                AddLabeled(stack, "Area:", _area);
                AddLabeled(stack, "Description:", _description);
                AddLabeled(stack, "Motivation:", _motivation);
                break;
            case PilotVocab.Project:
                AddLabeled(stack, "Description / scope:", _description);
                AddLabeled(stack, "Start date:", _startDate);
                AddLabeled(stack, "Target / due date:", _targetDate);
                AddLabeled(stack, "Area:", _area);
                AddLabeled(stack, "Notes:", _notes);
                break;
            case PilotVocab.Task:
                AddLabeled(stack, "Description / notes:", _description);
                AddLabeled(stack, "Due date (deadline):", _due);
                AddLabeled(stack, "Scheduled / do date (intent):", _scheduled);
                AddLabeled(stack, "Area:", _area);
                AddLabeled(stack, "Estimated duration:", _duration);
                break;
            case PilotVocab.Problem:
                AddLabeled(stack, "Description:", _description);
                AddLabeled(stack, "Date identified:", _identified);
                AddLabeled(stack, "Area:", _area);
                AddLabeled(stack, "Impact:", _impact);
                break;
            case PilotVocab.Decision:
                AddLabeled(stack, "Description (reasoning / consequences):", _description);
                AddLabeled(stack, "Decision date:", _decisionDate);
                AddLabeled(stack, "Area:", _area);
                break;
            case PilotVocab.Idea:
                AddLabeled(stack, "Description:", _description);
                AddLabeled(stack, "Area:", _area);
                break;
            case PilotVocab.Person:
                AddLabeled(stack, "Human / AI:", _personKind);
                AddLabeled(stack, "Role or relationship:", _personRole);
                AddLabeled(stack, "Description:", _description);
                AddLabeled(stack, "Contact / reference:", _contact);
                AddLabeled(stack, "Notes:", _notes);
                break;
            case PilotVocab.Area:
                AddLabeled(stack, "Description:", _description);
                AddLabeled(stack, "Notes:", _notes);
                break;
            case PilotVocab.Habit:
                AddLabeled(stack, "Cue:", _cue);
                AddLabeled(stack, "Routine:", _routine);
                AddLabeled(stack, "Reward:", _reward);
                AddLabeled(stack, "Start date:", _habitStart);
                AddLabeled(stack, "End date:", _habitEnd);
                AddRow(stack, _allowsPartial);
                AddLabeled(stack, "Area:", _area);
                AddRow(stack, _recurrence);
                break;
            case PilotVocab.Appointment:
                AddLabeled(stack, "Date:", _apptDate);
                AddRow(stack, _allDay);
                AddLabeled(stack, "Start time:", _startTime);
                AddLabeled(stack, "End time:", _endTime);
                AddLabeled(stack, "Location:", _location);
                AddLabeled(stack, "Meeting link:", _meetingLink);
                AddLabeled(stack, "Attendee:", _attendee);
                AddLabeled(stack, "Area:", _area);
                AddLabeled(stack, "Notes:", _notes);
                AddRow(stack, _recurrence);
                break;
            default:
                AddLabeled(stack, "Description:", _description);
                AddLabeled(stack, "Area:", _area);
                AddLabeled(stack, "Notes:", _notes);
                break;
        }

        AddRow(stack, Ui.Heading("Tags"));
        _tags.Height = 80;
        AddRow(stack, _tags);
        AddRow(stack, Ui.Heading("Relationships"));
        _relationships.Height = 140;
        AddRow(stack, _relationships);

        _fieldsHost.Controls.Add(stack);
        _rebuilding = false;
    }

    private string TitleLabel() => _currentType == PilotVocab.Value
        ? "Title (short handle):"
        : _currentType == PilotVocab.Person ? "Name:" : "Title:";

    private void DoSave()
    {
        var title = _title.Text.Trim();
        if (title.Length == 0)
        {
            MessageBox.Show(this, "Title is required.", "New", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _title.Focus();
            return;
        }

        if (_currentType == PilotVocab.Value && _statement.Text.Trim().Length == 0)
        {
            MessageBox.Show(this, "An Identity Statement needs the full first-person statement.", "New",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            _statement.Focus();
            return;
        }

        if (_currentType == PilotVocab.Appointment)
        {
            if (_apptDate.Text.Trim().Length == 0 && !_recurrence.IsRecurring)
            {
                MessageBox.Show(this, "An Appointment needs a date (or a recurrence for a series).", "New",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _apptDate.Focus();
                return;
            }

            if (!_allDay.Checked && _startTime.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "Start time is required unless All day is checked.", "New",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }

        if (_currentType == PilotVocab.Habit && _parentUrn is null && _relationships.Pending.Count == 0)
        {
            MessageBox.Show(this, "A Habit must relate to at least one parent (a Goal or Identity Statement).", "New",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            var args = new List<string> { "new", _currentType, title };
            if (_area.SelectedUrn is { } area && _currentType != PilotVocab.Area)
            {
                args.Add("--area");
                args.Add(area);
            }

            if (_parentUrn is not null)
            {
                args.Add("--parent");
                args.Add(_parentUrn);
            }

            if (_currentType == PilotVocab.Value)
            {
                args.Add("--statement");
                args.Add(_statement.Text.Trim());
            }

            foreach (var (key, value) in CollectAttrs())
            {
                args.Add("--attr");
                args.Add($"{key}={value}");
            }

            var created = _bsk.RunJson<CreatedSubject>(args.ToArray());
            BskWrites.ApplyTags(_bsk, created.Urn, _tags.Tags);
            BskWrites.ApplyLinks(_bsk, created.Urn, _relationships.Pending);
            if (_currentType is PilotVocab.Habit or PilotVocab.Appointment)
            {
                var recur = _recurrence.ToCliArgs();
                if (_recurrence.IsRecurring)
                {
                    BskWrites.ApplyRecurrence(_bsk, created.Urn, recur);
                }
            }

            if (_currentType == PilotVocab.Appointment && _attendee.SelectedUrn is { } person)
            {
                _bsk.Run("involve", created.Urn, person, "--role", "attendee");
            }

            CreatedSubject = created;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (BskException ex)
        {
            Ui.ShowError(this, "Create failed", ex);
        }
    }

    private IEnumerable<(string Key, string Value)> CollectAttrs()
    {
        void Yield(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                _attrs.Add((key, value.Trim()));
            }
        }

        _attrs.Clear();
        switch (_currentType)
        {
            case PilotVocab.Value:
                Yield("why_it_matters", _why.Text);
                Yield("notes", _notes.Text);
                break;
            case PilotVocab.Goal:
                Yield("desired_end_state", _endState.Text);
                Yield("target_date", _targetDate.Text);
                Yield("description", _description.Text);
                Yield("motivation", _motivation.Text);
                break;
            case PilotVocab.Project:
                Yield("description", _description.Text);
                Yield("start_date", _startDate.Text);
                Yield("target_date", _targetDate.Text);
                Yield("notes", _notes.Text);
                break;
            case PilotVocab.Task:
                Yield("description", _description.Text);
                Yield("due", _due.Text);
                Yield("scheduled", _scheduled.Text);
                Yield("estimated_duration", _duration.Text);
                Yield("priority", "Medium");
                break;
            case PilotVocab.Problem:
                Yield("description", _description.Text);
                Yield("date_identified", string.IsNullOrWhiteSpace(_identified.Text) ? Ui.TodayIso : _identified.Text);
                Yield("impact", _impact.Text);
                break;
            case PilotVocab.Decision:
                Yield("description", _description.Text);
                Yield("decision_date", _decisionDate.Text);
                break;
            case PilotVocab.Idea:
                Yield("description", _description.Text);
                break;
            case PilotVocab.Person:
                Yield("person_kind", _personKind.SelectedItem?.ToString() ?? "human");
                Yield("role", _personRole.Text);
                Yield("description", _description.Text);
                Yield("contact", _contact.Text);
                Yield("notes", _notes.Text);
                break;
            case PilotVocab.Area:
                Yield("description", _description.Text);
                Yield("notes", _notes.Text);
                break;
            case PilotVocab.Habit:
                Yield("cue", _cue.Text);
                Yield("routine", _routine.Text);
                Yield("reward", _reward.Text);
                Yield("start", _habitStart.Text);
                Yield("end", _habitEnd.Text);
                Yield("allows_partial", _allowsPartial.Checked ? "true" : "false");
                break;
            case PilotVocab.Appointment:
                Yield("date", _apptDate.Text);
                Yield("all_day", _allDay.Checked ? "true" : "false");
                if (!_allDay.Checked)
                {
                    Yield("start", _startTime.Text);
                    Yield("end", _endTime.Text);
                }

                Yield("location", _location.Text);
                Yield("meeting_link", _meetingLink.Text);
                Yield("notes", _notes.Text);
                break;
            default:
                Yield("description", _description.Text);
                Yield("notes", _notes.Text);
                break;
        }

        return _attrs;
    }

    private readonly List<(string Key, string Value)> _attrs = [];

    private static TextBox Multiline(int height)
        => new()
        {
            Width = 460,
            Height = height,
            Multiline = true,
            AcceptsReturn = true,
            ScrollBars = ScrollBars.Vertical
        };

    private static void AddLabeled(TableLayoutPanel stack, string label, Control control)
    {
        AddRow(stack, new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 0, 2) });
        AddRow(stack, control);
    }

    private static void AddRow(TableLayoutPanel stack, Control control)
    {
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.Controls.Add(control, 0, stack.RowCount);
        stack.RowCount++;
    }
}
