using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace GitHistoryAnalyzer;

internal sealed record GitCommit(string Hash, string Author, DateTimeOffset Date, string Subject, string Body);

internal sealed class GitRepository(string root)
{
    public string Root { get; } = root;

    public static async Task<GitRepository> OpenAsync(string directory, CancellationToken cancellationToken)
    {
        var repository = new GitRepository(directory);
        string root = (await repository.RunAsync(["rev-parse", "--show-toplevel"], cancellationToken)).TrimEnd('\r', '\n');
        return new GitRepository(Path.GetFullPath(root));
    }

    public async Task<IReadOnlyList<string>> GetDirectoriesAsync(CancellationToken cancellationToken)
    {
        string output = await RunAsync(["ls-tree", "-r", "-z", "-d", "--name-only", "HEAD"], cancellationToken);
        return output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    public async Task<IReadOnlyList<GitCommit>> GetHistoryAsync(
        string path, int skip, int count, CancellationToken cancellationToken, DateTimeOffset? fromDate = null)
    {
        var arguments = new List<string>
        {
            "log", "-z", "--no-show-signature", "--encoding=UTF-8",
            "--format=%H%x00%an%x00%cI%x00%s%x00%b",
            $"--skip={skip}", $"--max-count={count}"
        };
        if (fromDate is DateTimeOffset start)
            arguments.Add($"--since-as-filter={start.ToString("O", CultureInfo.InvariantCulture)}");
        arguments.AddRange(["--", LiteralPath(path)]);
        string output = await RunAsync(arguments, cancellationToken);
        if (output.Length == 0)
            return [];

        string[] fields = output.Split('\0');
        if (fields[^1] != "" || (fields.Length - 1) % 5 != 0)
            throw new InvalidDataException("Git returned an unexpected commit history format.");

        var commits = new List<GitCommit>();
        for (int i = 0; i < fields.Length - 1; i += 5)
        {
            commits.Add(new GitCommit(fields[i], fields[i + 1],
                DateTimeOffset.Parse(fields[i + 2], CultureInfo.InvariantCulture).ToLocalTime(),
                fields[i + 3], fields[i + 4].TrimEnd()));
        }
        return commits;
    }

    public Task<string> GetDetailsAsync(GitCommit commit, string path, CancellationToken cancellationToken) =>
        RunAsync(["show", "--no-ext-diff", "--no-textconv", "--no-show-signature",
            "--format=fuller", "--stat", "--no-renames", commit.Hash, "--", LiteralPath(path)], cancellationToken);

    private static string LiteralPath(string path) => path.Length == 0 ? "." : $":(literal){path}";

    private async Task<string> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("--no-pager");
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("core.quotepath=false");
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException("Could not start Git. Install Git and make sure it is available on PATH.", exception);
        }

        Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            string result = await output;
            string errorText = await error;
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"Git failed (exit code {process.ExitCode}):{Environment.NewLine}{errorText.Trim()}");
            return result;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            // Observe the redirected stream tasks before disposing the process.
            try { await Task.WhenAll(output, error); }
            catch (OperationCanceledException) { }
            throw;
        }
    }
}
