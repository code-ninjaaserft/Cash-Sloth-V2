using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CashSloth.Contracts;

namespace CashSloth.App;

public partial class MainWindow
{
    private readonly CashSlothEventCoordinator _eventCoordinator;
    private readonly ObservableCollection<ServerEventListItem> _serverEventItems = [];
    private readonly ObservableCollection<ServerEventMemberListItem> _serverEventMemberItems = [];
    private readonly ObservableCollection<ServerPresetChoice> _eventPresetItems = [];
    private bool _eventCoordinatorStarted;
    private EventDetailResponse? _editingEventDraft;

    private void InitializeServerEventUi()
    {
        OnlineEventsListBox.ItemsSource = _serverEventItems;
        ServerEventMembersListBox.ItemsSource = _serverEventMemberItems;
        EventPresetComboBox.ItemsSource = _eventPresetItems;
        EventPresetComboBox.SelectedValuePath = nameof(ServerPresetChoice.Id);
        RefreshEventPaymentMethods(null);
        ApplyServerEventUi(_eventCoordinator.Current);

        if (_currentUser is { MustChangePassword: false })
        {
            _ = RefreshServerEventsAsync(updateStatusOnError: false);
        }
    }

    private async Task StartEventCoordinatorAsync()
    {
        if (_eventCoordinatorStarted)
        {
            return;
        }

        _eventCoordinatorStarted = true;
        await _eventCoordinator.StartAsync();
        if (_eventCoordinator.Current is not null)
        {
            ApplyServerEventPreset(_eventCoordinator.Current.Event);
            await RefreshActiveServerEventAsync(updateStatusOnError: false);
        }
    }

