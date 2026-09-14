using LifeOs.Pilot.Shell;

namespace LifeOs.Pilot.Views;

/// <summary>Band-E placeholder (Reviews need the Review type + D3 edit trail).</summary>
internal sealed class ReviewsView : UserControl, IPilotView
{
    public ReviewsView()
    {
        var label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = SystemColors.GrayText,
            Text = "Reviews — blocked on kernel Band E\n\n"
                 + "The Review subject type, mutable-body edit trail (D3), and review scheduling\n"
                 + "are not in the current kernel. This tab is a labelled placeholder until that work lands."
        };
        Controls.Add(label);
    }

    public bool IsDirty => false;

    public bool TrySaveEdits() => true;

    public void DiscardEdits()
    {
    }

    public void Reload()
    {
    }

    public void SelectSubject(Guid id)
    {
    }

    public void SaveViewState(ViewStateStore store)
    {
    }

    public void RestoreViewState(ViewStateStore store)
    {
    }
}
