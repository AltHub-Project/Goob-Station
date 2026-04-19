// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using Content.Shared._AltHub.TTS;
using Content.Shared.Chat;
using Robust.Client.Audio;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.ContentPack;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Client._AltHub.TTS;

public sealed class TTSSystem : EntitySystem
{
    private const string CommunicationPresetId = "AltHubTTSCommunication";
    private const float WhisperVolume = -4f;
    private const int MaxActiveTTSPlaybacks = 12;

    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IResourceManager _resource = default!;

    private static readonly MemoryContentRoot ContentRoot = new();
    private static readonly ResPath Prefix = ResPath.Root / "Audio" / "_AltHub" / "TTS" / "Runtime";
    private static bool _contentRootAdded;

    private ISawmill _sawmill = default!;
    private int _fileIndex;
    private readonly LinkedList<ActivePlayback> _activePlaybacks = [];
    private readonly Dictionary<EntityUid, LinkedListNode<ActivePlayback>> _activePlaybackIndex = [];

    public override void Initialize()
    {
        base.Initialize();

        if (!_contentRootAdded)
        {
            _contentRootAdded = true;
            _resource.AddRoot(Prefix, ContentRoot);
        }

        _sawmill = Logger.GetSawmill("althub.tts.client");
        SubscribeNetworkEvent<PlayTTSEvent>(OnPlayTTS);
        SubscribeLocalEvent<AudioComponent, ComponentShutdown>(OnAudioShutdown);
    }

    private void OnPlayTTS(PlayTTSEvent ev)
    {
        if (ev.Data.Length == 0)
            return;

        if (!EnsurePlaybackCapacity(ev.Kind))
            return;

        var filePath = new ResPath($"{_fileIndex++}.ogg");
        ContentRoot.AddOrUpdateFile(filePath, ev.Data);

        try
        {
            var soundPath = Prefix / filePath;
            var resource = new AudioResource();
            resource.Load(IoCManager.Instance!, soundPath);

            var soundSpecifier = new ResolvedPathSpecifier(soundPath);
            var audioParams = BuildAudioParams(ev);

            (EntityUid Entity, AudioComponent Component)? playback = ev.Kind switch
            {
                TTSPlaybackKind.Speech => PlaySpeech(ev, resource, soundSpecifier, audioParams),
                TTSPlaybackKind.Radio => _audio.PlayGlobal(resource.AudioStream, soundSpecifier, audioParams),
                TTSPlaybackKind.Announcement => _audio.PlayGlobal(resource.AudioStream, soundSpecifier, audioParams),
                _ => null,
            };

            if (playback != null &&
                ev.ApplyCommunicationPreset &&
                _audio.Auxiliaries.ContainsKey(CommunicationPresetId))
            {
                _audio.SetEffect(playback.Value.Entity, playback.Value.Component, CommunicationPresetId);
            }

            if (playback != null)
                TrackPlayback(playback.Value.Entity, ev.Kind);
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to play AltHub TTS clip: {e}");
        }
        finally
        {
            ContentRoot.RemoveFile(filePath);
        }
    }

    private (EntityUid Entity, AudioComponent Component)? PlaySpeech(
        PlayTTSEvent ev,
        AudioResource resource,
        ResolvedPathSpecifier soundSpecifier,
        AudioParams audioParams)
    {
        if (ev.SourceUid == null || !TryGetEntity(ev.SourceUid.Value, out var source) || !Exists(source))
            return null;

        return _audio.PlayEntity(resource.AudioStream, source.Value, soundSpecifier, audioParams);
    }

    private static AudioParams BuildAudioParams(PlayTTSEvent ev)
    {
        var audioParams = AudioParams.Default;

        if (ev.Kind == TTSPlaybackKind.Speech)
        {
            audioParams = audioParams.WithMaxDistance(
                ev.IsWhisper
                    ? SharedChatSystem.WhisperMuffledRange
                    : SharedChatSystem.VoiceRange);

            if (ev.IsWhisper)
                audioParams = audioParams.WithVolume(WhisperVolume);
        }

        return audioParams;
    }

    private void OnAudioShutdown(EntityUid uid, AudioComponent component, ComponentShutdown args)
    {
        UntrackPlayback(uid);
    }

    private bool EnsurePlaybackCapacity(TTSPlaybackKind incomingKind)
    {
        PruneFinishedPlaybacks();

        while (_activePlaybacks.Count >= MaxActiveTTSPlaybacks)
        {
            var candidate = FindEvictionCandidate(incomingKind);
            if (candidate == null)
            {
                _sawmill.Warning($"Dropping AltHub TTS clip because the active playback budget of {MaxActiveTTSPlaybacks} was reached.");
                return false;
            }

            QueueDel(candidate.Value.Entity);
            UntrackPlayback(candidate.Value.Entity);
        }

        return true;
    }

    private LinkedListNode<ActivePlayback>? FindEvictionCandidate(TTSPlaybackKind incomingKind)
    {
        for (var node = _activePlaybacks.First; node != null; node = node.Next)
        {
            if (node.Value.Kind != TTSPlaybackKind.Announcement)
                return node;
        }

        return incomingKind == TTSPlaybackKind.Announcement
            ? _activePlaybacks.First
            : null;
    }

    private void TrackPlayback(EntityUid uid, TTSPlaybackKind kind)
    {
        UntrackPlayback(uid);

        var node = _activePlaybacks.AddLast(new ActivePlayback(uid, kind));
        _activePlaybackIndex[uid] = node;
    }

    private void UntrackPlayback(EntityUid uid)
    {
        if (!_activePlaybackIndex.Remove(uid, out var node))
            return;

        _activePlaybacks.Remove(node);
    }

    private void PruneFinishedPlaybacks()
    {
        for (var node = _activePlaybacks.First; node != null;)
        {
            var next = node.Next;
            if (Deleted(node.Value.Entity) || !Exists(node.Value.Entity))
                UntrackPlayback(node.Value.Entity);

            node = next;
        }
    }

    private readonly record struct ActivePlayback(EntityUid Entity, TTSPlaybackKind Kind);
}
