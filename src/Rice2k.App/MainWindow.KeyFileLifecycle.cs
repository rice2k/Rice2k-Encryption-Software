namespace Rice2k.Encryption;

public partial class MainWindow
{
    private bool _keyFileResetHooksInitialized;

    private void InitializeKeyFileResetHooks()
    {
        if (_keyFileResetHooksInitialized)
            return;

        _keyFileResetHooksInitialized = true;
        EncryptSourceBox.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(EncryptSourceBox.Text))
                return;

            if (_encryptPasswordOnlyRadio is not null)
                _encryptPasswordOnlyRadio.IsChecked = true;
            _encryptKeyPathBox?.Clear();
            _encryptKeyPackagePasswordBox?.Clear();
            UpdateEncryptKeyModeUi();
        };

        DecryptSourceBox.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(DecryptSourceBox.Text))
                return;

            _decryptRequiresKeyFile = false;
            _decryptKeyPathBox?.Clear();
            _decryptKeyPackagePasswordBox?.Clear();
            DetectDecryptProtection();
        };
    }
}
