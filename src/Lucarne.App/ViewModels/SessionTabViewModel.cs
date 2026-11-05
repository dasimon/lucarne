using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lucarne.App.Rdp;
using Lucarne.App.Resources;
using Lucarne.Core.Diagnostics;

namespace Lucarne.App.ViewModels;

/// <summary>
/// One tab: everything the strip and the session bar need to draw a single
/// <see cref="RdpSession"/>, and nothing else.
///
/// The view-model owns no UI and performs no action of its own: it observes
/// <see cref="RdpSession.Changed"/> and republishes it as bindable properties, and
/// <see cref="CloseCommand"/> merely raises <see cref="CloseRequested"/> so that
/// <see cref="SessionsViewModel"/> — the only thing that knows about the visual tree — runs the
/// close protocol.
/// </summary>
/// <remarks>
/// Internal because it exposes <see cref="Session"/>, and <see cref="RdpSession"/> is internal
/// (it owns a <c>WindowsFormsHost</c> the shell has to place). WPF binds to the public properties
/// of an internal type without complaint — verified on .NET 10 before this class was written — so
/// the accessibility costs the XAML nothing.
/// </remarks>
internal sealed partial class SessionTabViewModel : ObservableObject, IDisposable
{
    /// <summary>Status-dot resource keys. Declared here rather than in the XAML so that the
    /// mapping state → colour lives in one place; <c>SessionTabStrip</c> turns the key into a
    /// <c>DynamicResource</c> through a <c>DataTrigger</c>, which keeps the dot theme-aware.</summary>
    public const string ConnectedBrushKey = "SystemFillColorSuccessBrush";
    public const string RetryingBrushKey = "SystemFillColorCautionBrush";
    public const string FailedBrushKey = "SystemFillColorCriticalBrush";
    public const string NeutralBrushKey = "TextFillColorTertiaryBrush";

    private readonly RdpSession _session;
    private bool _disposed;

    public SessionTabViewModel(RdpSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        Title = session.Connection.Name;
        Subtitle = $"{session.Connection.Host}:{session.Connection.Port}";
        GroupName = session.Connection.GroupName;
        CloseCommand = new RelayCommand(() => CloseRequested?.Invoke(this));

        _session.Changed += OnSessionChanged;
        Refresh();
    }

    /// <summary>The session behind the tab. Internal on purpose: the shell and
    /// <see cref="SessionsViewModel"/> drive it, the XAML never sees it.</summary>
    internal RdpSession Session => _session;

    /// <summary>Connection name — the tab caption.</summary>
    public string Title { get; }

    /// <summary>Host and port, shown in the session bar and as the tab's tooltip.</summary>
    public string Subtitle { get; }

    /// <summary>The connection's group, which colours the square before the caption.</summary>
    public string GroupName { get; }

    /// <summary>The two letters on the session scene's badge: the same ones the connection's row
    /// carries, so the scene and the row are recognisably the same machine.</summary>
    public string Monogram => Lucarne.Core.Model.Monogram.Of(Title);

    /// <summary>Mirror of <see cref="RdpSession.State"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReconnect))]
    [NotifyPropertyChangedFor(nameof(CanCancelRetry))]
    [NotifyPropertyChangedFor(nameof(CanFullScreen))]
    private SessionState _state;

    /// <summary>Which theme brush the 8 px status dot must use; one of the four keys above.</summary>
    [ObservableProperty] private string _statusBrushKey = NeutralBrushKey;

    /// <summary>« retry in 7 s » while a reconnection is counting down, empty otherwise.</summary>
    [ObservableProperty] private string _countdownText = "";

    /// <summary>One short line describing the state, for the session bar.</summary>
    [ObservableProperty] private string _stateText = "";

    /// <summary>
    /// Windows' own wording of the failure under <see cref="StateText"/>, for the session scene:
    /// the one text that names the actual cause. Empty while the session is up or connecting, and
    /// for a disconnect that is not an error — <c>GetErrorDescription()</c> answers "an internal
    /// error has occurred" for an ordinary log-off, which would alarm for nothing. The same rule
    /// <see cref="Controls.SessionStatusPresenter"/> applies to the InfoBar, so the scene and the
    /// bar never disagree about whether there is a detail to show.
    /// </summary>
    [ObservableProperty] private string _detailText = "";

    /// <summary>True for the tab whose session is visible. Drives the strip's active styling.</summary>
    [ObservableProperty] private bool _isActive;

    /// <summary>
    /// True while the session is shown by a <c>SessionWindow</c> of its own rather than by the
    /// shell's container. The tab stays in <c>SessionsViewModel.Tabs</c> — the palette, the session
    /// count and the close-all pass all keep working precisely because it does — and the strip hides
    /// it by binding <c>Visibility</c> to this flag.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDetach))]
    [NotifyPropertyChangedFor(nameof(CanFullScreen))]
    private bool _isDetached;

