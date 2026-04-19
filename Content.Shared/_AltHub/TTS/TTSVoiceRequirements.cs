// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

using Robust.Shared.Enums;

namespace Content.Shared._AltHub.TTS;

public static class TTSVoiceRequirements
{
    public static bool IsSelectableForProfile(TTSVoicePrototype voice, string species, Gender gender)
    {
        if (voice.SpeciesWhitelist != null && !voice.SpeciesWhitelist.Contains(species))
            return false;

        return gender switch
        {
            Gender.Male => voice.Gender == Gender.Male,
            Gender.Female => voice.Gender == Gender.Female,
            _ => voice.Gender is Gender.Male or Gender.Female,
        };
    }

    public static bool TryGetForcedVoiceGender(Gender profileGender, out Gender forcedVoiceGender)
    {
        switch (profileGender)
        {
            case Gender.Male:
                forcedVoiceGender = Gender.Male;
                return true;
            case Gender.Female:
                forcedVoiceGender = Gender.Female;
                return true;
            default:
                forcedVoiceGender = Gender.Male;
                return false;
        }
    }
}
