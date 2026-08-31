namespace LifeOs.Pilot.Dialogs;

/// <summary>
/// Chooses the subject type and title to promote a capture into, for
/// <c>bsk promote &lt;event-id&gt; &lt;type&gt; "title"</c>. Content-sized so nothing clips.
/// </summary>
public sealed class PromoteDialog : Form
{
    private static readonly string[] Types =
    [
        "Project", "Idea", "Problem", "Task", "Goal",
        "Decision", "Commitment", "Person", "Constraint", "Season", "Value"
    ];

    private readonly ComboBox _type = new();
    private readonly TextBox _title = new();

    public PromoteDialog(string suggestedTitle)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Promote capture";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430));
        for (var row = 0; row < 5; row++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var typeLabel = new Label { Text = "Promote to type:", AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
        _type.DropDownStyle = ComboBoxStyle.DropDownList;
        _type.Items.AddRange(Types);
        _type.SelectedIndex = 0;
        _type.Width = 200;
        _type.Margin = new Padding(0, 0, 0, 10);

        var titleLabel = new Label { Text = "Title:", AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
        _title.Text = suggestedTitle;
        _title.Width = 430;
        _title.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _title.Margin = new Padding(0, 0, 0, 10);

        var ok = new Button { Text = "Promote", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        root.Controls.Add(typeLabel, 0, 0);
        root.Controls.Add(_type, 0, 1);
        root.Controls.Add(titleLabel, 0, 2);
        root.Controls.Add(_title, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string SubjectType => _type.SelectedItem?.ToString() ?? "Project";

    public string TitleText => _title.Text.Trim();
}