    /// <summary>
    /// True while a reconnection someone asked for is still at the VPN gate — the question, or a
    /// dial that can take several seconds. The session is still <see cref="SessionState.Failed"/> or
    /// <see cref="SessionState.Idle"/> all that time, so without this a second double-click in the
    /// connection list, or a second press of <em>Reconnect</em>, would dial the same tunnel twice
    /// and then start a second attempt on a control already connecting. Set and cleared by whichever
    /// window runs the reconnection; on the tab because both the shell and a detached window can.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanReconnect))]
    private bool _isReconnectPending;

    /// <summary>
    /// What the tab's own menu may offer, and when a <em>Connect</em> on a connection that already
    /// has this tab means reconnecting it. The toolbar applies the same state rule to the active
    /// tab; it is said once here so a menu aimed at a background tab cannot drift from the buttons.
    /// </summary>
    public bool CanReconnect => !IsReconnectPending && State is SessionState.Failed or SessionState.Idle;

    /// <summary>A retry is pending or running, and can still be called off. Exactly the states where
    /// the session bar offers <em>Cancel</em>, said once so the scene cannot drift from the bar.</summary>
    public bool CanCancelRetry => State is SessionState.Interrupted or SessionState.Reconnecting;

    /// <summary>A session already in a window of its own has nowhere to be detached to.</summary>
    public bool CanDetach => !IsDetached;

    /// <summary>
    /// Full screen means detaching first, and <c>SetFullScreen</c> refuses any session that is not
    /// connected — so the entry is live only where it would actually do something.
    /// </summary>
    public bool CanFullScreen => !IsDetached && State == SessionState.Connected;

    /// <summary>Closes the tab. Raises <see cref="CloseRequested"/>; the actual protocol is
    /// <see cref="SessionsViewModel.CloseAsync"/>'s.</summary>
    public IRelayCommand CloseCommand { get; }

    /// <summary>Raised by <see cref="CloseCommand"/> and by the strip's middle-click.</summary>
    public event Action<SessionTabViewModel>? CloseRequested;

    /// <summary>Raised whenever the underlying session changed anything — state, attempt count or
    /// countdown. <see cref="SessionsViewModel"/> forwards it so the shell can refresh the bar.</summary>
    public event Action<SessionTabViewModel>? Changed;

    private void OnSessionChanged()
    {
        Refresh();
        Changed?.Invoke(this);
    }

    /// <summary>Re-reads everything from the session. Cheap and idempotent: the properties are
    /// <c>ObservableProperty</c>-generated, so assigning an unchanged value raises nothing.</summary>
    private void Refresh()
    {
        State = _session.State;
        StatusBrushKey = BrushKeyFor(_session.State);
        CountdownText = _session.NextRetryIn is { } remaining
            ? Text.Of(Strings.Session_Countdown, remaining.TotalSeconds.ToString("F0", CultureInfo.CurrentCulture))
            : "";
        StateText = DescribeState();
        DetailText = DescribeDetail();
    }

    // No disconnect description at all — an attempt that never reached the wire — still has a
    // message worth reading: the exception's. Only an ordinary end withholds it.
    private string DescribeDetail() =>
        _session.State is SessionState.Idle or SessionState.Interrupted or SessionState.Failed
        && _session.LastDisconnect is not { Category: DisconnectCategory.NotAnError }
            ? _session.LastWindowsDescription ?? ""
            : "";

    private static string BrushKeyFor(SessionState state) => state switch
    {
        SessionState.Connected => ConnectedBrushKey,
        SessionState.Interrupted or SessionState.Reconnecting => RetryingBrushKey,
        SessionState.Failed => FailedBrushKey,
        _ => NeutralBrushKey,
    };

    private string DescribeState() => _session.State switch
    {
        SessionState.Idle => _session.LastDisconnect is { IsError: false } ended
            ? Text.Of(Strings.Session_StateDisconnected, ended.Title)
            : Strings.Session_StateNotConnected,
        SessionState.Connecting => Strings.Session_StateConnecting,
        SessionState.Connected => Strings.Session_StateConnected,
        SessionState.Interrupted => CountdownText.Length == 0
            ? Text.Of(Strings.Session_StateInterrupted, _session.Attempt)
            : Text.Of(Strings.Session_StateInterruptedCountdown, CountdownText, _session.Attempt),
        SessionState.Reconnecting => Text.Of(Strings.Session_StateReconnecting, _session.Attempt),
        SessionState.Failed => _session.LastDisconnect is { } failure
            ? Text.Of(Strings.Session_StateFailedReason, failure.Title)
            : Strings.Session_StateFailed,
        SessionState.Closing => Strings.Session_StateClosing,
        SessionState.Closed => Strings.Session_StateClosed,
        _ => _session.State.ToString(),
    };

    /// <summary>The disconnect family of the last drop, or <c>null</c> if the session never dropped.
    /// The shell picks the InfoBar severity from it.</summary>
    internal DisconnectCategory? LastCategory => _session.LastDisconnect?.Category;

    /// <summary>Stops observing the session. The session itself is disposed by its own
    /// <see cref="RdpSession.CloseAsync"/>, not here.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _session.Changed -= OnSessionChanged;
    }
}
