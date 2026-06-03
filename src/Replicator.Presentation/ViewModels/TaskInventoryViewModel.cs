using System.Collections.ObjectModel;
using Replicator.Core.Scheduling;
using Replicator.Presentation.State;

namespace Replicator.Presentation.ViewModels;

public sealed class TaskInventoryViewModel : ObservableObject
{
    private string _summary = "Scheduled tasks not checked.";
    private bool _hasIssues;
    private bool _isActionCenterVisible;
    private bool _isReviewOpen;

    public ObservableCollection<ScheduledTaskInventoryItem> Items { get; } = [];

    public string Summary { get => _summary; private set => SetProperty(ref _summary, value); }
    public bool HasIssues { get => _hasIssues; private set => SetProperty(ref _hasIssues, value); }
    public bool IsActionCenterVisible { get => _isActionCenterVisible; private set => SetProperty(ref _isActionCenterVisible, value); }
    public bool IsReviewOpen { get => _isReviewOpen; private set => SetProperty(ref _isReviewOpen, value); }

    public void Apply(ScheduledTaskInventoryResult? inventory, ScheduledTaskInventoryItem? selectedIssue)
    {
        Items.Clear();

        if (inventory is not null)
        {
            foreach (var item in inventory.Items)
            {
                Items.Add(item);
            }
        }

        Summary = selectedIssue?.Reason
            ?? inventory?.Summary.ToDisplayString()
            ?? "Scheduled tasks not checked.";
        HasIssues = inventory?.Summary.HasIssues == true;
        IsActionCenterVisible = selectedIssue is not null;
    }

    public void OpenReview()
    {
        IsReviewOpen = Items.Count > 0;
    }

    public void CloseReview()
    {
        IsReviewOpen = false;
    }
}
