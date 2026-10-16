using System.Diagnostics;
using System.Text;

namespace GitHistoryAnalyzer.GuiTests;

internal sealed class TestRepository : IDisposable
{
    public const string ApplicationPath = "Applications/Services/Example Server";
    public const string UnicodeSubject = "Fix audio \u00e9 \u2014 release";
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "GitHistoryAnalyzer.GuiTests", Guid.NewGuid().ToString("N"));

    public TestRepository(bool paginated = false, bool dated = false)
    {
        Directory.CreateDirectory(Root);
        try
        {
            Git(["init", "--quiet", "--initial-branch=main", "--template="]);
            if (dated)
            {
                DateTimeOffset midnight = new(new DateTime(2025, 6, 10, 0, 0, 0, DateTimeKind.Local));
                Import([
                    new("At midnight", [("Apps/Dated/file.txt", "boundary")], midnight),
                    new("Before midnight", [("Apps/Dated/file.txt", "old")], midnight.AddSeconds(-1)),
                    new("After midnight", [("Apps/Dated/file.txt", "new")], midnight.AddHours(2))
                ]);
            }
            else if (paginated)
            {
                Import(Enumerable.Range(1, 201).Select(index => new FixtureCommit(
                    $"Commit {index:000}", [($"Apps/Paged/file.txt", index.ToString())],
                    new DateTimeOffset(new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Local)).AddDays(index - 1))));
            }
            else
            {
                Import([
                    new("Initial applications", [
                        ($"{ApplicationPath}/app.txt", "first"),
                        ("Apps/Other/other.txt", "other"),
                        ("Apps/[special]/file.txt", "literal"),
                        ("Apps/s/file.txt", "decoy")]),
                    new("Add nested worker", [($"{ApplicationPath}/nested/worker.txt", "worker")]),
                    new("Unrelated change", [("Apps/Other/other.txt", "changed")]),
                    new("Literal path change", [("Apps/[special]/file.txt", "literal updated")]),
                    new("Wildcard decoy", [("Apps/s/file.txt", "decoy updated")]),
                    new(UnicodeSubject + "\n\nDetailed release context.", [($"{ApplicationPath}/app.txt", "fixed")])
                ]);
            }
            Git(["read-tree", "HEAD"]);
            Git(["checkout-index", "--all"]);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void AddApplicationCommit()
    {
        File.AppendAllText(Path.Combine(Root, ApplicationPath, "app.txt"), "\nupdated");
        Git(["add", "."]);
        Git(["-c", "user.name=GUI Test Author", "-c", "user.email=gui-test@example.invalid",
            "-c", "commit.gpgsign=false", "-c", "core.hooksPath=NUL",
            "commit", "--quiet", "-m", "New application change"]);
    }

    private void Import(IEnumerable<FixtureCommit> commits)
    {
        var input = new StringBuilder();
        int mark = 0;
        foreach (FixtureCommit commit in commits)
        {
            mark++;
            long timestamp = commit.Date?.ToUnixTimeSeconds() ?? 1700000000 + mark;
            input.Append($"commit refs/heads/main\nmark :{mark}\n")
                .Append($"author GUI Test Author <gui-test@example.invalid> {timestamp - 86400} +0000\n")
                .Append($"committer GUI Test Author <gui-test@example.invalid> {timestamp} +0000\n");
            AppendData(input, commit.Message);
            if (mark > 1)
                input.Append($"from :{mark - 1}\n");
            foreach ((string path, string content) in commit.Files)
            {
                input.Append($"M 100644 inline {path}\n");
                AppendData(input, content);
            }
            input.Append('\n');
        }
        Git(["fast-import", "--quiet"], input.ToString());
    }

    private static void AppendData(StringBuilder input, string data) =>
        input.Append($"data {Encoding.UTF8.GetByteCount(data)}\n").Append(data).Append('\n');

    private void Git(string[] arguments, string? input = null)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = input is not null
        };
        if (input is not null)
            start.StandardInputEncoding = new UTF8Encoding(false);
        start.Environment["GIT_CONFIG_GLOBAL"] = "NUL";
        start.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("Could not start Git.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        if (input is not null)
        {
            process.StandardInput.Write(input);
            process.StandardInput.Close();
        }
        if (!process.WaitForExit(15_000))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            throw new TimeoutException($"Git timed out: {string.Join(' ', arguments)}");
        }
        Task.WaitAll(output, error);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Git failed: {error.Result}");
    }

    public void Dispose()
    {
        if (!Directory.Exists(Root))
            return;
        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(Root, recursive: true);
    }

    private sealed record FixtureCommit(string Message, (string Path, string Content)[] Files, DateTimeOffset? Date = null);
}
