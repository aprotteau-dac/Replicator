using Microsoft.UI.Xaml;
using Replicator.App.Services;
using Replicator.Core;
using Replicator.Core.Availability;
using Replicator.Core.Execution;
using Replicator.Core.Scheduling;
using Replicator.Core.Scripting;
using Replicator.Core.Security;
using Replicator.Core.Shuttle;
using Replicator.Core.Storage;
using Replicator.Presentation.ViewModels;

namespace Replicator.App;

public sealed partial class MainWindow : Window
{
    public MainWindowViewModel ViewModel { get; }

    public MainWindow()
    {
        InitializeComponent();

        var paths = ReplicatorPaths.CreateDefault();
        paths.EnsureCreated();
        var processRunner = new ProcessRunner();

        ViewModel = new MainWindowViewModel(
            paths,
            new JsonProfileStore(paths.ProfilesFile),
            new ProfileAvailabilityChecker(),
            new PowerShellBitLockerStatusProvider(processRunner),
            new ElevatedPowerShellBitLockerStatusProvider(new ElevatedProcessRunner()),
            new ProfileDriveSecurityCache(),
            new PowerShellScriptGenerator(paths.ScriptsDirectory, paths.LogsDirectory),
            new BackupLogReader(paths.LogsDirectory),
            new BackupRunStatusReader(paths.LogsDirectory),
            new ShuttleService(new MachineIdentityProvider(paths.MachineIdentityFile).GetOrCreate()),
            processRunner,
            new WindowsScheduledTaskService(processRunner),
            new WindowsScheduledTaskInventoryService(processRunner),
            new WinUiFolderPicker(this),
            new WinUiUserConfirmation(RootGrid));

        RootGrid.DataContext = ViewModel;
        ModeComboBox.ItemsSource = Enum.GetValues<Replicator.Core.Models.ProfileMode>();
        CadenceComboBox.ItemsSource = Enum.GetValues<Replicator.Core.Models.ScheduleCadence>();
        DayOfWeekComboBox.ItemsSource = Enum.GetValues<DayOfWeek>();
        _ = ViewModel.LoadAsync();
    }
}
