using System.Reflection;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private void InitializeVersionStatus()
    {
        var informational = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        var version = string.IsNullOrWhiteSpace(informational)
            ? typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "development"
            : informational.Split('+', 2)[0];

        GlobalStatusText.Text = $"● Ready   |   Offline/local   |   v{version}";
    }
}
