using System.Windows;
using System.Windows.Threading;
using Borevexa.Prescan.Core.Services;

namespace Borevexa.Prescan.App;

/// <summary>
/// Globale foutvangnetten (fase 5, 07-07-2026). Voorheen sloot een onafgevangen
/// fout de app geluidloos af; nu wordt elke fatale of onopgemerkte fout gelogd in
/// %LOCALAPPDATA%\Borevexa\PrescanNative\Logs\app.log en blijft de app waar
/// mogelijk gewoon doorwerken.
/// </summary>
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Warn($"ONAFGEVANGEN UI-FOUT: {e.Exception}");

        // Houd de app in leven: één mislukte actie mag geen werkverlies veroorzaken.
        e.Handled = true;
        MessageBox.Show(
            "Er ging iets mis bij de laatste actie. De app werkt gewoon door.\n\n" +
            $"Details: {e.Exception.GetType().Name}: {e.Exception.Message}\n\n" +
            "Het volledige foutrapport staat in Logs\\app.log.",
            "Borevexa Prescan - fout opgevangen",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        // Fataal (achtergrondthread); loggen is het enige dat nog kan.
        AppLog.Warn($"FATALE ACHTERGRONDFOUT (IsTerminating={e.IsTerminating}): {e.ExceptionObject}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        AppLog.Warn($"ONOPGEMERKTE TAAKFOUT: {e.Exception}");
        e.SetObserved();
    }
}
