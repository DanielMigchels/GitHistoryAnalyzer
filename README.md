# Git History Analyzer

> 100% AI generated

A Windows desktop application for browsing application history in a local Git monorepo and producing per-application release notes.

## Requirements

- Windows and .NET 10 SDK
- Git installed and available on `PATH`
- A local repository with at least one commit

## Run

```powershell
dotnet run --project .\GitHistoryAnalyzer\GitHistoryAnalyzer.csproj
```

## Workflow

1. Choose **Open repository...** and select your monorepo (or a folder inside it).
2. Select an application directory in the tree. The history includes changes inside that directory and all its subdirectories, equivalent to `git log -- "Applications/Services/Example Server"`. Select the root to see all applications.
3. Browse commit dates, authors, and messages. Selecting a commit displays its full message and file-change summary for the selected directory.
4. Check the commits to include in the release. **Check all loaded** selects the currently loaded commits; **Load more commits** retrieves the next 200 older commits without losing existing checks.
5. Open **Release notes (Markdown)**, enter a release title, and copy or export the notes.

### Filter by release date

Check **From date (inclusive)** above the history table and choose the first day
to include in your next release. Uncheck it to show all dates. The **Commit date
(local)** column and filter both use the Git committer timestamp in your local
time zone, not the author timestamp. The selected day is included from midnight;
older commits are excluded before pagination, including when loading more.
Filtering scans the entire path history, so out-of-order commit dates do not
hide otherwise eligible commits.

Changing or disabling the date filter clears checked commits and regenerates the
release-note preview. The filter remains active when changing folders or
refreshing. To avoid overlap with a release covering an entire day, choose the
following day as the next release's start date. This is a date filter, not a
record of previously exported commits; it cannot distinguish two releases on the
same day.

The tree shows tracked directories at `HEAD`, not untracked folders or directories deleted before `HEAD`. History follows Git's default path-history and merge simplification rules on the current branch (`HEAD`); it does not follow directory renames or include other branches automatically. Paths are matched literally, including spaces and Git pathspec special characters. Selecting another directory clears the release selection. **Refresh** reloads the tree and history after external Git changes, retaining the selected directory if it still exists.

This application only reads your repository; it does not check out branches, stage files, or create commits.

## Automated GUI tests

The solution includes `GitHistoryAnalyzer.GuiTests`, an MSTest project. Run it on
Windows with .NET 10, Git on `PATH`, and an interactive desktop session:

```powershell
dotnet test .\GitHistoryAnalyzer.slnx --filter "TestCategory=GUI"
```

The tests show actual WinForms windows on a dedicated STA thread with a Windows
message loop. They exercise tree selections, grid checkbox editing, button
clicks, commit details, Markdown previews, 200-commit pagination, refresh,
rapid folder switching, and minimum-size layout. Each repository test creates
and deletes its own temporary Git repository; no existing repository is changed.
Tests run serially and use bounded waits for asynchronous UI updates.

The test driver bypasses only the native repository folder picker to supply a
fixture path. Native file dialogs, clipboard contents, and OS-level mouse/keyboard
automation are not covered. Private form-member access is isolated in the test
driver; the application does not expose a separate test-only UI.
