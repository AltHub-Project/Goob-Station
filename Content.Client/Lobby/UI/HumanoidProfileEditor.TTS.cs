// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 Zekins <zekins3366@gmail.com>
// SPDX-FileCopyrightText: 2025 pheenty <fedorlukin2006@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;
using System.Linq;
using Content.Shared._AltHub.TTS;
using Robust.Client.Audio;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Enums;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Client.Lobby.UI;

public sealed partial class HumanoidProfileEditor
{
    // AltHub Space -> start (TTS)
    private readonly List<TTSVoicePrototype> _ttsVoicePrototypes = [];
    private readonly Dictionary<string, bool> _ttsVoiceCategoryStates = [];
    private Gender _ttsVoiceListGender = Gender.Male;

    private void InitializeTTSVoice()
    {
        TTSVoiceGenderButton.AddItem(Loc.GetString("humanoid-profile-editor-voice-gender-male"), 0);
        TTSVoiceGenderButton.SetItemMetadata(0, Gender.Male);
        TTSVoiceGenderButton.AddItem(Loc.GetString("humanoid-profile-editor-voice-gender-female"), 1);
        TTSVoiceGenderButton.SetItemMetadata(1, Gender.Female);
        TTSVoiceGenderButton.OnItemSelected += args =>
        {
            TTSVoiceGenderButton.SelectId(args.Id);
            _ttsVoiceListGender = (Gender) (TTSVoiceGenderButton.GetItemMetadata(args.Id) ?? Gender.Male);
            RebuildTTSVoiceCategories();
            UpdateTTSVoiceSummary();
        };

        TTSVoiceSearch.OnTextChanged += _ => RebuildTTSVoiceCategories();
        TTSVoiceSearchClearButton.OnPressed += _ => TTSVoiceSearch.Clear();
        TTSVoiceCurrentPreviewButton.OnPressed += _ => PlayPreviewTTS();
        TTSVoiceClearButton.OnPressed += _ =>
        {
            SetTTSVoice(null);
            UpdateTTSVoice();
        };
    }

    private void UpdateTTSVoice()
    {
        if (Profile is null)
            return;

        _ttsVoicePrototypes.Clear();
        _ttsVoicePrototypes.AddRange(_prototypeManager
            .EnumeratePrototypes<TTSVoicePrototype>()
            .Where(voice => voice.RoundStart &&
                            (voice.SpeciesWhitelist is null ||
                             voice.SpeciesWhitelist.Contains(Profile.Species))));
        _ttsVoicePrototypes.Sort(CompareTTSVoices);

        if (Profile.TTSVoice != null &&
            (!_prototypeManager.TryIndex<TTSVoicePrototype>(Profile.TTSVoice, out var selectedVoice) ||
             !TTSVoiceRequirements.IsSelectableForProfile(selectedVoice, Profile.Species, Profile.Gender)))
        {
            SetTTSVoice(null);
        }

        SyncTTSVoiceGenderFilter();
        UpdateTTSVoiceGenderControls();
        RebuildTTSVoiceCategories();
        UpdateTTSVoiceSummary();
    }

    private void SyncTTSVoiceGenderFilter()
    {
        if (Profile is null)
            return;

        if (TTSVoiceRequirements.TryGetForcedVoiceGender(Profile.Gender, out var forcedGender))
        {
            _ttsVoiceListGender = forcedGender;
        }
        else if (Profile.TTSVoice != null &&
                 _prototypeManager.TryIndex<TTSVoicePrototype>(Profile.TTSVoice, out var selectedVoice))
        {
            _ttsVoiceListGender = selectedVoice.Gender;
        }
        else if (_ttsVoiceListGender is not Gender.Male and not Gender.Female)
        {
            _ttsVoiceListGender = Gender.Male;
        }

        TTSVoiceGenderButton.SelectId(_ttsVoiceListGender == Gender.Female ? 1 : 0);
    }

    private void UpdateTTSVoiceGenderControls()
    {
        if (Profile is null)
            return;

        var hasFreeSelection = !TTSVoiceRequirements.TryGetForcedVoiceGender(Profile.Gender, out _);
        TTSVoiceGenderLabel.Visible = hasFreeSelection;
        TTSVoiceGenderButton.Visible = hasFreeSelection;
    }

