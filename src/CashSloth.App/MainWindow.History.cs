using System.Globalization;
using System.Windows;

namespace CashSloth.App;

public partial class MainWindow
{
    private void RefreshHistoryToolsUi(bool updateStatusOnError)
    {
        if (!_saleHistoryStore.TryGetActiveRecording(out var active, out var activeError))
        {
            if (updateStatusOnError)
            {
                StatusText.Text = $"History recording status could not be loaded: {activeError}";
            }
            return;
        }

        ActiveRecordingStatusText.Text = active is null
            ? "No recording is active."
            : $"Recording '{active.Name}' since {active.StartedAtUtc.LocalDateTime:g}.";

        if (_saleHistoryStore.TryListRecordings(out var recordings, out var recordingsError))
        {
            var selectedId = (HistoryRecordingsListBox.SelectedItem as HistoryRecordingListItem)?.Recording.Id;
            var items = recordings.Select(value => new HistoryRecordingListItem(value)).ToArray();
            HistoryRecordingsListBox.ItemsSource = items;
            HistoryRecordingsListBox.SelectedItem = items.FirstOrDefault(value => value.Recording.Id == selectedId)
                                                        ?? items.FirstOrDefault();
        }
        else if (updateStatusOnError)
        {
            StatusText.Text = $"History recordings could not be loaded: {recordingsError}";
        }

        if (_saleHistoryStore.TryListArchives(out var archives, out var archivesError))
        {
            var selectedId = (HistoryArchivesListBox.SelectedItem as HistoryArchiveListItem)?.Archive.Id;
            var items = archives.Select(value => new HistoryArchiveListItem(value)).ToArray();
            HistoryArchivesListBox.ItemsSource = items;
            HistoryArchivesListBox.SelectedItem = items.FirstOrDefault(value => value.Archive.Id == selectedId)
                                                     ?? items.FirstOrDefault();
        }
        else if (updateStatusOnError)
        {
            StatusText.Text = $"History archives could not be loaded: {archivesError}";
        }
    }

    private void OnStartHistoryRecordingClick(object sender, RoutedEventArgs e)
    {
        if (!_saleHistoryStore.TryStartRecording(RecordingNameTextBox.Text, out var recording, out var error) || recording is null)
        {
            StatusText.Text = $"History recording could not start: {error}";
            return;
        }
        RecordingNameTextBox.Text = string.Empty;
        RefreshHistoryToolsUi(updateStatusOnError: true);
        StatusText.Text = $"History recording '{recording.Name}' started.";
    }

    private void OnStopHistoryRecordingClick(object sender, RoutedEventArgs e)
    {
        if (!_saleHistoryStore.TryStopActiveRecording(out var recording, out var error) || recording is null)
        {
            StatusText.Text = $"History recording could not stop: {error}";
            return;
        }
        RefreshHistoryToolsUi(updateStatusOnError: true);
        StatusText.Text = $"History recording '{recording.Name}' stopped with {recording.SaleCount} sale(s).";
    }

    private void OnExportHistoryRecordingClick(object sender, RoutedEventArgs e)
    {
        if (HistoryRecordingsListBox.SelectedItem is not HistoryRecordingListItem selected)
        {
            StatusText.Text = "Select a history recording to export.";
            return;
        }
        if (selected.Recording.EndedAtUtc is null)
        {
            StatusText.Text = "Stop the recording before exporting it.";
            return;
        }
        if (!_saleHistoryStore.TryListRecordingSales(selected.Recording.Id, out var sales, out var error))
        {
            StatusText.Text = $"Recording data could not be loaded: {error}";
            return;
        }

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a folder for the CashSloth recording export",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }
        try
        {
            var folder = HistoryExportService.ExportRecording(dialog.SelectedPath, selected.Recording, sales);
            StatusText.Text = $"Recording exported to {folder}.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Recording export failed: {exception.Message}";
        }
    }

    private void OnResetHistoryClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "Reset the visible local history? It will be archived and can be restored later. Server event history is not affected.",
                "Reset history", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        if (!_saleHistoryStore.TryArchiveCurrentHistory(out var archive, out var error) || archive is null)
        {
            StatusText.Text = $"History could not be reset: {error}";
            return;
        }
        RefreshSaleHistoryUi(updateStatusOnError: true);
        StatusText.Text = $"History archived as '{archive.Name}'.";
    }

    private void OnRestoreHistoryClick(object sender, RoutedEventArgs e)
    {
        if (HistoryArchivesListBox.SelectedItem is not HistoryArchiveListItem selected)
        {
            StatusText.Text = "Select a history archive to restore.";
            return;
        }
        if (MessageBox.Show(this,
                $"Restore '{selected.Archive.Name}' into the current local history?",
                "Restore history", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }
        if (!_saleHistoryStore.TryRestoreArchive(selected.Archive.Id, out var error))
        {
            StatusText.Text = $"History archive could not be restored: {error}";
            return;
        }
        RefreshSaleHistoryUi(updateStatusOnError: true);
        StatusText.Text = $"History archive '{selected.Archive.Name}' restored.";
    }

    private sealed record HistoryRecordingListItem(HistoryRecording Recording)
    {
        public string DisplayLabel =>
            $"{Recording.Name} · {Recording.StartedAtUtc.LocalDateTime:g}{(Recording.EndedAtUtc is null ? " · recording" : $"–{Recording.EndedAtUtc.Value.LocalDateTime:g}")} · {Recording.SaleCount} sales · {CurrencyFormatter.FormatCents(Recording.TotalCents)}";
    }

    private sealed record HistoryArchiveListItem(HistoryArchive Archive)
    {
        public string DisplayLabel =>
            $"{Archive.Name} · {Archive.CreatedAtUtc.LocalDateTime.ToString("g", CultureInfo.CurrentCulture)} · {Archive.SaleCount} sales · {CurrencyFormatter.FormatCents(Archive.TotalCents)}";
    }
}
