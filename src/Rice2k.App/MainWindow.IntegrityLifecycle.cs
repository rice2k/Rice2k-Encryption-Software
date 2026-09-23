using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private bool _integrityLifecycleInitialized;
    private Button? _sha256Button;
    private Button? _sha512Button;

    private void InitializeIntegrityLifecycleUi()
    {
        if (_integrityLifecycleInitialized)
            return;

        _integrityLifecycleInitialized = true;
        _sha256Button = FindVisualChildren<Button>(IntegrityPage)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "SHA-256", StringComparison.Ordinal));
        _sha512Button = FindVisualChildren<Button>(IntegrityPage)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "SHA-512", StringComparison.Ordinal));

        if (_sha256Button is not null)
        {
            _sha256Button.Click -= ComputeSha256_Click;
            _sha256Button.Click += ComputeSha256Safe_Click;
            AutomationProperties.SetHelpText(_sha256Button, "Calculate SHA-256 with cancellation-aware Rice2k background processing.");
        }

        if (_sha512Button is not null)
        {
            _sha512Button.Click -= ComputeSha512_Click;
            _sha512Button.Click += ComputeSha512Safe_Click;
            AutomationProperties.SetHelpText(_sha512Button, "Calculate SHA-512 with cancellation-aware Rice2k background processing.");
        }
    }

    private async void ComputeSha256Safe_Click(object sender, RoutedEventArgs e) =>
        await ComputeIntegritySafeAsync(
            "SHA-256",
            (path, token) => _integrity.ComputeSha256Async(path, token));

    private async void ComputeSha512Safe_Click(object sender, RoutedEventArgs e) =>
        await ComputeIntegritySafeAsync(
            "SHA-512",
            (path, token) => _integrity.ComputeSha512Async(path, token));

    private async Task ComputeIntegritySafeAsync(
        string algorithm,
        Func<string, CancellationToken, Task<string>> compute)
    {
        if (_operationCts is not null)
        {
            ShowFriendlyError("Another Rice2k file operation is already running. Wait for it to finish or cancel it before calculating a checksum.");
            return;
        }

        var sourcePath = IntegrityFileBox.Text;
        if (!File.Exists(sourcePath))
        {
            ShowFriendlyError("Choose a file first.");
            return;
        }

        _operationCts = new CancellationTokenSource();
        SetIntegrityButtonsEnabled(false);
        IntegrityResultBox.Clear();
        GlobalStatusText.Text = $"● Calculating {algorithm}…   |   closing Rice2k will cancel safely";

        try
        {
            var result = await compute(sourcePath, _operationCts.Token);
            _operationCts.Token.ThrowIfCancellationRequested();
            IntegrityResultBox.Text = result;
            GlobalStatusText.Text = $"● Ready   |   {algorithm} calculation complete   |   v{GetApplicationVersion()}";
            AddActivity($"{algorithm} calculated", Path.GetFileName(sourcePath));
        }
        catch (OperationCanceledException)
        {
            IntegrityResultBox.Clear();
            GlobalStatusText.Text = $"● {algorithm} calculation cancelled safely";
        }
        catch (Exception ex)
        {
            IntegrityResultBox.Clear();
            ShowFriendlyError(ex.Message);
            GlobalStatusText.Text = $"● Ready   |   v{GetApplicationVersion()}";
        }
        finally
        {
            _operationCts?.Dispose();
            _operationCts = null;
            SetIntegrityButtonsEnabled(true);
        }
    }

    private void SetIntegrityButtonsEnabled(bool enabled)
    {
        if (_sha256Button is not null)
            _sha256Button.IsEnabled = enabled;
        if (_sha512Button is not null)
            _sha512Button.IsEnabled = enabled;
    }
}
