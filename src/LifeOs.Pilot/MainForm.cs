using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Dialogs;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;
using LifeOs.Pilot.Views;

namespace LifeOs.Pilot;

/// <summary>
/// NAV-1 shell: wrapping destination strip, open-on-Dashboard, per-tab remembered
/// view state, global New, dirty-edit prompt on destination change. Also carries
/// the DEV/STAGING environment indicator and selector (top-right + title bar).
/// </summary>
public sealed class MainForm : Form
{
    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly PilotEnvironment _environment;
    private readonly EnvironmentSettings _settings;
    private readonly Navigator _navigator = new();
    private readonly ViewStateStore _viewState = ViewStateStore.Load(PilotPaths.ViewState);
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

    // Environment indicator/selector (top-right of the window).
    private readonly Label _envBadge = new();
    private readonly ComboBox _envSelector = new();
    private readonly Button _envFileButton = new();
    private readonly ToolTip _tips = new();

    private string _current = Destinations.Dashboard;
    private bool _switching;
    private bool _wiringEnv;
    private bool _restarting;

    internal MainForm(SubjectReader reader, BskCli? bsk, PilotEnvironment environment, EnvironmentSettings settings)
    {
        _reader = reader;
        _bsk = bsk;
        _environment = environment;
        _settings = settings;
        Text = $"BlueSkies Pilot — {PilotEnvironmentInfo.Label(environment)}";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1280, 800);
        MinimumSize = new Size(820, 520);

        var newButton = Ui.Action("New…");
        newButton.Enabled = bsk is not null;
        newButton.Click += (_, _) => DoNew();
        var status = Ui.Muted(bsk is null ? "Read-only (bsk.exe not found)" : "Ready");

        var banner = Ui.Toolbar();
        banner.Dock = DockStyle.Fill;
        banner.Controls.Add(newButton);
        banner.Controls.Add(status);

