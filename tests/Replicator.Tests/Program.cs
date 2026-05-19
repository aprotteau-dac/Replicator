using Replicator.Core.Models;
using Replicator.Core.Scheduling;
using Replicator.Core.Scripting;
using Replicator.Core.Shuttle;
using Replicator.Core.Storage;

var tests = new (string Name, Func<Task> Test)[]
{
    ("validator rejects destinations under the source tree", ValidatorRejectsNestedDestination),
    ("script generator emits robocopy dry-run script", ScriptGeneratorEmitsDryRunScript),
    ("log reader summarizes latest robocopy log", LogReaderSummarizesLatestRobocopyLog),
    ("profile store round-trips JSON", ProfileStoreRoundTripsJson),
    ("shuttle prepare depart dock receive preserves conflicts", ShuttlePrepareDepartDockReceivePreservesConflicts),
    ("scheduled task names are deterministic and scoped", ScheduledTaskNamesAreScoped),
    ("default profile carries local development excludes", DefaultProfileHasDevelopmentExcludes)
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

static Task ScheduledTaskNamesAreScoped()
{
    var profile = ValidProfile();
    profile.Id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    profile.Name = "AI Scratch Repo Backup!";

    var taskName = ScheduledTaskName.ForProfile(profile);

    Assert(taskName == @"\Replicator\AI-Scratch-Repo-Backup-00112233", $"Unexpected task name: {taskName}");
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
