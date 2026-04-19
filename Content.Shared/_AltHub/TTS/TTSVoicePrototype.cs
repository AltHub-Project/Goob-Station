// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._AltHub.TTS;

[Prototype("ttsVoice")]
public sealed class TTSVoicePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; } = default!;

    [DataField(required: true)]
    public string Name { get; } = string.Empty;

    [DataField(required: true)]
    public string Speaker { get; } = string.Empty;

    [DataField("description")]
    private readonly string? _description;

    [DataField("source")]
    private readonly string? _source;

    public string Description => _description ?? string.Empty;

    public string Source => _source ?? string.Empty;

    [DataField]
    public bool RoundStart { get; } = true;

    [DataField]
    public bool AdminAnnounceRoundPool { get; } = false;

    [DataField]
    public HashSet<ProtoId<SpeciesPrototype>>? SpeciesWhitelist { get; }

    public ResPath PreviewPath => new($"/Audio/_AltHub/TTS/Previews/{ID}.ogg");
}
