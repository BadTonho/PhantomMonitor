using PhantomMonitor.App.Services;

namespace PhantomMonitor.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    // Fase 3 — disparado por App.ApplyTheme DEPOIS da troca do dicionário.
    // Invalida o cache de pincéis da VM e repinta o que está visível na hora,
    // sem depender do próximo tick (importante com a coleta parada/pausada).
    private void OnThemeChanged()
    {
        if (_disposed) return;
        DashboardBrushCache.Clear();
        if (_latestSensorData is not null)
            UpdateDashboardExtras(_latestSensorData);
        ApplyOverlayProfile(_activeProfile);
        UpdateOverlayRows(_latestSensorData);
    }
}
