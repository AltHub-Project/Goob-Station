// SPDX-FileCopyrightText: 2025 GabyChangelog <agentepanela2@gmail.com>
// SPDX-FileCopyrightText: 2025 Kyoth25f <kyoth25f@gmail.com>
//
// SPDX-License-Identifier: MIT

using Content.Shared.Speech;
using Content.Shared._AltHub.TTS; // AltHub Space
using Content.Shared.StatusIcon;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Shared.IntrinsicVoiceModulator.Components;

[RegisterComponent]
public sealed partial class IntrinsicVoiceModulatorComponent : Component
{
    [DataField]
    public string VoiceName = "";

    [DataField]
    public ProtoId<SpeechVerbPrototype>? SpeechVerbProtoId;

    [DataField("ttsVoiceId")]
    public ProtoId<TTSVoicePrototype>? TTSVoiceId; // AltHub Space (TTS)

    [DataField]
    public ProtoId<JobIconPrototype>? JobIconProtoId;

    [DataField]
    public string? JobName;
}
