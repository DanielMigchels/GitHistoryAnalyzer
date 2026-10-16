namespace GitHistoryAnalyzer;

public partial class Form1 : Form
{
    private const int PageSize = 200;
    private readonly TextBox repositoryPath = new() { Dock = DockStyle.Fill, ReadOnly = true, PlaceholderText = "Open a local Git repository..." };
    private readonly Button openButton = new() { Text = "Open repository...", AutoSize = true };
    private readonly Button refreshButton = new() { Text = "Refresh", AutoSize = true, Enabled = false };
    private readonly TreeView directories = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly Label folderLabel = new() { Text = "Select an application folder", AutoSize = true, Margin = new Padding(6, 10, 6, 8) };
    private readonly DataGridView history = new()
    {
        Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, AllowUserToResizeRows = false, RowHeadersVisible = false,
        MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.None
    };
    private readonly Button moreButton = new() { Text = "Load more commits", AutoSize = true, Enabled = false };
    private readonly Button selectAllButton = new() { Text = "Check all loaded", AutoSize = true };
    private readonly Button clearButton = new() { Text = "Uncheck all", AutoSize = true };
    private readonly DateTimePicker fromDate = new()
    {
        Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd",
        ShowCheckBox = true, Checked = false, Width = 155
    };
    private readonly TextBox details = MultilineText();
    private readonly TextBox notes = MultilineText();
    private readonly TextBox releaseTitle = new() { Text = "Release notes", Width = 300 };
    private readonly Label selectionLabel = new() { AutoSize = true, Margin = new Padding(8) };
    private readonly Button copyButton = new() { Text = "Copy Markdown", AutoSize = true, Enabled = false };
    private readonly Button exportButton = new() { Text = "Export Markdown...", AutoSize = true, Enabled = false };
    private readonly ToolStripStatusLabel status = new() { Text = "Open a repository to browse application history." };
    private GitRepository? repository;
    private string selectedPath = "";
    private CancellationTokenSource? historyCancellation;
    private CancellationTokenSource? detailsCancellation;
    private CancellationTokenSource? openCancellation;
    private bool updatingTree;
    private bool updatingChecks;

    public Form1()
    {
        InitializeComponent();
        BuildLayout();
        openButton.Click += async (_, _) => await ChooseRepositoryAsync();
        refreshButton.Click += async (_, _) =>
        {
            if (repository is not null)
                await OpenRepositoryAsync(repository.Root);
        };
        directories.AfterSelect += async (_, e) =>
        {
            if (!updatingTree && e.Node?.Tag is string path)
            {
                selectedPath = path;
                await LoadHistoryAsync(false);
            }
        };
        moreButton.Click += async (_, _) => await LoadHistoryAsync(true);
        fromDate.ValueChanged += async (_, _) =>
        {
            if (openCancellation is null)
                await LoadHistoryAsync(false);
        };
        history.SelectionChanged += async (_, _) => await LoadDetailsAsync();
        history.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (history.IsCurrentCellDirty)
                history.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        history.CellValueChanged += (_, e) =>
        {
            if (e.ColumnIndex == 0 && !updatingChecks)
                UpdateNotes();
        };
        selectAllButton.Click += (_, _) => SetChecks(true);
        clearButton.Click += (_, _) => SetChecks(false);
        releaseTitle.TextChanged += (_, _) => UpdateNotes();
        copyButton.Click += (_, _) => CopyNotes();
        exportButton.Click += async (_, _) => await ExportNotesAsync();
        FormClosing += (_, _) =>
        {
            openCancellation?.Cancel();
            historyCancellation?.Cancel();
            detailsCancellation?.Cancel();
        };
        UpdateNotes();
    }

