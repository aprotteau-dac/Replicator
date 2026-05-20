using Replicator.Core.Availability;
using Replicator.Core.Models;
using Replicator.Core.Scheduling;
using Replicator.Core.Scripting;
using Replicator.Core.Security;
using Replicator.Core.Shuttle;
using Replicator.Core.Storage;

var tests = new (string Name, Func<Task> Test)[]
{
    ("validator rejects destinations under the source tree", ValidatorRejectsNestedDestination),
    ("script generator emits robocopy dry-run script", ScriptGeneratorEmitsDryRunScript),
    ("log reader summarizes latest robocopy log", LogReaderSummarizesLatestRobocopyLog),
    ("profile store round-trips JSON", ProfileStoreRoundTripsJson),
    ("shuttle prepare depart dock receive preserves conflicts", ShuttlePrepareDepartDockReceivePreservesConflicts),
    ("shuttle prepare preserves timestamps for fast skip analysis", ShuttlePreparePreservesTimestampsForFastSkipAnalysis),
    ("shuttle prepare reports file progress", ShuttlePrepareReportsFileProgress),
    ("shuttle operations honor cancellation", ShuttleOperationsHonorCancellation),
    ("scheduled task names are deterministic and scoped", ScheduledTaskNamesAreScoped),
    ("minute schedules emit schtasks minute cadence", MinuteSchedulesEmitSchtasksMinuteCadence),
    ("default profile carries local development excludes", DefaultProfileHasDevelopmentExcludes),
    ("validator rejects invalid minute interval", ValidatorRejectsInvalidMinuteInterval),
    ("availability checker reports missing source and creatable target", AvailabilityCheckerReportsMissingSourceAndCreatableTarget),
    ("availability checker reports unavailable drive", AvailabilityCheckerReportsUnavailableDrive),
    ("bitlocker parser classifies protected unprotected and locked drives", BitLockerParserClassifiesProtectedUnprotectedAndLockedDrives),
    ("profile drive security checker summarizes bitlocker posture", ProfileDriveSecurityCheckerSummarizesBitLockerPosture)
};

var failures = 0;

foreach (var (name, test) in tests)
{
    try
    {
        await test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}");
        Console.Error.WriteLine(exception);
    }
}

if (failures > 0)
{
    Console.Error.WriteLine($"{failures} test(s) failed.");
    return 1;
}

Console.WriteLine($"{tests.Length} test(s) passed.");
return 0;

static Task ValidatorRejectsNestedDestination()
{
    var profile = ValidProfile();
    profile.SourcePath = @"C:\work\scratch";
    profile.Target.Path = @"C:\work\scratch\backup";

    var issues = BackupProfileValidator.Validate(profile);

    Assert(issues.Any(issue => issue.Field == "Path"), "Expected nested destination validation issue.");
    return Task.CompletedTask;
}

static Task ScriptGeneratorEmitsDryRunScript()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var generator = new PowerShellScriptGenerator(Path.Combine(root, "scripts"), Path.Combine(root, "logs"));
    var profile = ValidProfile();
    profile.DryRun = true;
    profile.ExcludePatterns = ["node_modules", "bin"];

    var script = generator.Generate(profile);

    Assert(script.Path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase), "Expected PowerShell script path.");
    Assert(script.Content.Contains("robocopy", StringComparison.OrdinalIgnoreCase), "Expected robocopy invocation.");
    Assert(script.Content.Contains("$DryRunFromProfile = $true", StringComparison.Ordinal), "Expected profile dry-run flag.");
    Assert(script.Content.Contains("'node_modules'", StringComparison.Ordinal), "Expected node_modules exclude.");
    Assert(script.Content.Contains("'bin'", StringComparison.Ordinal), "Expected bin exclude.");

    return Task.CompletedTask;
}

