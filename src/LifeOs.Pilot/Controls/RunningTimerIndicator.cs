using LifeOs.Pilot.Cli;
using LifeOs.Pilot.Reader;
using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Controls;

/// <summary>
/// TASKS-2: the timer that is running right now, in the shell's top bar on every
/// screen (Toggl's header timer). Hidden while nothing runs. The title opens the Task;
/// Pause and Stop act in place. Re-reads on a timer change here and every
/// <see cref="PollSeconds"/> seconds, so a timer started from the web shows up too.
/// </summary>
internal sealed class RunningTimerIndicator : FlowLayoutPanel
{
    private const int PollSeconds = 15;
    private const int MaxTitle = 40;

    private readonly SubjectReader _reader;
    private readonly BskCli? _bsk;
    private readonly Navigator _navigator;
    private readonly Label _dot = new()
    {
        Text = "●",
        AutoSize = true,
        ForeColor = TaskTimerBar.RunningColor,
        Margin = new Padding(6, 9, 0, 0)
    };
    private readonly Label _clock = new() { AutoSize = true, Margin = new Padding(2, 9, 4, 0) };
    private readonly LinkLabel _title = new() { AutoSize = true, Margin = new Padding(0, 9, 6, 0) };
    private readonly Button _pause = Ui.Action("Pause");
    private readonly Button _stop = Ui.Action("Stop");
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private readonly ToolTip _tips = new();
    private TaskTimerRow? _running;
    private int _sincePoll;

    public RunningTimerIndicator(SubjectReader reader, BskCli? bsk, Navigator navigator)
    {
        _reader = reader;
        _bsk = bsk;
        _navigator = navigator;

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        WrapContents = false;
        Margin = new Padding(0);
        Visible = false;

        _clock.Font = new Font(_clock.Font, FontStyle.Bold);
        _clock.ForeColor = TaskTimerBar.RunningColor;
        _title.LinkClicked += (_, _) =>
        {
            if (_running is { } timer)
            {
                _navigator.OpenSubject(PilotVocab.Task, timer.SubjectId);
            }
        };
        _pause.Click += (_, _) => Run("pause");
        _stop.Click += (_, _) => Run("stop");
        _pause.Enabled = bsk is not null;
        _stop.Enabled = bsk is not null;
        _tick.Tick += (_, _) => OnTick();
        _navigator.TimersChanged += Reload;

        Controls.Add(_dot);
        Controls.Add(_clock);
        Controls.Add(_title);
        Controls.Add(_pause);
        Controls.Add(_stop);

        // Hidden controls get no window handle, so the first read can't wait for one:
        // the clock's own timer does it on its first tick, then polls.
        _sincePoll = PollSeconds - 1;
        _tick.Enabled = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _navigator.TimersChanged -= Reload;
            _tick.Dispose();
            _tips.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnTick()
    {
        if (++_sincePoll >= PollSeconds)
        {
            Reload();
            return;
        }

        RenderClock();
    }

    private void Reload()
    {
        _sincePoll = 0;
        try
        {
            _running = _reader.GetTimers().FirstOrDefault(t => t.State == TimerFormat.Running);
        }
        catch (Exception)
        {
            // A dropped connection should not pop a dialog every few seconds; the
            // indicator just hides until the next read succeeds.
            _running = null;
        }

        if (_running is not { } timer)
        {
            Visible = false;
            return;
        }

        var title = timer.SubjectTitle.Length > MaxTitle
            ? timer.SubjectTitle[..(MaxTitle - 1)] + "…"
            : timer.SubjectTitle;
        _title.Text = title;
        _tips.SetToolTip(_title, $"Timer running on “{timer.SubjectTitle}”. Click to open the Task.");
        _tips.SetToolTip(_pause, $"Pause the timer on “{timer.SubjectTitle}”.");
        _tips.SetToolTip(_stop, $"Stop the timer on “{timer.SubjectTitle}”.");
        _pause.AccessibleName = $"Pause the timer on {timer.SubjectTitle}";
        _stop.AccessibleName = $"Stop the timer on {timer.SubjectTitle}";
        RenderClock();
        Visible = true;
    }

    private void RenderClock()
    {
        if (_running is not { } timer)
        {
            return;
        }

        var (_, session) = TimerFormat.Live(
            timer.State, timer.RunningSince, timer.TotalSeconds, timer.SessionSeconds, DateTime.UtcNow);
        _clock.Text = TimerFormat.Clock(session);
        AccessibleName = $"Timer running on {timer.SubjectTitle}: {_clock.Text}";
    }

    private void Run(string action)
    {
        if (_running is not { } timer)
        {
            return;
        }

        Ui.RunWrite(this, _bsk, "Timer", bsk => bsk.Run("timer", action, timer.SubjectUrn));
        _navigator.SignalTimersChanged();
    }
}
