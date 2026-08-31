using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Reader;

namespace LifeOs.Pilot;

/// <summary>
/// The single pilot window. Navigation is a <see cref="TabControl"/> holding the
/// views (Browse, Inbox — more to come); the shell owns the window and its
/// remembered placement. Switching to a tab reloads its view, so an action taken in
/// one tab (promoting a capture in Inbox, say) is reflected when you return to another.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly string PlacementPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueSkies", "Pilot", "window-placement.json");

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly BrowseView _browse;
    private readonly InboxView _inbox;

    public MainForm(SubjectReader reader, BskCli? bsk)
    {
        Text = "BlueSkies Pilot";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1180, 760);
        MinimumSize = new Size(820, 520);

        _browse = new BrowseView(reader, bsk) { Dock = DockStyle.Fill };
        _inbox = new InboxView(reader, bsk) { Dock = DockStyle.Fill };

        var browseTab = new TabPage("Browse") { Padding = new Padding(3) };
        browseTab.Controls.Add(_browse);
        var inboxTab = new TabPage("Inbox") { Padding = new Padding(3) };
        inboxTab.Controls.Add(_inbox);

        _tabs.TabPages.Add(browseTab);
        _tabs.TabPages.Add(inboxTab);
        _tabs.SelectedIndexChanged += (_, _) => OnTabChanged();

        Controls.Add(_tabs);
    }

    private void OnTabChanged()
    {
        // Each view's own OnLoad handles its first show; this refreshes it on return.
        if (_tabs.SelectedIndex == 0)
        {
            _browse.Reload();
        }
        else if (_tabs.SelectedIndex == 1)
        {
            _inbox.Reload();
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Restore the exact position, size, monitor, and maximized/normal state from
        // the previous run. Falls back to CenterScreen on first launch or if the saved
        // placement is unusable.
        WindowPlacement.Restore(this, PlacementPath);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Capture placement before teardown, so the next launch reopens where this
        // one closed. Best-effort: a save failure never blocks shutdown.
        WindowPlacement.Save(this, PlacementPath);
        base.OnFormClosing(e);
    }
}
