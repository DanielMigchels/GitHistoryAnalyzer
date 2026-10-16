using System.Text;

namespace GitHistoryAnalyzer;

internal static class ReleaseNotes
{
    public static string Create(string title, string path, IEnumerable<GitCommit> commits)
    {
        var result = new StringBuilder();
        result.Append("# ").AppendLine(Inline(title));
        result.AppendLine();
        result.Append("Application: ").AppendLine(Inline(path.Length == 0 ? "Repository root" : path));
        result.AppendLine();
        foreach (GitCommit commit in commits)
        {
            result.Append("- ").Append(Inline(commit.Subject))
                .Append(" (").Append(commit.Hash.AsSpan(0, 8)).Append(", ")
                .Append(Inline(commit.Author)).Append(", ")
                .Append(commit.Date.ToString("yyyy-MM-dd")).AppendLine(")");
        }
        return result.ToString();
    }

    private static string Inline(string text)
    {
        var result = new StringBuilder();
        foreach (char character in text)
        {
            if (character is '\r' or '\n')
                result.Append(' ');
            else
            {
                if ("\\`*_{}[]<>()#+-.!|~".Contains(character))
                    result.Append('\\');
                result.Append(character);
            }
        }
        return result.ToString();
    }
}
