using System.Windows;
using System.Windows.Threading;

namespace PhantomMonitor.Overlay;

/// <summary>
/// Popup de alerta no canto inferior direito (não bloqueia a UI): aparece como toast
/// topmost, fecha sozinho em 6 segundos ou com um clique. Apenas uma instância por vez —
/// um novo alerta substitui o anterior.
/// </summary>
public partial class AlertPopupWindow : Window
{
    private static AlertPopupWindow? _current;

    private readonly DispatcherTimer _autoCloseTimer;

    private AlertPopupWindow(string title, string message)
    {
        InitializeComponent();

        TitleText.Text = title;
        MessageText.Text = message;

        _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        _autoCloseTimer.Tick += (_, _) => Close();

        Loaded += OnLoaded;
        MouseDown += (_, _) => Close();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 16;
        Top = area.Bottom - ActualHeight - 16;
        _autoCloseTimer.Start();
    }

    /// <summary>Exibe o alerta (substituindo o anterior, se houver).</summary>
    public static void ShowAlert(string title, string message)
    {
        try
        {
            _current?.Close();
            _current = new AlertPopupWindow(title, message);
            _current.Show();
        }
        catch (Exception ex)
        {
            Services.ErrorLog.Write("AlertPopup.ShowAlert", ex);
        }
    }
}
