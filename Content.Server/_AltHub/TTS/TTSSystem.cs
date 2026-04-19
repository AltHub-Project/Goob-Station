// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Goobstation.Common.CCVar;
using Content.Server.Chat.Systems;
using Content.Server.Communications;
using Content.Server.GameTicking.Events;
using Content.Server.Station.Systems;
using Content.Shared._AltHub.TTS;
using Content.Shared.Administration;
using Content.Shared.GameTicking;
using Content.Shared.Station.Components;
using Robust.Shared.Asynchronous;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._AltHub.TTS;

public sealed class TTSSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IHttpClientHolder _http = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ITaskManager _taskManager = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private TTSProviderClient _provider = default!;
    private ISawmill _sawmill = default!;

    private bool _enabled;
    private string _apiUrl = string.Empty;
    private string _apiToken = string.Empty;
    private float _rateLimitRps;
    private int _rateBurst;
    private int _maxConcurrency;
    private int _cacheMaxBytes;
    private int _cacheTtlSeconds;
    private float _speechMaxStalenessSeconds;
    private float _radioMaxStalenessSeconds;
    private float _requestTimeoutSeconds;
    private bool _debugLogging;

    private CancellationTokenSource _roundCts = new();
    private readonly List<CancellationTokenSource> _retiredRoundCts = [];
    private string? _roundAdminVoiceId;

    public string? RoundAdminVoiceId => _roundAdminVoiceId;

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = Logger.GetSawmill("althub.tts");

        Subs.CVar(_cfg, GoobCVars.AltHubTTSEnabled, value => _enabled = value, true);
        Subs.CVar(_cfg, GoobCVars.AltHubTTSApiUrl, value => _apiUrl = value, true);
        Subs.CVar(_cfg, GoobCVars.AltHubTTSApiToken, value => _apiToken = value, true);
        Subs.CVar(_cfg, GoobCVars.AltHubTTSRateLimitRps, value => _rateLimitRps = value, true);
        Subs.CVar(_cfg, GoobCVars.AltHubTTSRateBurst, value => _rateBurst = value, true);
        Subs.CVar(_cfg, GoobCVars.AltHubTTSMaxConcurrency, value => _maxConcurrency = value, true);
        Subs.CVar(_cfg, GoobCVars.AltHubTTSCacheMaxBytes, value => _cacheMaxBytes = value, true);
        Subs.CVar(_cfg, GoobCVars.AltHubTTSCacheTtlSeconds, value => _cacheTtlSeconds = value, true);
        Subs.CVar(_cfg, GoobCVars.AltHubTTSSpeechMaxStalenessSeconds, value => _speechMaxStalenessSeconds = value, true);
        Subs.CVar(_cfg, GoobCVars.AltHubTTSRadioMaxStalenessSeconds, value => _radioMaxStalenessSeconds = value, true);
        Subs.CVar(_cfg, GoobCVars.AltHubTTSRequestTimeoutSeconds, value => _requestTimeoutSeconds = value, true);
        Subs.CVar(_cfg, GoobCVars.AltHubTTSDebugLogging, value => _debugLogging = value, true);

        _provider = new TTSProviderClient(_http.Client, GetProviderOptions, _sawmill);

        SubscribeLocalEvent<EntitySpokeEvent>(OnEntitySpoke);
        SubscribeLocalEvent<RadioSpeechDeliveredEvent>(OnRadioSpeechDelivered);
        SubscribeLocalEvent<CommunicationConsoleAnnouncementEvent>(OnCommunicationAnnouncement);
        SubscribeLocalEvent<RoundStartingEvent>(OnRoundStarting);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _roundCts.Cancel();
        _roundCts.Dispose();
        foreach (var retiredCts in _retiredRoundCts)
        {
            retiredCts.Cancel();
            retiredCts.Dispose();
        }

        _retiredRoundCts.Clear();
        _provider.Dispose();
    }

    public void QueueAdminAnnouncement(
        string text,
        AdminAnnounceType announceType,
        bool useTts,
        string? requestedVoiceId)
    {
        if (!useTts || !_enabled)
            return;

        var voice = ResolveAdminAnnouncementVoice(requestedVoiceId);
        if (voice == null)
            return;

        var recipients = Filter.Broadcast().Recipients.ToArray();
        if (recipients.Length == 0)
            return;

        var delay = announceType == AdminAnnounceType.Station
            ? _audio.GetAudioLength(_audio.ResolveSound(new SoundPathSpecifier(ChatSystem.DefaultAnnouncementSound)))
            : TimeSpan.Zero;

        RunDetached(() => QueueAnnouncementAsync(
            recipients,
            text,
            voice.Speaker,
            delay,
            _roundCts.Token));
    }

    private void OnRoundStarting(RoundStartingEvent ev)
    {
        // AltHub Space -> start (TTS)
        var roundVoices = _prototype
            .EnumeratePrototypes<TTSVoicePrototype>()
            .Where(voice => voice.AdminAnnounceRoundPool)
            .ToArray();

        _roundAdminVoiceId = roundVoices.Length == 0
            ? null
            : _random.Pick(roundVoices).ID;
        // AltHub Space -> end (TTS)
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        var oldRoundCts = _roundCts;
        oldRoundCts.Cancel();
        _retiredRoundCts.Add(oldRoundCts);
        _roundCts = new CancellationTokenSource();

        _roundAdminVoiceId = null;
        _provider.Reset();
    }

    private void OnEntitySpoke(EntitySpokeEvent ev)
    {
        if (!_enabled ||
            ev.Recipients.Count == 0 ||
            !ev.Language.SpeechOverride.RequireSpeech)
        {
            return;
        }

        if (!TryResolveSpeakerVoice(ev.Source, out var voice))
            return;

        var recipients = ev.Recipients.Keys.ToArray();
        var source = ev.Source;
        var speaker = voice.Speaker;
        var message = ev.Message;
        var isWhisper = ev.IsWhisper;
        var staleness = TimeSpan.FromSeconds(Math.Max(0.1f, _speechMaxStalenessSeconds));
        var token = _roundCts.Token;

        RunDetached(async () =>
        {
            var data = await _provider.SynthesizeAsync(
                speaker,
                message,
                TTSRequestPriority.Speech,
                staleness,
                token);

            if (data == null || token.IsCancellationRequested)
                return;

            await RunOnMainThreadAsync(() =>
            {
                if (token.IsCancellationRequested || Deleted(source) || !Exists(source))
                    return;

                RaiseNetworkEvent(
                    new PlayTTSEvent(data, GetNetEntity(source), TTSPlaybackKind.Speech, isWhisper),
                    Filter.Empty().AddPlayers(recipients));
            });
        });
    }

    private void OnRadioSpeechDelivered(RadioSpeechDeliveredEvent ev)
    {
        if (!_enabled || ev.Recipients.Count == 0)
            return;

        if (!TryResolveSpeakerVoice(ev.Source, out var voice))
            return;

        var recipients = ev.Recipients.ToArray();
        var speaker = voice.Speaker;
        var message = ev.Message;
        var staleness = TimeSpan.FromSeconds(Math.Max(0.1f, _radioMaxStalenessSeconds));
        var token = _roundCts.Token;

        RunDetached(async () =>
        {
            var data = await _provider.SynthesizeAsync(
                speaker,
                message,
                TTSRequestPriority.Radio,
                staleness,
                token);

            if (data == null || token.IsCancellationRequested)
                return;

            await RunOnMainThreadAsync(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                RaiseNetworkEvent(
                    new PlayTTSEvent(data, null, TTSPlaybackKind.Radio),
                    Filter.Empty().AddPlayers(recipients));
            });
        });
    }

    private void OnCommunicationAnnouncement(ref CommunicationConsoleAnnouncementEvent ev)
    {
        if (!_enabled || ev.Sender is not { Valid: true } sender)
            return;

        if (!TryResolveSpeakerVoice(sender, out var voice))
            return;

        IReadOnlyList<ICommonSession> recipients;
        if (ev.Component.Global)
        {
            recipients = Filter.Broadcast().Recipients.ToArray();
        }
        else
        {
            var stationUid = _station.GetOwningStation(ev.Uid);
            if (stationUid == null || !TryComp<StationDataComponent>(stationUid, out var stationData))
                return;

            recipients = _station.GetInStation(stationData).Recipients.ToArray();
        }

        if (recipients.Count == 0)
            return;

        var delay = _audio.GetAudioLength(_audio.ResolveSound(ev.Component.Sound));
        var text = ev.Text;
        var speaker = voice.Speaker;
        var token = _roundCts.Token;
        RunDetached(() => QueueAnnouncementAsync(recipients, text, speaker, delay, token));
    }

    private async Task QueueAnnouncementAsync(
        IReadOnlyList<ICommonSession> recipients,
        string text,
        string speaker,
        TimeSpan delay,
        CancellationToken token)
    {
        var synthesisTask = _provider.SynthesizeAsync(
            speaker,
            text,
            TTSRequestPriority.Announcement,
            null,
            token);

        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, token);

        var data = await synthesisTask;
        if (data == null || token.IsCancellationRequested)
            return;

        await RunOnMainThreadAsync(() =>
        {
            if (token.IsCancellationRequested)
                return;

            RaiseNetworkEvent(
                new PlayTTSEvent(data, null, TTSPlaybackKind.Announcement),
                Filter.Empty().AddPlayers(recipients));
        });
    }

    private bool TryResolveSpeakerVoice(EntityUid source, out TTSVoicePrototype voice)
    {
        voice = default!;

        string? voiceId = CompOrNull<TTSComponent>(source)?.VoicePrototypeId;
        var transformEvent = new TransformSpeakerTTSVoiceEvent(source, voiceId);
        RaiseLocalEvent(source, transformEvent);

        var resolvedVoiceId = transformEvent.VoiceId;

        if (string.IsNullOrWhiteSpace(resolvedVoiceId))
            return false;

        if (!_prototype.TryIndex<TTSVoicePrototype>(resolvedVoiceId, out var resolvedVoice) ||
            resolvedVoice == null)
        {
            return false;
        }

        voice = resolvedVoice;
        return true;
    }

    private TTSVoicePrototype? ResolveAdminAnnouncementVoice(string? requestedVoiceId)
    {
        if (!string.IsNullOrWhiteSpace(requestedVoiceId) &&
            _prototype.TryIndex<TTSVoicePrototype>(requestedVoiceId, out var requested))
        {
            return requested;
        }

        if (!string.IsNullOrWhiteSpace(_roundAdminVoiceId) &&
            _prototype.TryIndex<TTSVoicePrototype>(_roundAdminVoiceId, out var roundDefault))
        {
            return roundDefault;
        }

        return null;
    }

    private TTSProviderOptions GetProviderOptions()
    {
        return new TTSProviderOptions(
            _enabled,
            _apiUrl,
            _apiToken,
            _rateLimitRps,
            _rateBurst,
            _maxConcurrency,
            _cacheMaxBytes,
            _cacheTtlSeconds,
            _requestTimeoutSeconds,
            _debugLogging);
    }

    private void RunDetached(Func<Task> taskFactory)
    {
        _ = Task.Run(taskFactory).ContinueWith(
            completed =>
            {
                if (completed.Exception != null)
                    _sawmill.Error($"Detached AltHub TTS task failed: {completed.Exception}");
            },
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private Task RunOnMainThreadAsync(Action action)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _taskManager.RunOnMainThread(() =>
        {
            try
            {
                action();
                tcs.TrySetResult();
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
            }
        });

        return tcs.Task;
    }
}
