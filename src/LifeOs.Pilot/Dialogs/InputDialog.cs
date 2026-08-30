namespace LifeOs.Pilot.Dialogs;

/// <summary>A minimal one-line text prompt (WinForms has no built-in InputBox).</summary>
public sealed class InputDialog : Form
{
    private readonly TextBox _input = new();

    private InputDialog(string title, string prompt, string initial)
    {
        // Content-sized so nothing clips at any DPI: the form grows to fit the
        // TableLayoutPanel rather than relying on hand-tuned pixel heights.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = title;
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
            RowCount = 3
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
        for (var row = 0; row < 3; row++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var label = new Label
        {
            Text = prompt,
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Margin = new Padding(0, 0, 0, 8)
        };

        _input.Text = initial;
        _input.Width = 400;
        _input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _input.Margin = new Padding(0, 0, 0, 12);

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
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

        root.Controls.Add(label, 0, 0);
        root.Controls.Add(_input, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    private string Value => _input.Text.Trim();

    /// <summary>Shows the prompt; returns the trimmed non-empty value, or null if cancelled/blank.</summary>
    public static string? Show(IWin32Window owner, string title, string prompt, string initial = "")
    {
        using var dialog = new InputDialog(title, prompt, initial);
        return dialog.ShowDialog(owner) == DialogResult.OK && dialog.Value.Length > 0 ? dialog.Value : null;
    }
}
