using Rice2k.Encryption.Services;

namespace Rice2k.Encryption;

public partial class MainWindow
{
    private readonly AppSettingsService _appSettingsService = new();
    private bool _onboardingChecked;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);

        if (_onboardingChecked)
            return;

        _onboardingChecked = true;
        var settings = _appSettingsService.Load();
        if (settings.FirstRunTourCompleted)
            return;

        var tour = new WelcomeTourWindow(_appSettingsService)
        {
            Owner = this
        };
        tour.ShowDialog();
    }
}
