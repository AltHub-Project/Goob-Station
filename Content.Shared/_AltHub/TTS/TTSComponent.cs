// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._AltHub.TTS;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TTSComponent : Component
{
    [DataField("voice"), AutoNetworkedField]
    public ProtoId<TTSVoicePrototype>? VoicePrototypeId;
}
