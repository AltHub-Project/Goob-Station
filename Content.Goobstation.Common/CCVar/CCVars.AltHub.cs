// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

using Robust.Shared.Configuration;

namespace Content.Goobstation.Common.CCVar;

public sealed partial class GoobCVars
{
    public static readonly CVarDef<bool> AltHubTTSEnabled =
        CVarDef.Create("althub.tts.enable", false, CVar.SERVER | CVar.REPLICATED);

    public static readonly CVarDef<string> AltHubTTSApiUrl =
        CVarDef.Create("althub.tts.api_url", "https://ntts.fdev.team/api/v1/tts", CVar.SERVERONLY);

    public static readonly CVarDef<string> AltHubTTSApiToken =
        CVarDef.Create("althub.tts.api_token", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    public static readonly CVarDef<float> AltHubTTSRateLimitRps =
        CVarDef.Create("althub.tts.rate_limit_rps", 1f, CVar.SERVERONLY);

    public static readonly CVarDef<int> AltHubTTSRateBurst =
        CVarDef.Create("althub.tts.rate_burst", 1, CVar.SERVERONLY);

    public static readonly CVarDef<int> AltHubTTSMaxConcurrency =
        CVarDef.Create("althub.tts.max_concurrency", 4, CVar.SERVERONLY);

    public static readonly CVarDef<int> AltHubTTSCacheMaxBytes =
        CVarDef.Create("althub.tts.cache.max_bytes", 64 * 1024 * 1024, CVar.SERVERONLY);

    public static readonly CVarDef<int> AltHubTTSCacheTtlSeconds =
        CVarDef.Create("althub.tts.cache.ttl_seconds", 900, CVar.SERVERONLY);

    public static readonly CVarDef<float> AltHubTTSSpeechMaxStalenessSeconds =
        CVarDef.Create("althub.tts.speech_max_staleness_seconds", 2f, CVar.SERVERONLY);

    public static readonly CVarDef<float> AltHubTTSRadioMaxStalenessSeconds =
        CVarDef.Create("althub.tts.radio_max_staleness_seconds", 4f, CVar.SERVERONLY);

    public static readonly CVarDef<float> AltHubTTSRequestTimeoutSeconds =
        CVarDef.Create("althub.tts.request_timeout_seconds", 10f, CVar.SERVERONLY);

    public static readonly CVarDef<bool> AltHubTTSDebugLogging =
        CVarDef.Create("althub.tts.debug_logging", false, CVar.SERVERONLY);
}
