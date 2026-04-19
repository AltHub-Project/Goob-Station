// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

#nullable enable

using System.Reflection;
using System.Runtime.CompilerServices;
using Content.Shared._AltHub.TTS;
using NUnit.Framework;
using Robust.Shared.Enums;

namespace Content.Tests.Server._AltHub.TTS;

[TestFixture]
public sealed class TTSVoiceRequirementsTests
{
    [Test]
    public void IsSelectableForProfile_RestrictsBinaryProfileToMatchingVoiceGender()
    {
        var maleVoice = BuildVoice("male_voice", Gender.Male);
        var femaleVoice = BuildVoice("female_voice", Gender.Female);

        Assert.That(TTSVoiceRequirements.IsSelectableForProfile(maleVoice, "Human", Gender.Male), Is.True);
        Assert.That(TTSVoiceRequirements.IsSelectableForProfile(femaleVoice, "Human", Gender.Male), Is.False);
        Assert.That(TTSVoiceRequirements.IsSelectableForProfile(femaleVoice, "Human", Gender.Female), Is.True);
        Assert.That(TTSVoiceRequirements.IsSelectableForProfile(maleVoice, "Human", Gender.Female), Is.False);
    }

    [Test]
    public void IsSelectableForProfile_AllowsBothBinaryVoiceGendersForEpiceneProfiles()
    {
        var maleVoice = BuildVoice("male_voice", Gender.Male);
        var femaleVoice = BuildVoice("female_voice", Gender.Female);

        Assert.That(TTSVoiceRequirements.IsSelectableForProfile(maleVoice, "Human", Gender.Epicene), Is.True);
        Assert.That(TTSVoiceRequirements.IsSelectableForProfile(femaleVoice, "Human", Gender.Epicene), Is.True);
        Assert.That(TTSVoiceRequirements.IsSelectableForProfile(maleVoice, "Human", Gender.Neuter), Is.True);
        Assert.That(TTSVoiceRequirements.IsSelectableForProfile(femaleVoice, "Human", Gender.Neuter), Is.True);
    }

    [Test]
    public void TryGetForcedVoiceGender_OnlyForBinaryProfileGenders()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TTSVoiceRequirements.TryGetForcedVoiceGender(Gender.Male, out var maleGender), Is.True);
            Assert.That(maleGender, Is.EqualTo(Gender.Male));

            Assert.That(TTSVoiceRequirements.TryGetForcedVoiceGender(Gender.Female, out var femaleGender), Is.True);
            Assert.That(femaleGender, Is.EqualTo(Gender.Female));

            Assert.That(TTSVoiceRequirements.TryGetForcedVoiceGender(Gender.Epicene, out _), Is.False);
            Assert.That(TTSVoiceRequirements.TryGetForcedVoiceGender(Gender.Neuter, out _), Is.False);
        });
    }

    [Test]
    public void IsSelectableForProfile_AllowsUnknownVoiceGenderAsFallback()
    {
        var neuterVoice = BuildVoice("neutral_voice", Gender.Neuter);

        Assert.That(TTSVoiceRequirements.IsSelectableForProfile(neuterVoice, "Human", Gender.Male), Is.True);
        Assert.That(TTSVoiceRequirements.IsSelectableForProfile(neuterVoice, "Human", Gender.Female), Is.True);
        Assert.That(TTSVoiceRequirements.IsSelectableForProfile(neuterVoice, "Human", Gender.Epicene), Is.True);
        Assert.That(TTSVoiceRequirements.IsSelectableForProfile(neuterVoice, "Human", Gender.Neuter), Is.True);
    }

    private static TTSVoicePrototype BuildVoice(string id, Gender gender)
    {
        var voice = (TTSVoicePrototype) RuntimeHelpers.GetUninitializedObject(typeof(TTSVoicePrototype));
        SetField(voice, "<ID>k__BackingField", id);
        SetField(voice, "<Name>k__BackingField", id);
        SetField(voice, "<Speaker>k__BackingField", id);
        SetField(voice, "<Gender>k__BackingField", gender);
        SetField(voice, "_source", "/N/TTS");
        return voice;
    }

    private static void SetField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found on {target.GetType().Name}.");
        field!.SetValue(target, value);
    }
}
