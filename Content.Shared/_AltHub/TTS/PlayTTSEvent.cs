// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

using Robust.Shared.Serialization;

namespace Content.Shared._AltHub.TTS;

[Serializable, NetSerializable]
public sealed class PlayTTSEvent : EntityEventArgs
{
    public byte[] Data { get; }
    public NetEntity? SourceUid { get; }
    public TTSPlaybackKind Kind { get; }
    public bool IsWhisper { get; }
    public bool ApplyCommunicationPreset { get; }

    public PlayTTSEvent(
        byte[] data,
        NetEntity? sourceUid,
        TTSPlaybackKind kind,
        bool isWhisper = false,
        bool applyCommunicationPreset = false)
    {
        Data = data;
        SourceUid = sourceUid;
        Kind = kind;
        IsWhisper = isWhisper;
        ApplyCommunicationPreset = applyCommunicationPreset;
    }
}