        var topBar = BuildTopBar(banner);

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
        Controls.Add(topBar);
        ShowTab(Destinations.Dashboard, selectId: null, force: true);
    }

    /// <summary>
    /// A two-column top bar: the existing toolbar fills the left, and the
    /// environment badge + selector sit right-aligned at the top-right.
    /// </summary>
    private TableLayoutPanel BuildTopBar(Control banner)
    {
        var envCluster = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0),
            Padding = new Padding(4, 4, 6, 4)
        };

        _envBadge.AutoSize = true;
        _envBadge.Text = PilotEnvironmentInfo.Label(_environment);
        _envBadge.Font = new Font(_envBadge.Font, FontStyle.Bold);
        _envBadge.ForeColor = PilotEnvironmentInfo.Foreground;
        _envBadge.BackColor = PilotEnvironmentInfo.Background(_environment);
        _envBadge.TextAlign = ContentAlignment.MiddleCenter;
        _envBadge.Padding = new Padding(8, 4, 8, 4);
        _envBadge.Margin = new Padding(0, 3, 6, 0);

        _envSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _envSelector.Width = 104;
        _envSelector.Margin = new Padding(0, 2, 4, 0);
        _envSelector.Items.Add(new EnvItem(PilotEnvironment.Dev));
        _envSelector.Items.Add(new EnvItem(PilotEnvironment.Staging));
        _wiringEnv = true;
        _envSelector.SelectedIndex = _environment == PilotEnvironment.Staging ? 1 : 0;
        _wiringEnv = false;
        _envSelector.SelectedIndexChanged += OnEnvSelectorChanged;

        _envFileButton.Text = "…";
        _envFileButton.AutoSize = false;
        _envFileButton.Width = 30;
        _envFileButton.Margin = new Padding(0, 2, 0, 0);
        _envFileButton.Click += (_, _) => ChooseEnvFile();

        envCluster.Controls.Add(_envBadge);
        envCluster.Controls.Add(_envSelector);
        envCluster.Controls.Add(_envFileButton);
        UpdateEnvTooltips();

        var topBar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 1
        };
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        topBar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        topBar.Controls.Add(banner, 0, 0);
        topBar.Controls.Add(envCluster, 1, 0);
        return topBar;
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

    // ---- Environment switch (top-right selector) --------------------------------

    private void OnEnvSelectorChanged(object? sender, EventArgs e)
    {
        if (_wiringEnv || _envSelector.SelectedItem is not EnvItem item || item.Environment == _environment)
        {
            return;
        }

        SwitchEnvironment(item.Environment);
    }

    private void SwitchEnvironment(PilotEnvironment target)
    {
        var label = PilotEnvironmentInfo.Label(target);

        if (target == PilotEnvironment.Staging && string.IsNullOrWhiteSpace(_settings.EnvFilePath))
        {
            MessageBox.Show(this,
                "No .env file is set for STAGING. Choose one with the “…” button first.",
                "Staging", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RevertEnvSelector();
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Switch to {label} and restart the app?\n\nThe pilot will reconnect to the {label} database.",
            "Switch environment", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm != DialogResult.OK)
        {
            RevertEnvSelector();
            return;
        }

        // Honour unsaved edits before tearing the window down.
        if (!Ui.ConfirmLeave(this, _tabs[_current].View))
        {
            RevertEnvSelector();
            return;
        }

        _settings.Environment = target;
        _settings.Save();
        _restarting = true;
        Application.Restart();
    }

    private void RevertEnvSelector()
    {
        _wiringEnv = true;
        _envSelector.SelectedIndex = _environment == PilotEnvironment.Staging ? 1 : 0;
        _wiringEnv = false;
    }

    private void ChooseEnvFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose the .env file for STAGING",
            Filter = "Env files (.env, *.env)|.env;*.env|All files (*.*)|*.*",
            CheckFileExists = true
        };

        var current = _settings.EnvFilePath;
        if (!string.IsNullOrWhiteSpace(current))
        {
            var directory = Path.GetDirectoryName(current);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
                dialog.FileName = Path.GetFileName(current);
            }
        }

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _settings.EnvFilePath = dialog.FileName;
        _settings.Save();
        UpdateEnvTooltips();

        if (_environment != PilotEnvironment.Staging)
        {
            return;
        }

        var reload = MessageBox.Show(this,
            "Apply the new .env now? This restarts the app.",
            "Staging .env changed", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (reload == DialogResult.OK && Ui.ConfirmLeave(this, _tabs[_current].View))
        {
            _restarting = true;
            Application.Restart();
        }
    }

    private void UpdateEnvTooltips()
    {
        var path = string.IsNullOrWhiteSpace(_settings.EnvFilePath) ? "(none chosen)" : _settings.EnvFilePath;
        _tips.SetToolTip(_envFileButton,
            $"STAGING reads credentials from:\n{path}\n\nClick to choose a different .env file.");
        _tips.SetToolTip(_envSelector, $"Current database: {PilotEnvironmentInfo.Label(_environment)}");
        _tips.SetToolTip(_envBadge, $"Running against the {PilotEnvironmentInfo.Label(_environment)} database.");
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        WindowPlacement.Restore(this, PilotPaths.Placement);
        // Tint the OS title bar to match the environment (Windows 11+).
        TitleBarTint.Apply(this, PilotEnvironmentInfo.Background(_environment), PilotEnvironmentInfo.Foreground);
        // NAV-1: always open on Dashboard — do not restore last destination.
        ShowTab(Destinations.Dashboard, selectId: null, force: true);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // A restart has already run the dirty-edit prompt; don't ask twice.
        if (!_restarting && !Ui.ConfirmLeave(this, _tabs[_current].View))
        {
            e.Cancel = true;
            return;
        }

        foreach (var (_, view) in _tabs.Values)
        {
            view.SaveViewState(_viewState);
        }

        _viewState.Save();
        WindowPlacement.Save(this, PilotPaths.Placement);
        base.OnFormClosing(e);
    }

    /// <summary>A selector row that shows the environment's label but carries its value.</summary>
    private sealed record EnvItem(PilotEnvironment Environment)
    {
        public override string ToString() => PilotEnvironmentInfo.Label(Environment);
    }
}