static Task LogReaderSummarizesLatestRobocopyLog()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var logsDirectory = Path.Combine(root, "logs");
    Directory.CreateDirectory(logsDirectory);

    try
    {
        var profile = ValidProfile();
        var slug = PowerShellScriptGenerator.ProfileSlug(profile);
        var logPath = Path.Combine(logsDirectory, $"{slug}-20260518-220000.log");

        File.WriteAllLines(
            logPath,
            [
                "Replicator backup run",
                "Mode: Dry run - no files will be copied",
                "Source: C:\\work\\scratch",
                "Destination: D:\\backups\\scratch",
                "",
                "------------------------------------------------------------------------------",
                "               Total    Copied   Skipped  Mismatch    FAILED    Extras",
                "    Dirs :        42        41         1         0         0         0",
                "   Files :        73        73         0         0         0         0",
                "   Bytes :    1.57 m    1.57 m         0         0         0         0"
            ]);

        var summary = new BackupLogReader(logsDirectory).ReadLatest(profile);

        if (summary is null)
        {
            throw new InvalidOperationException("Expected latest log summary.");
        }

        Assert(summary.Mode == "Dry run - no files will be copied", "Expected mode from header.");
        Assert(summary.TotalFiles == 73, "Expected total file count.");
        Assert(summary.CopiedFiles == 73, "Expected copied/listed file count.");
        Assert(summary.FailedFiles == 0, "Expected failed file count.");
        Assert(summary.TotalDirectories == 42, "Expected total directory count.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    return Task.CompletedTask;
}

static async Task ProfileStoreRoundTripsJson()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);

    try
    {
        var store = new JsonProfileStore(Path.Combine(root, "profiles.json"));
        var profile = ValidProfile();
        profile.Name = "Round trip";
        profile.Schedule.Cadence = ScheduleCadence.Weekly;
        profile.Schedule.DayOfWeek = DayOfWeek.Friday;

        await store.UpsertAsync(profile);
        var loaded = await store.LoadAsync();

        Assert(loaded.Count == 1, "Expected one profile.");
        Assert(loaded[0].Name == "Round trip", "Expected saved name.");
        Assert(loaded[0].Schedule.Cadence == ScheduleCadence.Weekly, "Expected saved cadence.");
        Assert(loaded[0].Schedule.DayOfWeek == DayOfWeek.Friday, "Expected saved day.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttlePrepareDepartDockReceivePreservesConflicts()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var homeSource = Path.Combine(root, "home", "repo");
    var workSource = Path.Combine(root, "work", "repo");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");

    Directory.CreateDirectory(homeSource);
    Directory.CreateDirectory(workSource);

    try
    {
        File.WriteAllText(Path.Combine(homeSource, "note.md"), "from home");
        Directory.CreateDirectory(Path.Combine(homeSource, "node_modules"));
        File.WriteAllText(Path.Combine(homeSource, "node_modules", "ignored.txt"), "ignored");
        File.WriteAllText(Path.Combine(workSource, "note.md"), "local work edit");

        var profileId = Guid.NewGuid();
        var homeProfile = ValidShuttleProfile(profileId, homeSource, shuttleRoot);
        var workProfile = ValidShuttleProfile(profileId, workSource, shuttleRoot);

        var home = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));
        var work = new ShuttleService(new MachineIdentity("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "WORK"));

        var prepare = await home.PrepareAsync(homeProfile);
        Assert(prepare.Succeeded, prepare.Message);
        Assert(File.Exists(Path.Combine(shuttleRoot, "payload", "note.md")), "Expected shuttle payload file.");
        Assert(!File.Exists(Path.Combine(shuttleRoot, "payload", "node_modules", "ignored.txt")), "Expected excluded payload file to be skipped.");

        var depart = await home.DepartAsync(homeProfile);
        Assert(depart.Succeeded, depart.Message);
        Assert(depart.Manifest?.ReadyToDock == true, "Expected depart manifest to be ready to dock.");

        var dock = await work.DockAsync(workProfile);
        Assert(dock.Succeeded, dock.Message);
        Assert(dock.Manifest?.ConflictFiles == 1, "Expected one potential conflict.");

        var receive = await work.ReceiveAsync(workProfile);
        Assert(receive.Succeeded, receive.Message);
        Assert(File.ReadAllText(Path.Combine(workSource, "note.md")) == "from home", "Expected inbound shuttle file to overwrite local file.");

        var conflictFiles = Directory.EnumerateFiles(Path.Combine(shuttleRoot, "conflicts"), "note.md", SearchOption.AllDirectories).ToList();
        Assert(conflictFiles.Count == 1, "Expected preserved local conflict copy.");
        Assert(File.ReadAllText(conflictFiles[0]) == "local work edit", "Expected conflict copy to preserve local content.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttlePreparePreservesTimestampsForFastSkipAnalysis()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var homeSource = Path.Combine(root, "home", "repo");
    var workSource = Path.Combine(root, "work", "repo");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");

    Directory.CreateDirectory(homeSource);
    Directory.CreateDirectory(workSource);

    try
    {
        var sourceFile = Path.Combine(homeSource, "note.md");
        var matchingWorkFile = Path.Combine(workSource, "note.md");
        var expectedTimestamp = new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc);

        File.WriteAllText(sourceFile, "same content");
        File.WriteAllText(matchingWorkFile, "same content");
        File.SetLastWriteTimeUtc(sourceFile, expectedTimestamp);
        File.SetLastWriteTimeUtc(matchingWorkFile, expectedTimestamp);

        var profileId = Guid.NewGuid();
        var homeProfile = ValidShuttleProfile(profileId, homeSource, shuttleRoot);
        var workProfile = ValidShuttleProfile(profileId, workSource, shuttleRoot);
        var payloadFile = Path.Combine(shuttleRoot, "payload", "note.md");

        var home = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));
        var work = new ShuttleService(new MachineIdentity("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "WORK"));

        var prepare = await home.PrepareAsync(homeProfile);
        Assert(prepare.Succeeded, prepare.Message);
        Assert(File.Exists(payloadFile), "Expected shuttle payload file.");
        Assert(TimestampsWithinTolerance(File.GetLastWriteTimeUtc(payloadFile), expectedTimestamp), "Expected prepared payload to preserve source timestamp.");

        var depart = await home.DepartAsync(homeProfile);
        Assert(depart.Succeeded, depart.Message);

        var dock = await work.DockAsync(workProfile);
        Assert(dock.Succeeded, dock.Message);
        Assert(dock.Manifest?.SkippedFiles == 1, "Expected matching inbound file to be skipped.");
        Assert(dock.Manifest?.ChangedFiles == 0, "Expected no changed files for matching inbound payload.");
        Assert(dock.Manifest?.ConflictFiles == 0, "Expected no conflicts for matching inbound payload.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttlePrepareReportsFileProgress()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");

    Directory.CreateDirectory(source);

    try
    {
        File.WriteAllText(Path.Combine(source, "one.md"), "one");
        Directory.CreateDirectory(Path.Combine(source, "notes"));
        File.WriteAllText(Path.Combine(source, "notes", "two.md"), "two");

        var profile = ValidShuttleProfile(Guid.NewGuid(), source, shuttleRoot);
        var service = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));
        var progressEvents = new System.Collections.Concurrent.ConcurrentQueue<ShuttleOperationProgress>();
        var progress = new Progress<ShuttleOperationProgress>(progressEvents.Enqueue);

        var result = await service.PrepareAsync(profile, progress);
        for (var attempt = 0; attempt < 20 && !progressEvents.Any(item => item.Operation == ShuttleOperationKind.Prepare && item.TotalFiles == 2 && item.ProcessedFiles == 2); attempt++)
        {
            await Task.Delay(10);
        }

        Assert(result.Succeeded, result.Message);
        Assert(progressEvents.Any(item => item.Operation == ShuttleOperationKind.Prepare && item.TotalFiles == 2), "Expected prepare progress to include total file count.");

        var maxProcessed = progressEvents
            .Where(item => item.Operation == ShuttleOperationKind.Prepare && item.TotalFiles == 2)
            .Max(item => item.ProcessedFiles);

        Assert(maxProcessed == 2, $"Expected max processed file count to be 2, got {maxProcessed}.");
        Assert(progressEvents.Any(item => item.Operation == ShuttleOperationKind.Prepare && item.PercentComplete == 100), "Expected a 100% progress event.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task ShuttleOperationsHonorCancellation()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "source");
    var shuttleRoot = Path.Combine(root, "external", "Replicator", "shuttle", "repo");

    Directory.CreateDirectory(source);

    try
    {
        File.WriteAllText(Path.Combine(source, "one.md"), "one");

        var profile = ValidShuttleProfile(Guid.NewGuid(), source, shuttleRoot);
        var service = new ShuttleService(new MachineIdentity("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "HOME"));

        await AssertCanceled(
            token => service.PrepareAsync(profile, progress: null, cancellationToken: token),
            "Expected Prepare Shuttle to honor cancellation.");
        await AssertCanceled(
            token => service.DepartAsync(profile, progress: null, cancellationToken: token),
            "Expected Depart to honor cancellation.");
        await AssertCanceled(
            token => service.DockAsync(profile, progress: null, cancellationToken: token),
            "Expected Dock Shuttle to honor cancellation.");
        await AssertCanceled(
            token => service.ReceiveAsync(profile, progress: null, cancellationToken: token),
            "Expected Receive Changes to honor cancellation.");

        var manifestsDirectory = Path.Combine(shuttleRoot, "manifests");
        var manifestCount = Directory.Exists(manifestsDirectory)
            ? Directory.EnumerateFiles(manifestsDirectory, "*.json").Count()
            : 0;

        Assert(manifestCount == 0, "Expected canceled prepare to avoid writing manifests.");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

static async Task AssertCanceled(Func<CancellationToken, Task> operation, string message)
{
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    try
    {
        await operation(cancellation.Token);
        throw new InvalidOperationException(message);
    }
    catch (OperationCanceledException)
    {
        // Expected path.
    }
}

static Task ScheduledTaskNamesAreScoped()
{
    var profile = ValidProfile();
    profile.Id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    profile.Name = "AI Scratch Repo Backup!";

    var taskName = ScheduledTaskName.ForProfile(profile);

    Assert(taskName == @"\Replicator\AI-Scratch-Repo-Backup-00112233", $"Unexpected task name: {taskName}");
    return Task.CompletedTask;
}

static Task MinuteSchedulesEmitSchtasksMinuteCadence()
{
    var profile = ValidProfile();
    profile.Schedule.Cadence = ScheduleCadence.Minutes;
    profile.Schedule.IntervalMinutes = 15;

    var arguments = BuildScheduledTaskArguments(profile);

    Assert(arguments.Contains("/SC"), "Expected schtasks schedule switch.");
    Assert(arguments.Contains("MINUTE"), "Expected minute schedule cadence.");
    Assert(arguments.Contains("/MO"), "Expected schedule modifier switch.");
    Assert(arguments.Contains("15"), "Expected 15-minute schedule modifier.");
    return Task.CompletedTask;
}

static Task DefaultProfileHasDevelopmentExcludes()
{
    var profile = BackupProfileFactory.CreateDefault();

    Assert(profile.DryRun, "Expected new profiles to default to dry-run.");
    Assert(profile.ExcludePatterns.Contains("node_modules"), "Expected node_modules exclude.");
    Assert(profile.ExcludePatterns.Contains("bin"), "Expected bin exclude.");
    Assert(profile.ExcludePatterns.Contains("obj"), "Expected obj exclude.");
    Assert(profile.ExcludePatterns.Contains(".replicator-conflicts"), "Expected conflict folder exclude.");

    return Task.CompletedTask;
}

static Task ValidatorRejectsInvalidMinuteInterval()
{
    var profile = ValidProfile();
    profile.Schedule.Cadence = ScheduleCadence.Minutes;
    profile.Schedule.IntervalMinutes = 0;

    var issues = BackupProfileValidator.Validate(profile);

    Assert(issues.Any(issue => issue.Field == "IntervalMinutes"), "Expected invalid minute interval validation issue.");
    return Task.CompletedTask;
}

static Task AvailabilityCheckerReportsMissingSourceAndCreatableTarget()
{
    var root = Path.Combine(Environment.CurrentDirectory, "test-artifacts", Guid.NewGuid().ToString("N"));
    var source = Path.Combine(root, "missing-source");
    var destination = Path.Combine(root, "backup", "repo");

    Directory.CreateDirectory(root);

    try
    {
        var profile = ValidProfile();
        profile.SourcePath = source;
        profile.Target.Path = destination;

        var report = new ProfileAvailabilityChecker().Check(profile);

        Assert(report.HasErrors, "Expected missing source to be an availability error.");
        Assert(report.HasWarnings, "Expected missing but creatable target to be an availability warning.");
        Assert(report.Items.Any(item => item.Label == "Source" && item.State == PathAvailabilityState.Missing), "Expected missing source state.");
        Assert(report.Items.Any(item => item.Label == "Target" && item.State == PathAvailabilityState.Creatable), "Expected creatable target state.");
        Assert(report.Summary.Contains("Source path is unavailable", StringComparison.OrdinalIgnoreCase), $"Unexpected summary: {report.Summary}");
    }
    finally
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    return Task.CompletedTask;
}

static Task AvailabilityCheckerReportsUnavailableDrive()
{
    var unavailableRoot = FindUnavailableDriveRoot();
    if (unavailableRoot is null)
    {
        Console.WriteLine("SKIP no unused Windows drive letter available for unavailable-drive availability check");
        return Task.CompletedTask;
    }

    var profile = ValidProfile();
    profile.SourcePath = Path.Combine(unavailableRoot, "source");
    profile.Target.Path = Path.Combine(unavailableRoot, "target");

    var report = new ProfileAvailabilityChecker().Check(profile);

    Assert(report.HasErrors, "Expected unavailable drive to be an availability error.");
    Assert(report.Items.Any(item => item.State == PathAvailabilityState.DriveUnavailable), "Expected drive unavailable state.");
    Assert(report.Summary.Contains("drive is unavailable", StringComparison.OrdinalIgnoreCase), $"Unexpected summary: {report.Summary}");
    return Task.CompletedTask;
}

static Task BitLockerParserClassifiesProtectedUnprotectedAndLockedDrives()
{
    const string protectedJson = """
        {"MountPoint":"D:","VolumeStatus":"FullyEncrypted","ProtectionStatus":"On","LockStatus":"Unlocked","EncryptionPercentage":100}
        """;
    const string unprotectedJson = """
        {"MountPoint":"E:","VolumeStatus":"FullyDecrypted","ProtectionStatus":"Off","LockStatus":"Unlocked","EncryptionPercentage":0}
        """;
    const string lockedJson = """
        {"MountPoint":"F:","VolumeStatus":"FullyEncrypted","ProtectionStatus":"On","LockStatus":"Locked","EncryptionPercentage":100}
        """;

    Assert(BitLockerStatusParser.TryParseJson(protectedJson, out var protectedStatus), "Expected protected BitLocker JSON to parse.");
    Assert(BitLockerStatusParser.TryParseJson(unprotectedJson, out var unprotectedStatus), "Expected unprotected BitLocker JSON to parse.");
    Assert(BitLockerStatusParser.TryParseJson(lockedJson, out var lockedStatus), "Expected locked BitLocker JSON to parse.");

    var protectedItem = BitLockerStatusParser.ToSecurityItem("Target drive", @"D:\backup", @"D:\", protectedStatus);
    var unprotectedItem = BitLockerStatusParser.ToSecurityItem("Target drive", @"E:\backup", @"E:\", unprotectedStatus);
    var lockedItem = BitLockerStatusParser.ToSecurityItem("Shuttle drive", @"F:\Replicator\shuttle", @"F:\", lockedStatus);

    Assert(protectedItem.State == DriveSecurityState.Protected, "Expected protected state.");
    Assert(protectedItem.Severity == DriveSecuritySeverity.Info, "Expected protected drive to be informational.");
    Assert(unprotectedItem.State == DriveSecurityState.Unprotected, "Expected unprotected state.");
    Assert(unprotectedItem.Severity == DriveSecuritySeverity.Warning, "Expected unprotected drive to warn.");
    Assert(lockedItem.State == DriveSecurityState.Locked, "Expected locked state.");
    Assert(lockedItem.Severity == DriveSecuritySeverity.Error, "Expected locked drive to error.");
    return Task.CompletedTask;
}

static async Task ProfileDriveSecurityCheckerSummarizesBitLockerPosture()
{
    var profile = ValidProfile();
    profile.SourcePath = @"C:\work\scratch";
    profile.Target.Path = @"D:\backups\scratch";

    var provider = new FakeBitLockerStatusProvider();
    provider.Items[@"C:\"] = new DriveSecurityItem(
        "Source drive",
        profile.SourcePath,
        @"C:\",
        DriveSecurityState.Protected,
        DriveSecuritySeverity.Info,
        @"Drive security: Source drive is BitLocker protected (C:\).");
    provider.Items[@"D:\"] = new DriveSecurityItem(
        "Target drive",
        profile.Target.Path,
        @"D:\",
        DriveSecurityState.Unprotected,
        DriveSecuritySeverity.Warning,
        @"Drive security: Target drive is not BitLocker protected (D:\).");

    var report = await new ProfileDriveSecurityChecker(provider).CheckAsync(profile);

    Assert(report.HasWarnings, "Expected unprotected target drive to warn.");
    Assert(report.Items.Count == 2, "Expected source and target drive security items.");
    Assert(report.Summary.Contains("Target drive is not BitLocker protected", StringComparison.OrdinalIgnoreCase), $"Unexpected security summary: {report.Summary}");
}

static string? FindUnavailableDriveRoot()
{
    if (!OperatingSystem.IsWindows())
    {
        return null;
    }

    var existing = DriveInfo
        .GetDrives()
        .Select(drive => char.ToUpperInvariant(drive.Name[0]))
        .ToHashSet();

    for (var letter = 'Z'; letter >= 'G'; letter--)
    {
        if (!existing.Contains(letter))
        {
            return $"{letter}:\\";
        }
    }

    return null;
}

static BackupProfile ValidProfile()
{
    return new BackupProfile
    {
        Name = "Scratch",
        SourcePath = @"C:\work\scratch",
        Target = new BackupTarget
        {
            Kind = BackupTargetKind.LocalPath,
            Path = @"D:\backups\scratch"
        },
        Engine = BackupEngineKind.NativePowerShell,
        Schedule = new BackupSchedule
        {
            Enabled = true,
            Cadence = ScheduleCadence.Daily,
            TimeOfDay = new TimeOnly(18, 0),
            IntervalHours = 6
        },
        DryRun = true,
        MirrorDeletes = false
    };
}

static BackupProfile ValidShuttleProfile(Guid profileId, string sourcePath, string shuttleRoot)
{
    return new BackupProfile
    {
        Id = profileId,
        Name = "Scratch Shuttle",
        Mode = ProfileMode.Shuttle,
        SourcePath = sourcePath,
        Target = new BackupTarget
        {
            Kind = BackupTargetKind.LocalPath,
            Path = shuttleRoot
        },
        Engine = BackupEngineKind.NativePowerShell,
        Schedule = new BackupSchedule
        {
            Enabled = true,
            Cadence = ScheduleCadence.Hourly,
            TimeOfDay = new TimeOnly(18, 0),
            IntervalHours = 1
        },
        DryRun = false,
        MirrorDeletes = false,
        ExcludePatterns = ["node_modules"]
    };
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static bool TimestampsWithinTolerance(DateTime first, DateTime second)
{
    return (first - second).Duration() <= TimeSpan.FromSeconds(2);
}

static IReadOnlyList<string> BuildScheduledTaskArguments(BackupProfile profile)
{
    var method = typeof(WindowsScheduledTaskService).GetMethod(
        "BuildCreateArguments",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

    if (method is null)
    {
        throw new InvalidOperationException("Expected scheduled task argument builder.");
    }

    return (IReadOnlyList<string>)method.Invoke(null, [profile, @"C:\Replicator\profile.ps1", ScheduledTaskName.ForProfile(profile)])!;
}

sealed class FakeBitLockerStatusProvider : IBitLockerStatusProvider
{
    public Dictionary<string, DriveSecurityItem> Items { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task<DriveSecurityItem> CheckAsync(
        string label,
        string path,
        string root,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Items.TryGetValue(root, out var item)
            ? item
            : new DriveSecurityItem(label, path, root, DriveSecurityState.Unknown, DriveSecuritySeverity.Warning, $"Unknown: {root}"));
    }
}
