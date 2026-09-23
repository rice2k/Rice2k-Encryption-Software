using System.Collections.Specialized;
using System.Windows.Controls;
using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private readonly PrivacyHistoryService _privacyHistoryService = new();
    private bool _privacyHistoryInitialized;

    private void InitializePrivacyHistoryUi()
    {
        if (_privacyHistoryInitialized)
            return;

        _privacyHistoryInitialized = true;
        ((INotifyCollectionChanged)ActivityList.Items).CollectionChanged += ActivityItems_CollectionChanged;
        EncryptSourceBox.TextChanged += RecentEncryptSource_Changed;
        DecryptSourceBox.TextChanged += RecentDecryptSource_Changed;

        Closed += (_, _) =>
        {
            ((INotifyCollectionChanged)ActivityList.Items).CollectionChanged -= ActivityItems_CollectionChanged;
            EncryptSourceBox.TextChanged -= RecentEncryptSource_Changed;
            DecryptSourceBox.TextChanged -= RecentDecryptSource_Changed;
        };
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
