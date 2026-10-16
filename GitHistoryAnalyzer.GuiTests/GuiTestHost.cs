using System.Diagnostics;
using System.Reflection;

namespace GitHistoryAnalyzer.GuiTests;

internal static class GuiTestHost
{
    public static async Task RunAsync(Func<HistoryWindow, Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new Form1();
                using var watchdog = new System.Windows.Forms.Timer { Interval = 30_000 };
                watchdog.Tick += (_, _) =>
                {
                    completion.TrySetException(new TimeoutException("GUI test exceeded 30 seconds."));
                    form.Close();
                    Application.ExitThread();
                };
                form.Shown += async (_, _) =>
                {
                    try
                    {
                        watchdog.Start();
                        await test(new HistoryWindow(form));
                        completion.TrySetResult();
                    }
                    catch (Exception exception) { completion.TrySetException(exception); }
                    finally
                    {
                        watchdog.Stop();
                        form.Close();
                    }
                };
                Application.Run(form);
            }
            catch (Exception exception) { completion.TrySetException(exception); }
            finally
            {
                completion.TrySetException(new InvalidOperationException("GUI closed before the test completed."));
            }
        })
        {
            IsBackground = true,
            Name = "WinForms GUI test"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        try
        {
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(40));
        }
        finally
        {
            if (!thread.Join(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("The GUI test thread did not shut down.");
        }
    }
}

internal sealed class HistoryWindow(Form1 form)
{
    public Form1 Form => form;
    public TreeView Tree => Field<TreeView>("directories");
    public DataGridView History => Field<DataGridView>("history");
    public TextBox Notes => Field<TextBox>("notes");
    public TextBox Details => Field<TextBox>("details");
    public TextBox ReleaseTitle => Field<TextBox>("releaseTitle");
    public Label Selection => Field<Label>("selectionLabel");
    public Button More => Field<Button>("moreButton");
    public Button Refresh => Field<Button>("refreshButton");
    public Button CheckAll => Field<Button>("selectAllButton");
    public Button UncheckAll => Field<Button>("clearButton");
    public Button Copy => Field<Button>("copyButton");
    public Button Export => Field<Button>("exportButton");
    public DateTimePicker FromDate => Field<DateTimePicker>("fromDate");
    public string Status => Field<ToolStripStatusLabel>("status").Text ?? "";
    public string[] Subjects => History.Rows.Cast<DataGridViewRow>()
        .Select(row => (string)row.Cells["Subject"].Value!).ToArray();

    public async Task OpenAsync(string directory)
    {
        // Bypass only the native folder picker; all subsequent interactions use real controls and events.
        MethodInfo method = typeof(Form1).GetMethod("OpenRepositoryAsync", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(nameof(Form1), "OpenRepositoryAsync");
        await (Task)(method.Invoke(form, [directory])
            ?? throw new InvalidOperationException("OpenRepositoryAsync did not return a task."));
        Assert.IsTrue(Refresh.Enabled, $"Repository did not open: {Status}");
    }

    public async Task SelectFolderAsync(string path, int expectedCount)
    {
        SelectFolder(path);
        await WaitForHistoryAsync(expectedCount);
    }

    public void SelectFolder(string path) =>
        Tree.SelectedNode = FindNode(Tree.Nodes, path)
            ?? throw new AssertFailedException($"Directory not found in tree: {path}");

    public Task WaitForHistoryAsync(int count) => WaitUntilAsync(
        () => History.Rows.Count == count && !Status.Contains("Loading", StringComparison.Ordinal)
            && Status.Contains("commits loaded", StringComparison.Ordinal),
        $"Expected {count} commits. Status: {Status}");

    public void ShowNotes()
    {
        TabPage page = Notes.Parent as TabPage
            ?? throw new AssertFailedException("Notes are not hosted in a tab.");
        ((TabControl)page.Parent!).SelectedTab = page;
    }

    public void CheckCommit(int rowIndex, bool value)
    {
        History.CurrentCell = History.Rows[rowIndex].Cells["Include"];
        Assert.IsTrue(History.BeginEdit(false), "Could not edit the release-note checkbox.");
        History.CurrentCell.Value = value;
        Assert.IsTrue(History.EndEdit(), "Could not commit the release-note checkbox.");
    }

    public static async Task WaitUntilAsync(Func<bool> condition, string message)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > TimeSpan.FromSeconds(10))
                Assert.Fail(message);
            await Task.Delay(20);
        }
    }

    private T Field<T>(string name) where T : class =>
        typeof(Form1).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(form) as T
        ?? throw new MissingFieldException(nameof(Form1), name);

    private static TreeNode? FindNode(TreeNodeCollection nodes, string path)
    {
        foreach (TreeNode node in nodes)
        {
            if (node.Tag as string == path)
                return node;
            if (FindNode(node.Nodes, path) is TreeNode found)
                return found;
        }
        return null;
    }
}
