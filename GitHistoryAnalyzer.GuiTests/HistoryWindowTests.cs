namespace GitHistoryAnalyzer.GuiTests;

[TestClass]
[TestCategory("GUI")]
public sealed class HistoryWindowTests
{
    [TestMethod]
    public Task Startup_DisablesActionsUntilRepositoryIsOpened() => GuiTestHost.RunAsync(window =>
    {
        Assert.AreEqual(0, window.Tree.Nodes.Count);
        Assert.AreEqual(0, window.History.Rows.Count);
        Assert.IsFalse(window.Refresh.Enabled);
        Assert.IsFalse(window.More.Enabled);
        Assert.IsFalse(window.Copy.Enabled);
        Assert.IsFalse(window.Export.Enabled);
        Assert.IsFalse(window.FromDate.Checked);
        Assert.AreEqual("0 selected", window.Selection.Text);
        return Task.CompletedTask;
    });

    [TestMethod]
    public async Task SelectingFolder_FiltersHistoryIncludingDescendantsAndLoadsDetails()
    {
        using var repository = new TestRepository();
        await GuiTestHost.RunAsync(async window =>
        {
            await window.OpenAsync(Path.Combine(repository.Root, "Applications", "Services"));
            Assert.AreEqual(1, window.Tree.Nodes.Count);
            Assert.AreEqual(6, window.History.Rows.Count);
            await window.SelectFolderAsync(TestRepository.ApplicationPath, 3);
            CollectionAssert.AreEqual(
                new[] { TestRepository.UnicodeSubject, "Add nested worker", "Initial applications" }, window.Subjects);
            await HistoryWindow.WaitUntilAsync(
                () => window.Details.Text.Contains("Detailed release context.", StringComparison.Ordinal),
                "Selected commit body was not displayed.");
            StringAssert.Contains(window.Details.Text, "app.txt");
            Assert.IsFalse(window.Details.Text.Contains("other.txt", StringComparison.Ordinal));
            Assert.IsFalse(window.More.Enabled);
            await window.SelectFolderAsync("", 6);
            Assert.HasCount(6, window.Subjects);
        });
    }

    [TestMethod]
    public async Task SelectingLiteralPath_DoesNotMatchWildcardDecoy()
    {
        using var repository = new TestRepository();
        await GuiTestHost.RunAsync(async window =>
        {
            await window.OpenAsync(repository.Root);
            await window.SelectFolderAsync("Apps/[special]", 2);
            CollectionAssert.AreEqual(new[] { "Literal path change", "Initial applications" }, window.Subjects);
        });
    }

    [TestMethod]
    public async Task CheckingCommits_UpdatesMarkdownTitleCountAndActions()
    {
        using var repository = new TestRepository();
        await GuiTestHost.RunAsync(async window =>
        {
            await window.OpenAsync(repository.Root);
            await window.SelectFolderAsync(TestRepository.ApplicationPath, 3);
            window.ShowNotes();
            window.ReleaseTitle.Text = "HTI v1.2";
            window.CheckCommit(0, true);
            Assert.AreEqual("1 selected", window.Selection.Text);
            StringAssert.StartsWith(window.Notes.Text, "# HTI v1\\.2");
            StringAssert.Contains(window.Notes.Text, "Application: " + TestRepository.ApplicationPath);
            StringAssert.Contains(window.Notes.Text, TestRepository.UnicodeSubject);
            Assert.IsFalse(window.Notes.Text.Contains("Add nested worker", StringComparison.Ordinal));
            Assert.IsTrue(window.Copy.Enabled);
            Assert.IsTrue(window.Export.Enabled);

            window.CheckAll.PerformClick();
            Assert.AreEqual("3 selected", window.Selection.Text);
            Assert.AreEqual(3, window.Notes.Lines.Count(line => line.StartsWith("- ", StringComparison.Ordinal)));
            window.UncheckAll.PerformClick();
            Assert.AreEqual("0 selected", window.Selection.Text);
            Assert.IsFalse(window.Copy.Enabled);
            Assert.IsFalse(window.Export.Enabled);

            window.CheckCommit(0, true);
            await window.SelectFolderAsync("Apps/[special]", 2);
            Assert.AreEqual("0 selected", window.Selection.Text);
            Assert.IsFalse(window.Export.Enabled);
        });
    }

    [TestMethod]
    public async Task LoadMore_AppendsOlderCommitsAndPreservesChecks()
    {
        using var repository = new TestRepository(paginated: true);
        await GuiTestHost.RunAsync(async window =>
        {
            await window.OpenAsync(repository.Root);
            await window.SelectFolderAsync("Apps/Paged", 200);
            Assert.AreEqual("Commit 201", window.Subjects[0]);
            Assert.AreEqual("Commit 002", window.Subjects[^1]);
            Assert.IsTrue(window.More.Enabled);
            window.CheckAll.PerformClick();
            window.More.PerformClick();
            await window.WaitForHistoryAsync(201);
            Assert.AreEqual("Commit 001", window.Subjects[^1]);
            Assert.AreEqual(201, window.Subjects.Distinct().Count());
            Assert.AreEqual("200 selected", window.Selection.Text);
            Assert.IsFalse((bool)window.History.Rows[200].Cells["Include"].Value!);
            Assert.IsFalse(window.More.Enabled);
        });
    }

