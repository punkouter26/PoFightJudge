using Microsoft.JSInterop;

namespace PoFightJudge.Client.Services;

/// <summary>
/// Which side of the phone breakpoint the window is on, for the decisions CSS cannot make.
///
/// Almost everything responsive here is a media query, and should stay one. This exists for the exception: a
/// <c>RadzenDataGrid</c> lays its columns out from their declared widths, so a column hidden with <c>display: none</c>
/// is still measured into the table and the grid still scrolls sideways. Dropping it means not declaring it, which
/// is a parameter, which is C#.
///
/// The query is 40rem — the same phone → large phone line the stylesheets use, written here in px because
/// matchMedia has no root font size to resolve rem against at the point it is asked.
/// </summary>
public sealed class Viewport(IJSRuntime js) : IDisposable
{
    /// <summary>40rem, the first of the design system's three breakpoints.</summary>
    public const string PhoneQuery = "(max-width: 640px)";

    private DotNetObjectReference<Viewport>? _self;
    private bool _watching;

    /// <summary>True on a phone-width window. False until <see cref="InitializeAsync"/> has run — the wide layout, which shows everything.</summary>
    public bool IsPhone { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Idempotent: the first call starts the listener, later ones do nothing.</summary>
    public async Task InitializeAsync()
    {
        if (_watching)
        {
            return;
        }

        _watching = true;
        _self = DotNetObjectReference.Create(this);
        try
        {
            IsPhone = await js.InvokeAsync<bool>("PoViewport.watch", PhoneQuery, _self);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            // Prerender, a browser with no matchMedia, or a test double. The wide layout is the safe answer.
            IsPhone = false;
        }
    }

    [JSInvokable]
    public void OnViewportChanged(bool matches)
    {
        if (matches == IsPhone)
        {
            return;
        }

        IsPhone = matches;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Synchronous on purpose. Disposing the reference is the whole of it: PoViewport drops a reference whose
    /// invoke throws, so a listener whose .NET side has gone unregisters itself on the next change. An async
    /// dispose would be tidier on the JS side and would also mean every bunit test that renders a page with a grid
    /// needs IAsyncLifetime, because bunit tears its container down synchronously.
    /// </summary>
    public void Dispose()
    {
        _self?.Dispose();
        _self = null;
    }
}
