// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

namespace Content.Shared._AltHub.TTS;

public static class TTSEffects
{
    public const string Announce = "announce";
    public const string Radio = "radio";
    public const string Reverse = "reverse";
    public const string Robotic = "robotic";
    public const string Echo = "echo";
    public const string Ghost = "ghost";

    public static IReadOnlyList<string> Ordered { get; } =
    [
        Announce,
        Radio,
        Reverse,
        Robotic,
        Echo,
        Ghost,
    ];

    public static bool IsSupported(string? effectId)
    {
        return effectId is Announce or Radio or Reverse or Robotic or Echo or Ghost;
    }
}