    private void RebuildTTSVoiceCategories()
    {
        TTSVoiceCategories.DisposeAllChildren();

        if (Profile is null)
            return;

        var search = NormalizeTTSVoiceSearch(TTSVoiceSearch.Text);
        var filteredVoices = _ttsVoicePrototypes
            .Where(voice => MatchesTTSVoiceListGender(voice) && MatchesTTSVoiceSearch(voice, search))
            .ToList();

        TTSVoiceEmptyLabel.Visible = filteredVoices.Count == 0;
        TTSVoiceEmptyLabel.Text = Loc.GetString(filteredVoices.Count == 0 && _ttsVoicePrototypes.Count == 0
            ? "humanoid-profile-editor-voice-empty-unavailable"
            : "humanoid-profile-editor-voice-empty-filtered");
        if (filteredVoices.Count == 0)
            return;

        foreach (var group in filteredVoices.GroupBy(voice => voice.Source).OrderBy(group => group.Key))
        {
            var source = group.Key;
            var voices = group.ToList();
            voices.Sort(CompareTTSVoices);

            var heading = new CollapsibleHeading(Loc.GetString(
                "humanoid-profile-editor-voice-category-title",
                ("source", source),
                ("count", voices.Count)));
            heading.AddStyleClass(ContainerButton.StyleClassButton);
            heading.Label.HorizontalAlignment = HAlignment.Left;
            heading.Label.HorizontalExpand = true;

            var body = new CollapsibleBody
            {
                Margin = new Thickness(8, 4, 0, 0),
            };

            var content = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                SeparationOverride = 4,
            };
            body.AddChild(content);

            var collapsible = new Collapsible(heading, body)
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                HorizontalExpand = true,
                Margin = new Thickness(0, 0, 0, 6),
                BodyVisible = ShouldExpandTTSVoiceCategory(source, voices, search),
            };

            heading.OnToggled += args => _ttsVoiceCategoryStates[source] = args.Pressed;

            foreach (var voice in voices)
            {
                content.AddChild(BuildTTSVoiceEntry(voice));
            }

