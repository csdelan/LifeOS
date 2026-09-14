using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;
using LifeOs.Pilot.Views;

namespace LifeOs.Pilot;

/// <summary>
/// NAV-1 shell: wrapping destination strip, open-on-Dashboard, per-tab remembered
/// view state, global New, dirty-edit prompt on destination change.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BlueSkies", "Pilot");

    private static readonly string PlacementPath = Path.Combine(DataDir, "window-placement.json");
    private static readonly string ViewStatePath = Path.Combine(DataDir, "view-state.json");

    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly Navigator _navigator = new();
    private readonly ViewStateStore _viewState = ViewStateStore.Load(ViewStatePath);
    private readonly FlowLayoutPanel _destinations = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        WrapContents = true,
        Padding = new Padding(6, 2, 6, 2)
    };
    private readonly Panel _host = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<string, (Control Control, IPilotView View)> _tabs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RadioButton> _buttons = new(StringComparer.Ordinal);
    private string _current = Destinations.Dashboard;
    private bool _switching;

    public MainForm(SubjectReader reader, BskCli? bsk)
    {
        _reader = reader;
        _bsk = bsk;
        Text = "BlueSkies Pilot";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 800);
        MinimumSize = new Size(820, 520);

        var newButton = Ui.Action("New…");
        newButton.Enabled = bsk is not null;
        newButton.Click += (_, _) => DoNew();
        var status = Ui.Muted(bsk is null ? "Read-only (bsk.exe not found)" : "Ready");

        var banner = Ui.Toolbar();
        banner.Controls.Add(newButton);
        banner.Controls.Add(status);

        foreach (var name in Destinations.All)
        {
            var button = new RadioButton
            {
                Text = name,
                Appearance = Appearance.Button,
                AutoSize = true,
                Margin = new Padding(2),
                Checked = name == Destinations.Dashboard
            };
            var captured = name;
            button.Click += (_, _) => TrySwitch(captured);
            _buttons[name] = button;
            _destinations.Controls.Add(button);
        }

        BuildTabs();
        _navigator.Navigated += OnNavigate;

        Controls.Add(_host);
        Controls.Add(_destinations);
        Controls.Add(banner);
        ShowTab(Destinations.Dashboard, selectId: null, force: true);
    }

    private void BuildTabs()
    {
        Add(Destinations.Dashboard, new DashboardView(_reader, _bsk, _navigator));
        Add(Destinations.Inbox, new InboxView(_reader, _bsk));
        Add(Destinations.Tasks, new TasksView(_reader, _bsk, _navigator));
        Add(Destinations.Projects, new TypedListView(_reader, _bsk, _navigator, PilotVocab.Project, Destinations.Projects));
        Add(Destinations.Goals, new TypedListView(_reader, _bsk, _navigator, PilotVocab.Goal, Destinations.Goals));
        Add(Destinations.Habits, new HabitsView(_reader, _bsk, _navigator));
        Add(Destinations.Reviews, new ReviewsView());
        Add(Destinations.Vision, new VisionView(_reader, _bsk, _navigator));
        Add(Destinations.Browse, new BrowseView(_reader, _bsk, _navigator));
        Add(Destinations.Areas, new AreasView(_reader, _bsk, _navigator));
        Add(Destinations.People, new PeopleView(_reader, _bsk, _navigator));

        foreach (var (control, view) in _tabs.Values)
        {
            control.Dock = DockStyle.Fill;
            control.Visible = false;
            _host.Controls.Add(control);
            view.RestoreViewState(_viewState);
        }
    }

    private void Add(string name, Control control)
    {
        if (control is not IPilotView view)
        {
            throw new InvalidOperationException($"{name} is not an IPilotView.");
        }

        _tabs[name] = (control, view);
    }

    private void OnNavigate(string destination, Guid? id)
        => TrySwitch(destination, id);

    private void TrySwitch(string destination, Guid? selectId = null)
    {
        if (_switching || destination == _current && selectId is null)
        {
            if (selectId is { } already && destination == _current)
            {
                _tabs[_current].View.SelectSubject(already);
            }

            return;
        }

        if (!Ui.ConfirmLeave(this, _tabs[_current].View))
        {
            _switching = true;
            _buttons[_current].Checked = true;
            _switching = false;
            return;
        }

        ShowTab(destination, selectId, force: false);
    }

    private void ShowTab(string destination, Guid? selectId, bool force)
    {
        _switching = true;
        _tabs[_current].View.SaveViewState(_viewState);
        foreach (var (name, (control, view)) in _tabs)
        {
            var on = name == destination;
            control.Visible = on;
            if (on)
            {
                control.BringToFront();
                view.Reload();
                if (selectId is { } id)
                {
                    view.SelectSubject(id);
                }
            }
        }

        _current = destination;
        _buttons[destination].Checked = true;
        _switching = false;
        _ = force;
    }

    private void DoNew()
    {
        if (_bsk is null)
        {
            Ui.WarnNoBsk(this);
            return;
        }

        var created = NewSubjectDialog.ShowNew(this, _reader, _bsk);
        if (created is not null)
        {
            _tabs[_current].View.Reload();
            MessageBox.Show(this, $"Created {PilotVocab.Label(created.Type)} “{created.Title}”.",
                "Created", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        WindowPlacement.Restore(this, PlacementPath);
        // NAV-1: always open on Dashboard — do not restore last destination.
        ShowTab(Destinations.Dashboard, selectId: null, force: true);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!Ui.ConfirmLeave(this, _tabs[_current].View))
        {
            e.Cancel = true;
            return;
        }

        foreach (var (_, view) in _tabs.Values)
        {
            view.SaveViewState(_viewState);
        }

        _viewState.Save();
        WindowPlacement.Save(this, PlacementPath);
        base.OnFormClosing(e);
    }
}
