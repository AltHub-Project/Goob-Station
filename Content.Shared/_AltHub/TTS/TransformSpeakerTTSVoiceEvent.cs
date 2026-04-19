// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

using Content.Shared.Inventory;

namespace Content.Shared._AltHub.TTS;

public sealed class TransformSpeakerTTSVoiceEvent : EntityEventArgs, IInventoryRelayEvent
{
    public SlotFlags TargetSlots { get; } = SlotFlags.WITHOUT_POCKET;
    public EntityUid Sender { get; }
    public string? VoiceId { get; set; }

    public TransformSpeakerTTSVoiceEvent(EntityUid sender, string? voiceId)
    {
        Sender = sender;
        VoiceId = voiceId;
    }
}
