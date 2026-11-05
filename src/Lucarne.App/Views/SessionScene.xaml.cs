using System.Windows;
using Lucarne.App.Rdp;
using Lucarne.App.ViewModels;

namespace Lucarne.App.Views;

/// <summary>
/// The session area's own face while the remote desktop has nothing to show. See the markup for
/// what it draws; this half decides when it is on screen and relays its two buttons.
/// </summary>
/// <remarks>
/// Shown exactly when <see cref="SessionsViewModel.ShowsDesktop"/> says the host is hidden, so
/// the scene and the host are never both on screen and never both absent. The windows call
/// <see cref="Show"/> from the same places they refresh their bars — the active tab changing, the
/// tab's own state changing, a close starting — and nothing else moves it.
/// </remarks>
public partial class SessionScene : System.Windows.Controls.UserControl
{
    public SessionScene()
    {
        InitializeComponent();
    }

    /// <summary>Raised by <em>Reconnect</em>. The window runs its own reconnection, VPN gate
    /// included; the scene only asks.</summary>
    internal event Action? ReconnectRequested;

    /// <summary>Raised by <em>Cancel</em> while a retry is pending.</summary>
    internal event Action? CancelRequested;

    /// <summary>
    /// Draws <paramref name="tab"/>, or nothing for <c>null</c>. <paramref name="live"/> is false
    /// while the window is closing the session: the buttons go, the state stays readable.
    /// </summary>
    internal void Show(SessionTabViewModel? tab, bool live)
    {
        DataContext = tab;

        if (tab is null || SessionsViewModel.ShowsDesktop(tab.State))
        {
            Visibility = Visibility.Collapsed;
            return;
        }

        ReconnectButton.Visibility = live && tab.State is SessionState.Failed or SessionState.Idle
            ? Visibility.Visible
            : Visibility.Collapsed;
        CancelRetryButton.Visibility = live && tab.CanCancelRetry
            ? Visibility.Visible
            : Visibility.Collapsed;
        Visibility = Visibility.Visible;
    }

    private void OnReconnectClick(object sender, RoutedEventArgs e) => ReconnectRequested?.Invoke();

    private void OnCancelRetryClick(object sender, RoutedEventArgs e) => CancelRequested?.Invoke();
}