    private void OnCashSlothEventSessionChanged(CashSlothLocalEventSession? session)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => OnCashSlothEventSessionChanged(session));
            return;
        }

        ApplyServerEventUi(session);
        if (session is not null)
        {
            ApplyServerEventPreset(session.Event);
            _ = RefreshActiveServerEventStatisticsAsync(updateStatusOnError: false);
        }
    }

    private void OnCashSlothEventStatusChanged(string status)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => OnCashSlothEventStatusChanged(status));
            return;
        }

        ActiveEventSyncText.Text = status;
        StatusText.Text = status;
        RefreshEventCheckoutAvailability();
    }

    private void OnCashSlothEventSalesSynchronised(IReadOnlyList<EventSaleUploadResult> results)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(() => OnCashSlothEventSalesSynchronised(results));
            return;
        }

        RefreshEventCheckoutAvailability();
        _ = RefreshActiveServerEventStatisticsAsync(updateStatusOnError: false);
    }

    private void RefreshEventAccessUi()
    {
        var inEvent = _eventCoordinator.Current is not null;
        var canCreate = HasRole(CashSlothRole.Creator) && !inEvent;
        EventCreatorPanel.Visibility = canCreate ? Visibility.Visible : Visibility.Collapsed;
        EventLobbyPanel.Visibility = inEvent ? Visibility.Collapsed : Visibility.Visible;
        ActiveEventPanel.Visibility = inEvent ? Visibility.Visible : Visibility.Collapsed;

        PresetsTab.Visibility = inEvent ? Visibility.Collapsed : Visibility.Visible;
        AccountsTab.Visibility = inEvent ? Visibility.Collapsed : Visibility.Visible;
        EditModeCheckBox.IsChecked = false;
        EditModeCheckBox.IsEnabled = !inEvent;
        LogoutButton.IsEnabled = _currentUser is not null && !inEvent;

        if (!inEvent && HasRole(CashSlothRole.User))
        {
            _ = RefreshServerEventsAsync(updateStatusOnError: false);
        }
    }

    private void ApplyServerEventUi(CashSlothLocalEventSession? session)
    {
        RefreshEventAccessUi();
        if (session is null)
        {
            EventNameTextBox.Text = DefaultEventName;
            RegisterNameTextBox.Text = DefaultRegisterName;
            ActiveEventTitleText.Text = "Event";
            ActiveEventIdentityText.Text = string.Empty;
            ActiveEventStateText.Text = "Not joined";
            ActiveEventSyncText.Text = "Offline";
            _serverEventMemberItems.Clear();
            RefreshEventPaymentMethods(null);
            SetCustomerDisplayEventIdentity(null);
            RefreshEventCheckoutAvailability();
            return;
        }

        var detail = session.Event;
        var member = session.Membership;
        EventNameTextBox.Text = detail.Name;
        RegisterNameTextBox.Text = member.Nickname;
        ActiveEventTitleText.Text = detail.Name;
        ActiveEventIdentityText.Text = $"{member.Nickname} · {member.Role}";
        ActiveEventStateText.Text = detail.State.ToString();
        ActiveEventSyncText.Text = _clientLeaseText(session);
        EventRulesSummaryText.Text = BuildRulesSummary(detail.Rules);
        EventHostMemberActionsPanel.Visibility = _eventCoordinator.IsHost ? Visibility.Visible : Visibility.Collapsed;
        CloseEventButton.Visibility = _eventCoordinator.IsHost && detail.State == CashSlothEventState.Active ? Visibility.Visible : Visibility.Collapsed;
        FinalizeEventButton.Visibility = _eventCoordinator.IsHost && detail.State == CashSlothEventState.Closing ? Visibility.Visible : Visibility.Collapsed;
        LeaveEventButton.Visibility = _eventCoordinator.IsHost && detail.State is CashSlothEventState.Active or CashSlothEventState.Closing
            ? Visibility.Collapsed
            : Visibility.Visible;
        LeaveEventButton.Content = detail.State is CashSlothEventState.Ended or CashSlothEventState.Cancelled ? "Exit event" : "Leave event";

        _serverEventMemberItems.Clear();
        foreach (var eventMember in detail.Members)
        {
            _serverEventMemberItems.Add(new ServerEventMemberListItem(eventMember));
        }

        RefreshEventPaymentMethods(detail.Rules);
        EventTipAmountTextBox.IsEnabled = detail.Rules.AllowTips;
        if (!detail.Rules.AllowTips)
        {
            EventTipAmountTextBox.Text = string.Empty;
        }
        EventShowcaseModeCheckBox.IsEnabled = detail.Rules.AllowShowcase;
        if (!detail.Rules.AllowShowcase)
        {
            EventShowcaseModeCheckBox.IsChecked = false;
        }
        SetCustomerDisplayEventIdentity(session);
        RefreshEventCheckoutAvailability();

        static string _clientLeaseText(CashSlothLocalEventSession value) =>
            $"Offline lease until {value.OfflineUntilUtc.LocalDateTime:g}";
    }

    private void RefreshEventCheckoutAvailability()
    {
        var allowed = _eventCoordinator.Current is null || _eventCoordinator.CanCheckout;
        ShopCompleteSaleButton.IsEnabled = allowed;
        CompleteSaleButton.IsEnabled = allowed;
        if (_eventCoordinator.Current is { } session && !allowed)
        {
            ActiveEventSyncText.Text = session.Event.State == CashSlothEventState.Closing
                ? "Checkout stopped: event is closing."
                : "Checkout stopped: offline lease expired.";
        }
    }

    private void RefreshEventPaymentMethods(EventRulesDocument? rules)
    {
        var selected = EventPaymentMethodComboBox.SelectedValue as string;
        var allowed = rules?.AllowedPaymentMethods?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = UiLocalizer.BuildPaymentMethodOptions(_settings.Language)
            .Where(value => allowed is null || allowed.Contains(value.Value))
            .ToArray();
        EventPaymentMethodComboBox.ItemsSource = options;
        EventPaymentMethodComboBox.SelectedValue = selected is not null && options.Any(value => value.Value == selected)
            ? selected
            : options.FirstOrDefault()?.Value;
    }

    private static string BuildRulesSummary(EventRulesDocument rules)
    {
        var tips = rules.AllowTips ? "tips allowed" : "no tips";
        var showcase = rules.AllowShowcase ? "showcase allowed" : "no showcase";
        return $"{string.Join(", ", rules.AllowedPaymentMethods)} · {tips} · {showcase}";
    }

    private async void OnRefreshServerEventsClick(object sender, RoutedEventArgs e) =>
        await RefreshServerEventsAsync(updateStatusOnError: true);

    private async Task RefreshServerEventsAsync(bool updateStatusOnError)
    {
        if (!HasRole(CashSlothRole.User) || _eventCoordinator.Current is not null)
        {
            _serverEventItems.Clear();
            return;
        }

        try
        {
            var selectedEventId = (OnlineEventsListBox.SelectedItem as ServerEventListItem)?.Event.Id;
            var events = await _serverClient.GetEventsAsync(includeOwnedDrafts: HasRole(CashSlothRole.Creator));
            _serverEventItems.Clear();
            foreach (var item in events)
            {
                _serverEventItems.Add(new ServerEventListItem(item));
            }
            OnlineEventsListBox.SelectedItem = _serverEventItems.FirstOrDefault(value => value.Event.Id == selectedEventId)
                                                   ?? _serverEventItems.FirstOrDefault();

            if (HasRole(CashSlothRole.Creator))
            {
                var selectedPresetId = EventPresetComboBox.SelectedValue as string;
                var presets = await _serverClient.GetPresetsAsync();
                _eventPresetItems.Clear();
                foreach (var preset in presets)
                {
                    _eventPresetItems.Add(new ServerPresetChoice(preset.Id, preset.Name, preset.Version, preset.ItemCount));
                }
                EventPresetComboBox.SelectedValue = _eventPresetItems.Any(value => value.Id == selectedPresetId)
                    ? selectedPresetId
                    : _editingEventDraft?.PresetId ?? _eventPresetItems.FirstOrDefault()?.Id;
            }
        }
        catch (Exception exception)
        {
            if (updateStatusOnError)
            {
                StatusText.Text = $"Events could not be loaded: {exception.Message}";
            }
        }
    }

    private async void OnSaveEventDraftClick(object sender, RoutedEventArgs e)
    {
        if (!EnsureRole(CashSlothRole.Creator, "create an event"))
        {
            return;
        }
        if (EventPresetComboBox.SelectedItem is not ServerPresetChoice preset)
        {
            StatusText.Text = "Select a central preset for the event.";
            return;
        }
        try
        {
            var name = CreateEventNameTextBox.Text.Trim();
            var hostNickname = CreateEventHostNicknameTextBox.Text.Trim();
            var joinMode = EventRequiresCodeCheckBox.IsChecked == true ? CashSlothEventJoinMode.Code : CashSlothEventJoinMode.Open;
            var rules = ReadEventRulesFromEditor();
            var draft = _editingEventDraft is { State: CashSlothEventState.Draft } editing
                ? await _serverClient.UpdateEventDraftAsync(editing.Id, new EventUpdateDraftRequest(
                    name, hostNickname, preset.Id, preset.Version, joinMode, rules, editing.Version))
                : await _serverClient.CreateEventDraftAsync(new EventCreateRequest(
                    name, hostNickname, preset.Id, preset.Version, joinMode, rules));
            _editingEventDraft = draft;
            await RefreshServerEventsAsync(updateStatusOnError: true);
            OnlineEventsListBox.SelectedItem = _serverEventItems.FirstOrDefault(value => value.Event.Id == draft.Id);
            StatusText.Text = $"Event draft '{draft.Name}' saved. Start it when its rules are final.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Event draft could not be saved: {exception.Message}";
        }
    }

    private void OnNewEventDraftClick(object sender, RoutedEventArgs e)
    {
        _editingEventDraft = null;
        OnlineEventsListBox.SelectedItem = null;
        CreateEventNameTextBox.Text = string.Empty;
        CreateEventHostNicknameTextBox.Text = "Kasse 1";
        EventRequiresCodeCheckBox.IsChecked = false;
        EventAllowCashCheckBox.IsChecked = true;
        EventAllowCardCheckBox.IsChecked = true;
        EventAllowTwintCheckBox.IsChecked = true;
        EventAllowRfidCheckBox.IsChecked = false;
        EventAllowMobileCheckBox.IsChecked = false;
        EventAllowTipsCheckBox.IsChecked = true;
        EventAllowShowcaseCheckBox.IsChecked = false;
        EventPresetComboBox.SelectedItem = _eventPresetItems.FirstOrDefault();
        StatusText.Text = "New event draft ready.";
    }

    private async void OnServerEventSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (OnlineEventsListBox.SelectedItem is not ServerEventListItem selected ||
            selected.Event.State != CashSlothEventState.Draft ||
            !string.Equals(selected.Event.HostUsername, _currentUser?.Username, StringComparison.OrdinalIgnoreCase))
        {
            _editingEventDraft = null;
            return;
        }
        try
        {
            var draft = await _serverClient.GetEventAsync(selected.Event.Id);
            _editingEventDraft = draft;
            CreateEventNameTextBox.Text = draft.Name;
            CreateEventHostNicknameTextBox.Text = draft.HostNickname;
            EventPresetComboBox.SelectedValue = draft.PresetId;
            EventRequiresCodeCheckBox.IsChecked = draft.JoinMode == CashSlothEventJoinMode.Code;
            var methods = draft.Rules.AllowedPaymentMethods.ToHashSet(StringComparer.OrdinalIgnoreCase);
            EventAllowCashCheckBox.IsChecked = methods.Contains("Cash");
            EventAllowCardCheckBox.IsChecked = methods.Contains("Card");
            EventAllowTwintCheckBox.IsChecked = methods.Contains("TWINT");
            EventAllowRfidCheckBox.IsChecked = methods.Contains("RFID/NFC");
            EventAllowMobileCheckBox.IsChecked = methods.Contains("Mobile");
            EventAllowTipsCheckBox.IsChecked = draft.Rules.AllowTips;
            EventAllowShowcaseCheckBox.IsChecked = draft.Rules.AllowShowcase;
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Event draft could not be loaded: {exception.Message}";
        }
    }

    private async void OnCancelEventDraftClick(object sender, RoutedEventArgs e)
    {
        if (_editingEventDraft is not { State: CashSlothEventState.Draft } draft)
        {
            StatusText.Text = "Select one of your event drafts first.";
            return;
        }
        if (MessageBox.Show(this, $"Cancel the draft '{draft.Name}'?", "Cancel event draft",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        try
        {
            await _serverClient.CancelEventDraftAsync(draft.Id);
            _editingEventDraft = null;
            await RefreshServerEventsAsync(updateStatusOnError: true);
            StatusText.Text = $"Event draft '{draft.Name}' cancelled.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Event draft could not be cancelled: {exception.Message}";
        }
    }

    private EventRulesDocument ReadEventRulesFromEditor()
    {
        var methods = new List<string>();
        if (EventAllowCashCheckBox.IsChecked == true) methods.Add("Cash");
        if (EventAllowCardCheckBox.IsChecked == true) methods.Add("Card");
        if (EventAllowTwintCheckBox.IsChecked == true) methods.Add("TWINT");
        if (EventAllowRfidCheckBox.IsChecked == true) methods.Add("RFID/NFC");
        if (EventAllowMobileCheckBox.IsChecked == true) methods.Add("Mobile");
        return new EventRulesDocument(methods.ToArray(), EventAllowTipsCheckBox.IsChecked == true, EventAllowShowcaseCheckBox.IsChecked == true);
    }

    private async void OnPublishServerEventClick(object sender, RoutedEventArgs e)
    {
        if (OnlineEventsListBox.SelectedItem is not ServerEventListItem selected || selected.Event.State != CashSlothEventState.Draft)
        {
            StatusText.Text = "Select one of your event drafts first.";
            return;
        }
        try
        {
            var published = await _serverClient.PublishEventAsync(selected.Event.Id);
            await _eventCoordinator.ActivateAsync(
                published.Event,
                published.Membership,
                published.OfflineLease,
                published.OfflineUntilUtc,
                _activePresetId);
            if (!string.IsNullOrWhiteSpace(published.JoinCode))
            {
                Clipboard.SetText(published.JoinCode);
                MessageBox.Show(this,
                    $"Entry code: {published.JoinCode}\n\nThe code was copied to the clipboard and is shown only once.",
                    "Event started",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            StatusText.Text = $"Event '{published.Event.Name}' is active. Its rules and preset are now frozen.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Event could not be started: {exception.Message}";
        }
    }

    private async void OnJoinServerEventClick(object sender, RoutedEventArgs e)
    {
        if (OnlineEventsListBox.SelectedItem is not ServerEventListItem selected || selected.Event.State != CashSlothEventState.Active)
        {
            StatusText.Text = "Select an active event first.";
            return;
        }
        try
        {
            EventMembershipResponse membership;
            if (string.Equals(selected.Event.HostUsername, _currentUser?.Username, StringComparison.OrdinalIgnoreCase))
            {
                membership = await _serverClient.ResumeEventHostAsync(selected.Event.Id);
            }
            else
            {
                membership = await _serverClient.JoinEventAsync(selected.Event.Id, new EventJoinRequest(
                    JoinEventNicknameTextBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(JoinEventCodeTextBox.Text) ? null : JoinEventCodeTextBox.Text.Trim()));
            }
            await _eventCoordinator.ActivateAsync(
                membership.Event,
                membership.Membership,
                membership.OfflineLease,
                membership.OfflineUntilUtc,
                _activePresetId);
            JoinEventCodeTextBox.Text = string.Empty;
            StatusText.Text = $"Joined '{membership.Event.Name}' as {membership.Membership.Nickname}.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Event could not be joined: {exception.Message}";
        }
    }

    private async void OnRefreshActiveEventClick(object sender, RoutedEventArgs e) =>
        await RefreshActiveServerEventAsync(updateStatusOnError: true);

    private async Task RefreshActiveServerEventAsync(bool updateStatusOnError)
    {
        try
        {
            await _eventCoordinator.RefreshAsync();
            await RefreshActiveServerEventStatisticsAsync(updateStatusOnError);
        }
        catch (Exception exception)
        {
            if (updateStatusOnError)
            {
                StatusText.Text = $"Event status could not be refreshed: {exception.Message}";
            }
        }
    }

    private async Task RefreshActiveServerEventStatisticsAsync(bool updateStatusOnError)
    {
        var current = _eventCoordinator.Current;
        if (current is null)
        {
            return;
        }
        try
        {
            var stats = await _serverClient.GetEventStatisticsAsync(current.Event.Id);
            ServerEventSalesText.Text = stats.SaleCount.ToString(CultureInfo.CurrentCulture);
            ServerEventSubtotalText.Text = CurrencyFormatter.FormatCents(stats.SubtotalCents);
            ServerEventTipsText.Text = CurrencyFormatter.FormatCents(stats.TipCents);
            ServerEventTotalText.Text = CurrencyFormatter.FormatCents(stats.TotalCents);
        }
        catch (Exception exception)
        {
            if (updateStatusOnError)
            {
                StatusText.Text = $"Event statistics could not be loaded: {exception.Message}";
            }
        }
    }

    private async void OnLeaveServerEventClick(object sender, RoutedEventArgs e)
    {
        var current = _eventCoordinator.Current;
        if (current is null)
        {
            return;
        }
        try
        {
            if (current.Event.State is CashSlothEventState.Ended or CashSlothEventState.Cancelled ||
                current.Membership.Status is CashSlothEventMemberStatus.Left or CashSlothEventMemberStatus.Kicked)
            {
                await ExitServerEventLocallyAsync(current.PreviousLocalPresetId);
            }
            else
            {
                var previousPreset = current.PreviousLocalPresetId;
                await _eventCoordinator.LeaveAsync();
                RestoreLocalPreset(previousPreset);
            }
            await RefreshServerEventsAsync(updateStatusOnError: false);
            StatusText.Text = "Event mode ended on this register.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Event could not be left: {exception.Message}";
        }
    }

    private async void OnRenameEventMemberClick(object sender, RoutedEventArgs e)
    {
        if (ServerEventMembersListBox.SelectedItem is not ServerEventMemberListItem selected)
        {
            StatusText.Text = "Select an event member first.";
            return;
        }
        try
        {
            await _eventCoordinator.RenameMemberAsync(selected.Member.Id, RenameEventMemberTextBox.Text.Trim());
            RenameEventMemberTextBox.Text = string.Empty;
            StatusText.Text = "Event nickname updated.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Nickname could not be changed: {exception.Message}";
        }
    }

    private async void OnKickEventMemberClick(object sender, RoutedEventArgs e)
    {
        if (ServerEventMembersListBox.SelectedItem is not ServerEventMemberListItem selected)
        {
            StatusText.Text = "Select an event member first.";
            return;
        }
        if (MessageBox.Show(this, $"Permanently kick '{selected.Member.Nickname}' from this event?", "Kick event member",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        try
        {
            await _eventCoordinator.KickMemberAsync(selected.Member.Id);
            StatusText.Text = $"'{selected.Member.Nickname}' was kicked from the event.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Event member could not be kicked: {exception.Message}";
        }
    }

    private async void OnCloseServerEventClick(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this,
                "Stop new checkouts and begin the final synchronisation phase? The event cannot return to Active afterwards.",
                "Begin event closing", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }
        try
        {
            var response = await _eventCoordinator.CloseAsync();
            StatusText.Text = response.UnsynchronisedNicknames.Length == 0
                ? "Event is closing and all registers are synchronised."
                : $"Event is closing. Waiting for: {string.Join(", ", response.UnsynchronisedNicknames)}.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Event closing could not begin: {exception.Message}";
        }
    }

    private async void OnFinalizeServerEventClick(object sender, RoutedEventArgs e)
    {
        var current = _eventCoordinator.Current;
        if (current is null)
        {
            return;
        }
        try
        {
            EventFinalReportResponse report;
            try
            {
                report = await _eventCoordinator.FinalizeAsync(confirmIncomplete: false);
            }
            catch (CashSlothServerException exception) when (exception.StatusCode == 409)
            {
                if (MessageBox.Show(this,
                        $"{exception.Message}\n\nFinalize anyway and mark the report incomplete?",
                        "Unsynchronised registers", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                {
                    return;
                }
                report = await _eventCoordinator.FinalizeAsync(confirmIncomplete: true);
            }

            var exportMessage = "";
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose a folder for the final CashSloth event report",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            })
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var sales = await _serverClient.GetEventSalesAsync(current.Event.Id);
                    var folder = HistoryExportService.ExportEventReport(dialog.SelectedPath, report, sales);
                    exportMessage = $"\n\nReport exported to:\n{folder}";
                }
            }

            await ExitServerEventLocallyAsync(current.PreviousLocalPresetId);
            MessageBox.Show(this,
                $"Event ended with {report.Statistics.SaleCount} sales and {CurrencyFormatter.FormatCents(report.Statistics.TotalCents)} total.{exportMessage}",
                "Event complete", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = report.IsComplete ? "Event finalised successfully." : "Event finalised with missing register synchronisation.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Event could not be finalised: {exception.Message}";
        }
    }

    private async Task ExitServerEventLocallyAsync(string? previousPresetId)
    {
        await _eventCoordinator.ClearAsync();
        RestoreLocalPreset(previousPresetId);
    }

    private void ApplyServerEventPreset(EventDetailResponse detail)
    {
        if (detail.Preset is not { } preset)
        {
            return;
        }
        var items = preset.Items.Select(value => new CatalogItemEditor(value.Id, value.Name, value.UnitCents, value.Category)).ToArray();
        ApplyPresetCatalog(items, preset.Categories, $"Event preset '{preset.Name}' loaded read-only.");
    }

    private void RestoreLocalPreset(string? previousPresetId)
    {
        var target = string.IsNullOrWhiteSpace(previousPresetId) ? _activePresetId : previousPresetId;
        if (_assortmentStore.TryLoadPreset(target, out var catalog, out var categories, out _))
        {
            _activePresetId = target;
            ApplyPresetCatalog(catalog, categories, $"Local preset restored after event mode.");
            RefreshPresetControls(target);
            return;
        }
        if (_assortmentStore.TryLoad(out catalog, out categories, out _))
        {
            ApplyPresetCatalog(catalog, categories, "Local assortment restored after event mode.");
        }
    }

    private void SetCustomerDisplayEventIdentity(CashSlothLocalEventSession? session)
    {
        _customerDisplayWindow?.SetEventRegister(session?.Event.Name, session?.Membership.Nickname);
    }

    private sealed record ServerPresetChoice(string Id, string Name, long Version, int ItemCount)
    {
        public string Label => $"{Name} · v{Version} · {ItemCount} items";
    }

    private sealed record ServerEventListItem(EventSummaryResponse Event)
    {
        public string DisplayLabel =>
            $"{(Event.JoinMode == CashSlothEventJoinMode.Code ? "🔒 " : string.Empty)}{Event.Name} · {Event.State} · Host {Event.HostUsername} · {Event.ActiveMemberCount} online";
    }

    private sealed record ServerEventMemberListItem(EventMemberResponse Member)
    {
        public string DisplayLabel =>
            $"{Member.Nickname} · {Member.Role} · {Member.Status}{(Member.IsOnline ? " · online" : string.Empty)}{(Member.PendingSaleCount > 0 ? $" · {Member.PendingSaleCount} pending" : string.Empty)}";
    }
}
