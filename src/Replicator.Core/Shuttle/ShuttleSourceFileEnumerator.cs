namespace Replicator.Core.Shuttle;

public sealed class ShuttleSourceFileEnumerator
{
    private static readonly string AlwaysExcludedDirectory = ".replicator-conflicts";
    private readonly Func<string, IEnumerable<string>> enumerateFiles;
    private readonly Func<string, IEnumerable<string>> enumerateDirectories;

    public ShuttleSourceFileEnumerator()
        : this(
            path => Directory.EnumerateFiles(path),
            path => Directory.EnumerateDirectories(path))
    {
    }

    public ShuttleSourceFileEnumerator(
        Func<string, IEnumerable<string>> enumerateFiles,
        Func<string, IEnumerable<string>> enumerateDirectories)
    {
        this.enumerateFiles = enumerateFiles;
        this.enumerateDirectories = enumerateDirectories;
    }

    public IEnumerable<string> EnumerateFiles(
        string sourceRoot,
        IEnumerable<string> excludePatterns,
        CancellationToken cancellationToken = default)
    {
        var excludedNames = CreateExcludedNames(excludePatterns);
        foreach (var file in EnumerateIncludedFiles(Path.GetFullPath(sourceRoot), excludedNames, cancellationToken))
        {
            yield return file;
        }
    }

    private IEnumerable<string> EnumerateIncludedFiles(
        string directory,
        IReadOnlySet<string> excludedNames,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var file in enumerateFiles(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsExcludedSegment(file, excludedNames))
            {
                yield return file;
            }
        }

        foreach (var childDirectory in enumerateDirectories(directory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsExcludedSegment(childDirectory, excludedNames))
            {
                continue;
            }

            foreach (var file in EnumerateIncludedFiles(childDirectory, excludedNames, cancellationToken))
            {
                yield return file;
            }
        }
    }

    private static IReadOnlySet<string> CreateExcludedNames(IEnumerable<string> excludePatterns)
    {
        var excludedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawPattern in excludePatterns.Append(AlwaysExcludedDirectory))
        {
            var pattern = rawPattern.Trim();
            if (!string.IsNullOrWhiteSpace(pattern))
            {
                excludedNames.Add(pattern);
            }
        }

        return excludedNames;
    }

    private static bool IsExcludedSegment(string path, IReadOnlySet<string> excludedNames)
    {
        var segment = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return excludedNames.Contains(segment);
    }
}
