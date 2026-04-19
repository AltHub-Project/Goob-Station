// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

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

    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IResourceManager _resource = default!;

    private static readonly MemoryContentRoot ContentRoot = new();
    private static readonly ResPath Prefix = ResPath.Root / "Audio" / "_AltHub" / "TTS" / "Runtime";
    private static bool _contentRootAdded;

    private ISawmill _sawmill = default!;
    private int _fileIndex;

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
    }

    private void OnPlayTTS(PlayTTSEvent ev)
    {
        if (ev.Data.Length == 0)
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
                ev.Kind is TTSPlaybackKind.Radio or TTSPlaybackKind.Announcement &&
                _audio.Auxiliaries.ContainsKey(CommunicationPresetId))
            {
                _audio.SetEffect(playback.Value.Entity, playback.Value.Component, CommunicationPresetId);
            }
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
}
