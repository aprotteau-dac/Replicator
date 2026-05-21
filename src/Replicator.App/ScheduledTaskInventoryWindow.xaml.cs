using System.Windows;
using Replicator.Core.Scheduling;

namespace Replicator.App;

public partial class ScheduledTaskInventoryWindow : Window
{
    public ScheduledTaskInventoryWindow(ScheduledTaskInventoryResult inventory)
    {
        InitializeComponent();
        SummaryTextBlock.Text = inventory.Summary.ToDisplayString();
        TasksGrid.ItemsSource = inventory.Items;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
