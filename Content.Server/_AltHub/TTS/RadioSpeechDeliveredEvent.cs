// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

using Content.Shared._EinsteinEngines.Language;
using Content.Shared.Radio;
using Robust.Shared.Player;

namespace Content.Server._AltHub.TTS;

public sealed class RadioSpeechDeliveredEvent : EntityEventArgs
{
    public EntityUid Source { get; }
    public string Message { get; }
    public RadioChannelPrototype Channel { get; }
    public EntityUid RadioSource { get; }
    public LanguagePrototype Language { get; }
    public IReadOnlyCollection<ICommonSession> Recipients { get; }

    public RadioSpeechDeliveredEvent(
        EntityUid source,
        string message,
        RadioChannelPrototype channel,
        EntityUid radioSource,
        LanguagePrototype language,
        IReadOnlyCollection<ICommonSession> recipients)
    {
        Source = source;
        Message = message;
        Channel = channel;
        RadioSource = radioSource;
        Language = language;
        Recipients = recipients;
    }
}
