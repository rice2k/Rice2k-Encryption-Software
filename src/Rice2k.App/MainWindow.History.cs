using System.Collections.Specialized;
using System.Windows.Controls;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private readonly PrivacyHistoryService _privacyHistoryService = new();
    private INotifyCollectionChanged? _activityItemsNotifier;
    private bool _privacyHistoryInitialized;

    private void InitializePrivacyHistoryUi()
    {
        if (_privacyHistoryInitialized)
            return;

        _privacyHistoryInitialized = true;
        UpdateActivityPagePrivacyDescription();
        _activityItemsNotifier = ActivityList.Items as INotifyCollectionChanged;
        if (_activityItemsNotifier is not null)
            _activityItemsNotifier.CollectionChanged += ActivityItems_CollectionChanged;

        EncryptSourceBox.TextChanged += RecentEncryptSource_Changed;
        DecryptSourceBox.TextChanged += RecentDecryptSource_Changed;

        Closed += (_, _) =>
        {
            if (_activityItemsNotifier is not null)
                _activityItemsNotifier.CollectionChanged -= ActivityItems_CollectionChanged;
            EncryptSourceBox.TextChanged -= RecentEncryptSource_Changed;
            DecryptSourceBox.TextChanged -= RecentDecryptSource_Changed;
        };
    }

    private void UpdateActivityPagePrivacyDescription()
    {
        if (ActivityPage.Content is not StackPanel root)
            return;

        var textBlocks = root.Children.OfType<TextBlock>().ToArray();
        if (textBlocks.Length < 2)
            return;

        textBlocks[1].Text =
            "Session activity stays in memory by default. Optional persistent activity is off by default and, when explicitly enabled in Settings, stores only redacted action names and timestamps—not passwords, plaintext, secret keys, or file/path details.";
    }

    private void ActivityItems_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null)
            return;

        var settings = _appSettingsService.Load();
        if (!settings.PersistentActivityLogEnabled || settings.PrivacyModeEnabled)
            return;

        foreach (var item in e.NewItems.OfType<string>())
            _privacyHistoryService.TryAppendRedactedActivity(item);
    }

    private void RecentEncryptSource_Changed(object sender, TextChangedEventArgs e) =>
        TryRememberRecentFile(EncryptSourceBox.Text, "Encrypt source");

    private void RecentDecryptSource_Changed(object sender, TextChangedEventArgs e) =>
        TryRememberRecentFile(DecryptSourceBox.Text, "Decrypt source");

    private void TryRememberRecentFile(string path, string purpose)
    {
        var settings = _appSettingsService.Load();
        if (!settings.RememberRecentFiles || settings.PrivacyModeEnabled || !File.Exists(path))
            return;

        _privacyHistoryService.TryRememberRecentFile(path, purpose);
    }

    private void ShowStoredPrivacyHistory()
    {
        var window = new PrivacyHistoryWindow { Owner = this };
        window.ShowDialog();
    }
}