    [TestMethod]
    public async Task Refresh_ReloadsExternalChangesAndRetainsSelectedFolder()
    {
        using var repository = new TestRepository();
        await GuiTestHost.RunAsync(async window =>
        {
            await window.OpenAsync(repository.Root);
            await window.SelectFolderAsync(TestRepository.ApplicationPath, 3);
            window.CheckAll.PerformClick();
            repository.AddApplicationCommit();
            window.Refresh.PerformClick();
            await window.WaitForHistoryAsync(4);
            Assert.AreEqual(TestRepository.ApplicationPath, window.Tree.SelectedNode?.Tag);
            Assert.AreEqual("New application change", window.Subjects[0]);
            Assert.AreEqual("0 selected", window.Selection.Text);
        });
    }

    [TestMethod]
    public async Task RapidFolderSwitching_DiscardsSupersededHistory()
    {
        using var repository = new TestRepository();
        await GuiTestHost.RunAsync(async window =>
        {
            await window.OpenAsync(repository.Root);
            window.SelectFolder(TestRepository.ApplicationPath);
            await window.SelectFolderAsync("Apps/[special]", 2);
            Assert.AreEqual("Apps/[special]", window.Tree.SelectedNode?.Tag);
            CollectionAssert.AreEqual(new[] { "Literal path change", "Initial applications" }, window.Subjects);
        });
    }

    [TestMethod]
    public async Task FromDate_IsInclusiveAndClearsPreviouslyCheckedCommits()
    {
        using var repository = new TestRepository(dated: true);
        await GuiTestHost.RunAsync(async window =>
        {
            await window.OpenAsync(repository.Root);
            await window.SelectFolderAsync("Apps/Dated", 3);
            window.CheckAll.PerformClick();
            Assert.AreEqual("3 selected", window.Selection.Text);
            window.FromDate.Value = new DateTime(2025, 6, 10);
            window.FromDate.Checked = true;
            await window.WaitForHistoryAsync(2);
            CollectionAssert.AreEquivalent(new[] { "At midnight", "After midnight" }, window.Subjects);
            Assert.AreEqual("0 selected", window.Selection.Text);
            Assert.IsFalse(window.Export.Enabled);
            var midnightRow = window.History.Rows.Cast<DataGridViewRow>()
                .Single(row => (string)row.Cells["Subject"].Value! == "At midnight");
            Assert.AreEqual(new DateTime(2025, 6, 10), midnightRow.Cells["Date"].Value);
            Assert.AreEqual("Commit date (local)", window.History.Columns["Date"]!.HeaderText);
            window.CheckAll.PerformClick();
            window.ShowNotes();
            Assert.IsFalse(window.Notes.Text.Contains("Before midnight", StringComparison.Ordinal));
            StringAssert.Contains(window.Notes.Text, "2025-06-10");

            window.FromDate.Value = new DateTime(2025, 6, 11);
            await window.WaitForHistoryAsync(0);
            Assert.AreEqual("0 selected", window.Selection.Text);
            Assert.IsFalse(window.More.Enabled);
            Assert.IsFalse(window.Export.Enabled);

            window.FromDate.Checked = false;
            await window.WaitForHistoryAsync(3);
            Assert.AreEqual("0 selected", window.Selection.Text);
        });
    }

    [TestMethod]
    public async Task FromDate_AppliesBeforePaginationAndSurvivesRefreshAndFolderChanges()
    {
        using var repository = new TestRepository(paginated: true);
        await GuiTestHost.RunAsync(async window =>
        {
            window.FromDate.Value = new DateTime(2025, 1, 2);
            window.FromDate.Checked = true;
            await window.OpenAsync(repository.Root);
            await window.SelectFolderAsync("Apps/Paged", 200);
            Assert.AreEqual("Commit 002", window.Subjects[^1]);
            Assert.IsTrue(window.More.Enabled);
            window.CheckAll.PerformClick();
            window.More.PerformClick();
            await HistoryWindow.WaitUntilAsync(() => !window.More.Enabled && window.Status.Contains("End of history", StringComparison.Ordinal),
                "Date-filtered pagination did not reach the end.");
            Assert.AreEqual(200, window.History.Rows.Count);
            Assert.IsFalse(window.Subjects.Contains("Commit 001"));
            Assert.AreEqual("200 selected", window.Selection.Text);

            window.Refresh.PerformClick();
            await window.WaitForHistoryAsync(200);
            Assert.IsTrue(window.FromDate.Checked);
            Assert.AreEqual("0 selected", window.Selection.Text);
            await window.SelectFolderAsync("", 200);
            Assert.IsFalse(window.Subjects.Contains("Commit 001"));
            window.FromDate.Checked = false;
            await window.WaitForHistoryAsync(200);
            window.More.PerformClick();
            await window.WaitForHistoryAsync(201);
            Assert.AreEqual("Commit 001", window.Subjects[^1]);
        });
    }

    [TestMethod]
    public async Task MinimumWindowSize_KeepsReleaseActionsVisible()
    {
        using var repository = new TestRepository();
        await GuiTestHost.RunAsync(async window =>
        {
            await window.OpenAsync(repository.Root);
            window.Form.Size = window.Form.MinimumSize;
            window.ShowNotes();
            window.Form.PerformLayout();
            Assert.IsTrue(window.FromDate.Visible);
            Assert.IsTrue(window.FromDate.Parent!.ClientRectangle.Contains(window.FromDate.Bounds));
            foreach (Control control in new Control[] { window.ReleaseTitle, window.Copy, window.Export, window.Selection })
            {
                Assert.IsTrue(control.Visible, $"{control.Text} is hidden.");
                Assert.IsNotNull(control.Parent);
                Assert.IsTrue(control.Parent.ClientRectangle.Contains(control.Bounds), $"{control.Text} is clipped.");
            }
        });
    }
}
