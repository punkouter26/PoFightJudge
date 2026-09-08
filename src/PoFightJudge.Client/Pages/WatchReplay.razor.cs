using Microsoft.AspNetCore.Components;
using PoFightJudge.Client.Services;
using PoFightJudge.Shared.Identifiers;
using PoFightJudge.Shared.Models;

namespace PoFightJudge.Client.Pages;

/// <summary>
/// A watch, read back. Every round was spoken once and the audio kept, so this plays the stored bytes rather than
/// asking for the line to be synthesized again: a replay costs nothing and sounds exactly like the night it happened.
/// </summary>
public sealed partial class WatchReplay : ComponentBase, IDisposable
{
    private readonly CancellationTokenSource _leaving = new();
    private readonly List<TurnDto> _turns = [];
    private MatchDto? _match;
    private bool _loading = true;
    private bool _playingAll;
    private int _speaking = -1;
    private string? _error;

    [Parameter] public string MatchIdText { get; set; } = string.Empty;

    [Inject] private IApiClient Api { get; set; } = default!;

    [Inject] private AudioInterop Audio { get; set; } = default!;

    [Inject] private NavigationManager Nav { get; set; } = default!;

    private string Title => _match?.Topic is { Length: > 0 } topic ? topic : "Replay";

    private string Lead => _match is null
        ? "Looking it up."
        : $"{_match.StartedAt.LocalDateTime:d MMM, HH:mm} — every line as it was said, in the voices that said it.";

    public void Dispose()
    {
        _leaving.Cancel();
        _leaving.Dispose();
    }

    protected override async Task OnParametersSetAsync()
    {
        if (!MatchId.TryParse(MatchIdText, null, out var id))
        {
            _loading = false;
            return;
        }

        _loading = true;
        try
        {
            _match = await Api.GetMatchAsync(id, _leaving.Token);
            _turns.Clear();
            if (_match is not null)
            {
                // Only the lines that were argued: a judge's summing-up is on the match, not in the transcript.
                _turns.AddRange((await Api.GetMatchTurnsAsync(id, _leaving.Token)).Where(t => t.Kind == TurnKind.Round));
            }
        }
        catch (OperationCanceledException)
        {
            // Left the page mid-load.
        }
        catch (HttpRequestException)
        {
            _error = "The replay could not be loaded. Try again in a moment.";
        }
        finally
        {
            _loading = false;
        }
    }

    private string NameOf(Speaker speaker) =>
        _match is null ? speaker.ToString() : speaker == Speaker.Player1 ? _match.Side1.DisplayName : _match.Side2.DisplayName;

    /// <summary>One line on its own. Anything already playing is replaced rather than layered over.</summary>
    private async Task PlayOneAsync(int index)
    {
        _playingAll = false;
        await SpeakAsync(index);
    }

    /// <summary>
    /// The whole argument, in order, each line waiting for the one before it to finish. The flag is checked between
    /// lines so pressing Stop takes effect at the next gap rather than never.
    /// </summary>
    private async Task PlayAllAsync()
    {
        if (_playingAll)
        {
            _playingAll = false;
            await Audio.StopAsync(_leaving.Token);
            _speaking = -1;
            return;
        }

        _playingAll = true;
        foreach (var turn in _turns.Where(t => t.AudioBlobName is not null))
        {
            if (!_playingAll)
            {
                break;
            }

            await SpeakAsync(turn.Index);
        }

        _playingAll = false;
        _speaking = -1;
    }

    private async Task SpeakAsync(int index)
    {
        if (_match is null)
        {
            return;
        }

        try
        {
            var audio = await Api.GetRoundAudioAsync(_match.Id, index, _leaving.Token);
            if (audio is null)
            {
                return;
            }

            _speaking = index;
            StateHasChanged();

            var length = await Audio.PlayAsync(audio, _leaving.Token);
            await Task.Delay(length, _leaving.Token);
        }
        catch (OperationCanceledException)
        {
            // Left the page, or Stop was pressed.
        }
        catch (HttpRequestException)
        {
            _error = "That line could not be played back.";
        }
        finally
        {
            _speaking = -1;
        }
    }
}
