namespace LifeOs.Pilot.Dialogs;

/// <summary>
/// Captures a new Value as two parts: a short <em>handle</em> (the title, which
/// drives the URN slug and is what edges reference) and the full first-person
/// <em>identity statement</em>. Both are required — a Value with no statement is
/// rejected by the kernel (migration 0010), so the OK button stays disabled until
/// both fields are non-empty.
/// </summary>
public sealed class ValueDialog : Form
{
    private readonly TextBox _handle = new();
    private readonly TextBox _statement = new();
    private readonly Button _ok = new() { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, Enabled = false };

    public ValueDialog()
    {
        // Content-sized so nothing clips at any DPI (matches InputDialog).
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "New Value";
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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 460));
        for (var row = 0; row < 5; row++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var handleLabel = new Label
        {
            Text = "Handle — a short name for this value (becomes the slug):",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Margin = new Padding(0, 0, 0, 4)
        };
        _handle.Width = 460;
        _handle.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _handle.Margin = new Padding(0, 0, 0, 12);

        var statementLabel = new Label
        {
            Text = "Statement — who you've chosen to be, in your own words:",
            AutoSize = true,
            MaximumSize = new Size(460, 0),
            Margin = new Padding(0, 0, 0, 4)
        };
        _statement.Width = 460;
        _statement.Height = 90;
        _statement.Multiline = true;
        _statement.AcceptsReturn = true;
        _statement.ScrollBars = ScrollBars.Vertical;
        _statement.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _statement.Margin = new Padding(0, 0, 0, 12);

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
        buttons.Controls.Add(_ok);

        root.Controls.Add(handleLabel, 0, 0);
        root.Controls.Add(_handle, 0, 1);
        root.Controls.Add(statementLabel, 0, 2);
        root.Controls.Add(_statement, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        Controls.Add(root);

        _handle.TextChanged += (_, _) => UpdateOk();
        _statement.TextChanged += (_, _) => UpdateOk();

        AcceptButton = _ok;
        CancelButton = cancel;
    }

    /// <summary>The short handle — becomes the subject title (and slug).</summary>
    public string HandleText => _handle.Text.Trim();

    /// <summary>The full identity statement — stored in attributes.statement.</summary>
    public string Statement => _statement.Text.Trim();

    private void UpdateOk() => _ok.Enabled = HandleText.Length > 0 && Statement.Length > 0;
}
