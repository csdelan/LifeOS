namespace LifeOs.Pilot.Dialogs;

/// <summary>
/// Picks a subject-to-subject relation and a target, for <c>bsk link &lt;from&gt;
/// &lt;relation&gt; &lt;to&gt;</c>. The "from" side is the subject currently open in Browse.
/// </summary>
public sealed class LinkDialog : Form
{
    private readonly ComboBox _relation = new();
    private readonly TextBox _target = new();

    public LinkDialog(string fromLabel)
    {
        // Content-sized so nothing clips at any DPI.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;

        Text = "Link subject";
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
            RowCount = 6
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 420));
        for (var row = 0; row < 6; row++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var from = new Label
        {
            Text = $"From:  {fromLabel}",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
            Margin = new Padding(0, 0, 0, 12)
        };

        var relationLabel = new Label { Text = "Relation:", AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
        _relation.DropDownStyle = ComboBoxStyle.DropDownList;
        _relation.Items.AddRange(["serves", "results_in", "supersedes"]);
        _relation.SelectedIndex = 0;
        _relation.Width = 180;
        _relation.Margin = new Padding(0, 0, 0, 12);

        var targetLabel = new Label { Text = "To (title, URN, or short id):", AutoSize = true, Margin = new Padding(0, 0, 0, 2) };
        _target.Width = 420;
        _target.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _target.Margin = new Padding(0, 0, 0, 12);

        var ok = new Button { Text = "Link", DialogResult = DialogResult.OK, AutoSize = true };
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

        root.Controls.Add(from, 0, 0);
        root.Controls.Add(relationLabel, 0, 1);
        root.Controls.Add(_relation, 0, 2);
        root.Controls.Add(targetLabel, 0, 3);
        root.Controls.Add(_target, 0, 4);
        root.Controls.Add(buttons, 0, 5);
        Controls.Add(root);

        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string Relation => _relation.SelectedItem?.ToString() ?? "serves";

    public string Target => _target.Text.Trim();
}
