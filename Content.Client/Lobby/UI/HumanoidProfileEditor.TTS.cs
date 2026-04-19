// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 Zekins <zekins3366@gmail.com>
// SPDX-FileCopyrightText: 2025 pheenty <fedorlukin2006@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Shared._AltHub.TTS;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    private readonly List<TTSVoicePrototype?> _ttsVoicePrototypes = [];

    private void InitializeTTSVoice()
    {
        TTSVoiceButton.OnItemSelected += args =>
        {
            TTSVoiceButton.SelectId(args.Id);
            SetTTSVoice(_ttsVoicePrototypes[args.Id]?.ID);
            PlayPreviewTTS();
        };

        TTSVoicePlayButton.OnPressed += _ => PlayPreviewTTS();
    }

    private void UpdateTTSVoice()
    {
        if (Profile is null)
            return;

        _ttsVoicePrototypes.Clear();
        _ttsVoicePrototypes.Add(null);
        _ttsVoicePrototypes.AddRange(_prototypeManager
            .EnumeratePrototypes<TTSVoicePrototype>()
            .Where(o => o.RoundStart &&
                        (o.SpeciesWhitelist is null ||
                         o.SpeciesWhitelist.Contains(Profile.Species)))
            .OrderBy(LocalizeTTSVoiceName)
            .Cast<TTSVoicePrototype?>());

        TTSVoiceButton.Clear();
        TTSVoiceButton.AddItem(Loc.GetString("humanoid-profile-editor-voice-none"), 0);

        var selectedVoiceId = 0;
        for (var i = 1; i < _ttsVoicePrototypes.Count; i++)
        {
            var voice = _ttsVoicePrototypes[i]!;
            if (voice.ID == Profile.TTSVoice)
                selectedVoiceId = i;

            TTSVoiceButton.AddItem(LocalizeTTSVoiceName(voice), i);
        }

        TTSVoiceButton.SelectId(selectedVoiceId);
        SetTTSVoice(_ttsVoicePrototypes[selectedVoiceId]?.ID);
    }

    private void PlayPreviewTTS()
    {
        if (Profile?.TTSVoice == null ||
            !_prototypeManager.TryIndex<TTSVoicePrototype>(Profile.TTSVoice, out var voice))
            return;

        if (!_resManager.ContentFileExists(voice.PreviewPath))
            return;

        _entManager.System<AudioSystem>()
            .PlayGlobal(new SoundPathSpecifier(voice.PreviewPath), Filter.Local(), false);
    }

    private string LocalizeTTSVoiceName(TTSVoicePrototype voice)
    {
        return Loc.TryGetString(voice.Name, out var localized)
            ? localized
            : FormattedMessage.RemoveMarkupPermissive(voice.Name);
    }
}
