// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

using Robust.Shared.Serialization;

namespace Content.Shared._AltHub.TTS;

[Serializable, NetSerializable]
public enum TTSPlaybackKind : byte
{
    Speech,
    Radio,
    Announcement,
}