    private static TextBox MultilineText() => new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
        WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 10)
    };

    private void BuildLayout()
    {
        var toolbar = new TableLayoutPanel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8), ColumnCount = 3 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.Controls.Add(repositoryPath, 0, 0);
        toolbar.Controls.Add(openButton, 1, 0);
        toolbar.Controls.Add(refreshButton, 2, 0);

        history.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Notes", Width = 55 });
        history.Columns.Add(new DataGridViewTextBoxColumn { Name = "Hash", HeaderText = "Commit", Width = 90, ReadOnly = true });
        history.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Date", HeaderText = "Commit date (local)", Width = 155, ReadOnly = true,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "yyyy-MM-dd HH:mm" }
        });
        history.Columns.Add(new DataGridViewTextBoxColumn { Name = "Author", HeaderText = "Author", Width = 150, ReadOnly = true });
        history.Columns.Add(new DataGridViewTextBoxColumn { Name = "Subject", HeaderText = "Message", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 150, ReadOnly = true });
        foreach (DataGridViewColumn column in history.Columns)
            column.SortMode = DataGridViewColumnSortMode.NotSortable;

        var historyActions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(4) };
        historyActions.Controls.AddRange([selectAllButton, clearButton, moreButton]);
        var historyPanel = new Panel { Dock = DockStyle.Fill };
        var dateFilter = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(4) };
        dateFilter.Controls.AddRange([
            new Label { Text = "From date (inclusive):", AutoSize = true, Margin = new Padding(4, 7, 4, 4) },
            fromDate
        ]);
        folderLabel.Dock = DockStyle.Top;
        historyPanel.Controls.Add(history);
        historyPanel.Controls.Add(historyActions);
        historyPanel.Controls.Add(dateFilter);
        historyPanel.Controls.Add(folderLabel);

        var noteActions = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 80, Padding = new Padding(4), ColumnCount = 2, RowCount = 2
        };
        noteActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        noteActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        noteActions.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        noteActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        noteActions.Controls.Add(new Label { Text = "Release title:", AutoSize = true, Margin = new Padding(4, 7, 4, 4) }, 0, 0);
        releaseTitle.Dock = DockStyle.Fill;
        noteActions.Controls.Add(releaseTitle, 1, 0);
        var exportActions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        exportActions.Controls.AddRange([copyButton, exportButton, selectionLabel]);
        noteActions.Controls.Add(exportActions, 0, 1);
        noteActions.SetColumnSpan(exportActions, 2);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var commitTab = new TabPage("Commit details");
        commitTab.Controls.Add(details);
        var notesTab = new TabPage("Release notes (Markdown)");
        notesTab.Controls.Add(notes);
        notesTab.Controls.Add(noteActions);
        tabs.TabPages.AddRange([commitTab, notesTab]);
        var content = new SplitContainer
        {
            Dock = DockStyle.Fill, Orientation = Orientation.Horizontal,
            Size = new Size(950, 700), SplitterDistance = 360, Panel1MinSize = 150, Panel2MinSize = 180
        };
        content.Panel1.Controls.Add(historyPanel);
        content.Panel2.Controls.Add(tabs);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1280, 700), SplitterDistance = 280, Panel1MinSize = 180, Panel2MinSize = 550 };
        split.Panel1.Controls.Add(directories);
        split.Panel2.Controls.Add(content);
        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(status);
        Controls.Add(split);
        Controls.Add(toolbar);
        Controls.Add(statusStrip);
    }

    private async Task ChooseRepositoryAsync()
    {
        using var dialog = new FolderBrowserDialog { Description = "Select a local Git repository (or a folder inside it)", UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            await OpenRepositoryAsync(dialog.SelectedPath);
    }

    private async Task OpenRepositoryAsync(string directory)
    {
        using var cancellation = new CancellationTokenSource();
        openCancellation = cancellation;
        historyCancellation?.Cancel();
        detailsCancellation?.Cancel();
        openButton.Enabled = refreshButton.Enabled = directories.Enabled = moreButton.Enabled = false;
        status.Text = "Opening repository...";
        try
        {
            GitRepository opened = await GitRepository.OpenAsync(directory, cancellation.Token);
            IReadOnlyList<string> paths = await opened.GetDirectoriesAsync(cancellation.Token);
            string previousPath = repository?.Root == opened.Root ? selectedPath : "";
            repository = opened;
            repositoryPath.Text = opened.Root;
            updatingTree = true;
            directories.BeginUpdate();
            try
            {
                directories.Nodes.Clear();
                var root = new TreeNode(new DirectoryInfo(opened.Root).Name) { Tag = "" };
                directories.Nodes.Add(root);
                var nodes = new Dictionary<string, TreeNode>(StringComparer.Ordinal) { [""] = root };
                foreach (string path in paths.Order(StringComparer.OrdinalIgnoreCase))
                {
                    string parent = "";
                    foreach (string part in path.Split('/'))
                    {
                        string current = parent.Length == 0 ? part : $"{parent}/{part}";
                        if (!nodes.TryGetValue(current, out TreeNode? node))
                        {
                            node = new TreeNode(part) { Tag = current };
                            nodes[parent].Nodes.Add(node);
                            nodes.Add(current, node);
                        }
                        parent = current;
                    }
                }
                root.Expand();
                TreeNode selected = nodes.GetValueOrDefault(previousPath, root);
                selectedPath = (string)selected.Tag!;
                directories.SelectedNode = selected;
                selected.EnsureVisible();
            }
            finally
            {
                directories.EndUpdate();
                updatingTree = false;
            }
            await LoadHistoryAsync(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) { ShowError("Unable to open repository", exception); }
        finally
        {
            openCancellation = null;
            if (!IsDisposed && !Disposing)
            {
                openButton.Enabled = true;
                refreshButton.Enabled = directories.Enabled = repository is not null;
            }
        }
    }

    private async Task LoadHistoryAsync(bool append)
    {
        historyCancellation?.Cancel();
        detailsCancellation?.Cancel();
        if (repository is null)
            return;
        using var cancellation = new CancellationTokenSource();
        historyCancellation = cancellation;
        string path = selectedPath;
        DateTimeOffset? start = fromDate.Checked
            ? new DateTimeOffset(DateTime.SpecifyKind(fromDate.Value.Date, DateTimeKind.Local))
            : null;
        moreButton.Enabled = false;
        if (!append)
        {
            history.Rows.Clear();
            details.Clear();
            UpdateNotes();
        }
        folderLabel.Text = path.Length == 0 ? "Repository root (all applications)" : path;
        status.Text = "Loading commits...";
        try
        {
            IReadOnlyList<GitCommit> commits = await repository.GetHistoryAsync(path, history.Rows.Count, PageSize, cancellation.Token, start);
            cancellation.Token.ThrowIfCancellationRequested();
            foreach (GitCommit commit in commits)
            {
                int row = history.Rows.Add(false, commit.Hash[..8], commit.Date.LocalDateTime, commit.Author, commit.Subject);
                history.Rows[row].Tag = commit;
            }
            moreButton.Enabled = commits.Count == PageSize;
            status.Text = $"{history.Rows.Count} commits loaded for {(path.Length == 0 ? "repository root" : path)}. " +
                (start is DateTimeOffset date ? $"From {date:yyyy-MM-dd} (inclusive). " : "") +
                (moreButton.Enabled ? "Load more to see older commits." : "End of history.");
            await LoadDetailsAsync();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested)
                ShowError("Unable to load history", exception);
        }
        finally
        {
            if (ReferenceEquals(historyCancellation, cancellation))
                historyCancellation = null;
        }
    }

    private async Task LoadDetailsAsync()
    {
        detailsCancellation?.Cancel();
        details.Clear();
        if (repository is null || history.CurrentRow?.Tag is not GitCommit commit)
            return;
        using var cancellation = new CancellationTokenSource();
        detailsCancellation = cancellation;
        details.Text = "Loading commit details...";
        try
        {
            string text = await repository.GetDetailsAsync(commit, selectedPath, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            details.Text = text.ReplaceLineEndings(Environment.NewLine);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            if (!cancellation.IsCancellationRequested)
            {
                details.Text = exception.Message;
                ShowError("Unable to load commit details", exception);
            }
        }
        finally
        {
            if (ReferenceEquals(detailsCancellation, cancellation))
                detailsCancellation = null;
        }
    }

    private void SetChecks(bool value)
    {
        updatingChecks = true;
        try
        {
            foreach (DataGridViewRow row in history.Rows)
                row.Cells[0].Value = value;
        }
        finally { updatingChecks = false; }
        UpdateNotes();
    }

    private void UpdateNotes()
    {
        var commits = history.Rows.Cast<DataGridViewRow>()
            .Where(row => row.Cells[0].Value is true)
            .Select(row => row.Tag).OfType<GitCommit>().ToList();
        notes.Text = ReleaseNotes.Create(
            string.IsNullOrWhiteSpace(releaseTitle.Text) ? "Release notes" : releaseTitle.Text.Trim(),
            selectedPath, commits);
        selectionLabel.Text = $"{commits.Count} selected";
        copyButton.Enabled = exportButton.Enabled = commits.Count > 0;
    }

    private void CopyNotes()
    {
        try
        {
            Clipboard.SetText(notes.Text);
            status.Text = "Release notes copied to clipboard.";
        }
        catch (Exception exception) { ShowError("Unable to copy release notes", exception); }
    }

    private async Task ExportNotesAsync()
    {
        using var dialog = new SaveFileDialog { Filter = "Markdown files (*.md)|*.md", FileName = "release-notes.md", DefaultExt = "md", AddExtension = true };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            await File.WriteAllTextAsync(dialog.FileName, notes.Text, new System.Text.UTF8Encoding(false));
            if (!IsDisposed && !Disposing)
                status.Text = $"Release notes saved to {dialog.FileName}";
        }
        catch (Exception exception) { ShowError("Unable to export release notes", exception); }
    }

    private void ShowError(string title, Exception exception)
    {
        if (IsDisposed || Disposing)
            return;
        status.Text = title + ".";
        MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