            TTSVoiceCategories.AddChild(collapsible);
        }
    }

    private bool MatchesTTSVoiceListGender(TTSVoicePrototype voice)
    {
        return _ttsVoiceListGender switch
        {
            Gender.Female => voice.Gender is Gender.Female or Gender.Epicene or Gender.Neuter,
            _ => voice.Gender is Gender.Male or Gender.Epicene or Gender.Neuter,
        };
    }

    private bool ShouldExpandTTSVoiceCategory(string source, IReadOnlyCollection<TTSVoicePrototype> voices, string search)
    {
        if (!string.IsNullOrEmpty(search))
            return true;

        if (_ttsVoiceCategoryStates.TryGetValue(source, out var visible))
            return visible;

        return voices.Any(voice => voice.ID == Profile?.TTSVoice);
    }

    private Control BuildTTSVoiceEntry(TTSVoicePrototype voice)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 8,
            Margin = new Thickness(2, 2, 2, 2),
        };

        var details = new RichTextLabel
        {
            HorizontalExpand = true,
        };
        details.SetMessage(BuildTTSVoiceEntryText(voice));

        var previewButton = new Button
        {
            Text = Loc.GetString("humanoid-profile-editor-voice-play"),
            MinWidth = 84,
        };
        previewButton.OnPressed += _ => PlayPreviewTTS(voice.ID);

        var isSelected = Profile?.TTSVoice == voice.ID;
        var selectButton = new Button
        {
            Text = Loc.GetString(isSelected
                ? "humanoid-profile-editor-voice-selected"
                : "humanoid-profile-editor-voice-select"),
            Disabled = isSelected,
            MinWidth = 96,
        };
        selectButton.OnPressed += _ =>
        {
            SetTTSVoice(voice.ID);
            _ttsVoiceListGender = voice.Gender;
            UpdateTTSVoice();
        };

        row.AddChild(details);
        row.AddChild(previewButton);
        row.AddChild(selectButton);
        return row;
    }

    private string BuildTTSVoiceEntryText(TTSVoicePrototype voice)
    {
        return $"{LocalizeTTSVoiceName(voice)}\n{BuildTTSVoiceSubtitle(voice)}";
    }

    private string BuildTTSVoiceSubtitle(TTSVoicePrototype voice)
    {
        if (string.IsNullOrWhiteSpace(voice.Description))
            return voice.Speaker;

        return $"{voice.Description} [{voice.Speaker}]";
    }

    private void UpdateTTSVoiceSummary()
    {
        if (Profile is null)
            return;

        var genderRule = GetTTSVoiceGenderRuleText();
        var visibleVoices = _ttsVoicePrototypes.Count(voice => MatchesTTSVoiceListGender(voice));
        if (Profile.TTSVoice == null ||
            !_prototypeManager.TryIndex<TTSVoicePrototype>(Profile.TTSVoice, out var selectedVoice))
        {
            TTSVoiceStatus.SetMessage(
                $"{Loc.GetString("humanoid-profile-editor-voice-current-none")}\n{genderRule}\n{Loc.GetString("humanoid-profile-editor-voice-current-available", ("count", visibleVoices))}");
            TTSVoiceCurrentPreviewButton.Disabled = true;
            TTSVoiceClearButton.Disabled = true;
            return;
        }

        var lines = new List<string>
        {
            Loc.GetString("humanoid-profile-editor-voice-current", ("voice", LocalizeTTSVoiceName(selectedVoice))),
            Loc.GetString("humanoid-profile-editor-voice-current-source", ("source", selectedVoice.Source)),
            Loc.GetString("humanoid-profile-editor-voice-current-speaker", ("speaker", selectedVoice.Speaker)),
        };

        if (!string.IsNullOrWhiteSpace(selectedVoice.Description))
        {
            lines.Add(Loc.GetString(
                "humanoid-profile-editor-voice-current-description",
                ("description", selectedVoice.Description)));
        }

        lines.Add(genderRule);
        lines.Add(Loc.GetString("humanoid-profile-editor-voice-current-available", ("count", visibleVoices)));

        TTSVoiceStatus.SetMessage(string.Join("\n", lines));
        TTSVoiceCurrentPreviewButton.Disabled = false;
        TTSVoiceClearButton.Disabled = false;
    }

    private string GetTTSVoiceGenderRuleText()
    {
        if (Profile is null)
            return string.Empty;

        if (TTSVoiceRequirements.TryGetForcedVoiceGender(Profile.Gender, out var forcedGender))
        {
            return Loc.GetString(forcedGender == Gender.Male
                ? "humanoid-profile-editor-voice-gender-locked-male"
                : "humanoid-profile-editor-voice-gender-locked-female");
        }

        return Loc.GetString(
            "humanoid-profile-editor-voice-gender-free",
            ("gender", Loc.GetString(_ttsVoiceListGender == Gender.Female
                ? "humanoid-profile-editor-voice-gender-female"
                : "humanoid-profile-editor-voice-gender-male")));
    }

    private bool MatchesTTSVoiceSearch(TTSVoicePrototype voice, string search)
    {
        if (string.IsNullOrEmpty(search))
            return true;

        return BuildTTSVoiceSearchText(voice).Contains(search, System.StringComparison.Ordinal);
    }

    private string BuildTTSVoiceSearchText(TTSVoicePrototype voice)
    {
        return NormalizeTTSVoiceSearch(string.Join(
            '\n',
            LocalizeTTSVoiceName(voice),
            voice.Speaker,
            voice.Source,
            voice.Description));
    }

    private static string NormalizeTTSVoiceSearch(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    private static int CompareTTSVoices(TTSVoicePrototype left, TTSVoicePrototype right)
    {
        var sourceCompare = string.Compare(left.Source, right.Source, System.StringComparison.OrdinalIgnoreCase);
        if (sourceCompare != 0)
            return sourceCompare;

        var nameCompare = string.Compare(left.Name, right.Name, System.StringComparison.OrdinalIgnoreCase);
        if (nameCompare != 0)
            return nameCompare;

        return string.Compare(left.Speaker, right.Speaker, System.StringComparison.OrdinalIgnoreCase);
    }

    private void PlayPreviewTTS(string? voiceId = null)
    {
        var resolvedVoiceId = voiceId ?? Profile?.TTSVoice;
        if (resolvedVoiceId == null ||
            !_prototypeManager.TryIndex<TTSVoicePrototype>(resolvedVoiceId, out var voice))
            return;

        if (!_resManager.ContentFileExists(voice.PreviewPath))
            return;

        _entManager.System<AudioSystem>()
            .PlayGlobal(new SoundPathSpecifier(voice.PreviewPath), Filter.Local(), false);
    }

    private string LocalizeTTSVoiceName(TTSVoicePrototype voice)
    {
        return string.IsNullOrWhiteSpace(voice.Name)
            ? voice.Speaker
            : FormattedMessage.RemoveMarkupPermissive(voice.Name);
    }
    // AltHub Space -> end (TTS)
}
