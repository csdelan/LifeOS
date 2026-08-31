namespace LifeOs.Pilot.Dialogs;

/// <summary>
/// Edits a subject's date/cadence attributes — due / do-on date, next review date,
/// and cadence — for <c>bsk set</c>. A blank field clears that attribute. Content-
/// sized so nothing clips at any DPI.
/// </summary>
public sealed class AttributesDialog : Form
{
    private readonly TextBox _due = new();
    private readonly TextBox _review = new();
    private readonly TextBox _cadence = new();

    public AttributesDialog(string subjectLabel, string? due, string? review, string? cadence)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Set dates & cadence";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        _due.Text = due ?? "";
        _review.Text = review ?? "";
        _cadence.Text = cadence ?? "";

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430));
        for (var row = 0; row < 8; row++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var header = new Label
        {
            Text = subjectLabel,
            AutoSize = true,
            MaximumSize = new Size(430, 0),
            Margin = new Padding(0, 0, 0, 10),
            Font = new Font(Font, FontStyle.Bold)
        };

        foreach (var box in new[] { _due, _review, _cadence })
        {
            box.Width = 430;
            box.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            box.Margin = new Padding(0, 0, 0, 10);
        }

        var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };
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

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(FieldLabel("Due / do-on date (e.g. 2026-09-02 — blank to clear):"), 0, 1);
        root.Controls.Add(_due, 0, 2);
        root.Controls.Add(FieldLabel("Next review date:"), 0, 3);
        root.Controls.Add(_review, 0, 4);
        root.Controls.Add(FieldLabel("Cadence (e.g. weekly, 10 days):"), 0, 5);
        root.Controls.Add(_cadence, 0, 6);
        root.Controls.Add(buttons, 0, 7);
        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Due => _due.Text.Trim();

    public string Review => _review.Text.Trim();

    public string Cadence => _cadence.Text.Trim();

    private static Label FieldLabel(string text)
        => new() { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
}
