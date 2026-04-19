// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Content.Server._AltHub.TTS;

public readonly record struct TTSProviderOptions(
    bool Enabled,
    string ApiUrl,
    string ApiToken,
    float RateLimitRps,
    int RateBurst,
    int MaxConcurrency,
    int CacheMaxBytes,
    int CacheTtlSeconds,
    float RequestTimeoutSeconds,
    bool DebugLogging);

public enum TTSRequestPriority : byte
{
    Announcement,
    Radio,
    Speech,
}

/// <summary>
/// Queues and deduplicates requests to the external AltHub TTS provider.
/// </summary>
public sealed class TTSProviderClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Func<TTSProviderOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ISawmill _sawmill;

    private readonly object _sync = new();
    private readonly PriorityQueue<QueuedRequest, (int Priority, long Sequence)> _queue = new();
    private readonly Dictionary<string, Task<byte[]?>> _inFlight = new();
    private readonly Dictionary<string, CacheEntry> _cache = new();
    private readonly LinkedList<string> _cacheLru = new();
    private readonly SemaphoreSlim _queueSignal = new(0);
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Task _dispatcherTask;

    private DateTimeOffset _lastTokenRefill;
    private double _availableTokens;
    private long _queueSequence;
    private int _activeRequests;
    private long _cachedBytes;

    public TTSProviderClient(
        HttpClient httpClient,
        Func<TTSProviderOptions> options,
        ISawmill sawmill,
        TimeProvider? timeProvider = null)
    {
        _httpClient = httpClient;
        _options = options;
        _sawmill = sawmill;
        _timeProvider = timeProvider ?? TimeProvider.System;

        var now = _timeProvider.GetUtcNow();
        var initial = _options();
        _lastTokenRefill = now;
        _availableTokens = Math.Max(initial.RateBurst, 1);
        _dispatcherTask = Task.Run(DispatchLoopAsync);
    }

    public ValueTask<byte[]?> SynthesizeAsync(
        string speakerId,
        string text,
        TTSRequestPriority priority,
        TimeSpan? maxStaleness = null,
        string? effectId = null,
        CancellationToken cancellationToken = default)
    {
        var options = _options();
        if (!options.Enabled ||
            string.IsNullOrWhiteSpace(speakerId) ||
            string.IsNullOrWhiteSpace(options.ApiUrl) ||
            string.IsNullOrWhiteSpace(options.ApiToken))
        {
            return new ValueTask<byte[]?>((byte[]?) null);
        }

        var requestText = TTSTextSanitizer.PrepareForSynthesis(text);
        if (string.IsNullOrWhiteSpace(requestText))
            return new ValueTask<byte[]?>((byte[]?) null);

        var requestEffect = NormalizeEffectId(effectId);
        var cacheKey = $"{speakerId}\n{requestEffect ?? string.Empty}\n{NormalizeForCache(requestText)}";
        var now = _timeProvider.GetUtcNow();

        lock (_sync)
        {
            if (TryGetCachedNoLock(cacheKey, now, out var cached))
                return new ValueTask<byte[]?>(cached);

            if (_inFlight.TryGetValue(cacheKey, out var inFlight))
                return new ValueTask<byte[]?>(inFlight.WaitAsync(cancellationToken));

            var completion = new TaskCompletionSource<byte[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight[cacheKey] = completion.Task;

            var queued = new QueuedRequest(
                cacheKey,
                speakerId,
                requestText,
                requestEffect,
                priority,
                now,
                maxStaleness,
                completion);

            _queue.Enqueue(queued, ((int) priority, _queueSequence++));
            _queueSignal.Release();

            return new ValueTask<byte[]?>(completion.Task.WaitAsync(cancellationToken));
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            while (_queue.TryDequeue(out var queued, out _))
            {
                queued.Completion.TrySetResult(null);
                _inFlight.Remove(queued.CacheKey);
            }

            _cache.Clear();
            _cacheLru.Clear();
            _cachedBytes = 0;
        }
    }

    public void Dispose()
    {
        _lifetimeCts.Cancel();

        try
        {
            _dispatcherTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // ignored
        }

        _queueSignal.Dispose();
        _lifetimeCts.Dispose();
    }

    private async Task DispatchLoopAsync()
    {
        var token = _lifetimeCts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                var dispatcherDelay = TimeSpan.Zero;
                await _queueSignal.WaitAsync(token);

                while (!token.IsCancellationRequested)
                {
                    QueuedRequest? next = null;
                    TimeSpan delay = TimeSpan.Zero;

                    lock (_sync)
                    {
                        var options = _options();
                        var now = _timeProvider.GetUtcNow();

                        EvictExpiredCacheNoLock(now);

                        if (_activeRequests >= Math.Max(1, options.MaxConcurrency) ||
                            !_queue.TryPeek(out var peeked, out _))
                        {
                            break;
                        }

                        if (peeked.MaxStaleness is { } maxAge &&
                            now - peeked.CreatedAt > maxAge)
                        {
                            _queue.Dequeue();
                            _inFlight.Remove(peeked.CacheKey);
                            peeked.Completion.TrySetResult(null);
                            continue;
                        }

                        if (!TryTakeTokenNoLock(options, now, out var tokenDelay))
                        {
                            dispatcherDelay = tokenDelay;
                            break;
                        }

                        next = _queue.Dequeue();
                        _activeRequests++;
                    }

                    if (next == null)
                        break;

                    _ = ProcessQueuedAsync(next.Value, token);
                }

                if (dispatcherDelay > TimeSpan.Zero && !token.IsCancellationRequested)
                {
                    await Task.Delay(dispatcherDelay, token);
                    if (!token.IsCancellationRequested)
                        _queueSignal.Release();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
    }

    private async Task ProcessQueuedAsync(QueuedRequest request, CancellationToken token)
    {
        byte[]? data = null;

        try
        {
            data = await ExecuteRequestAsync(request, token);

            if (data != null)
            {
                lock (_sync)
                {
                    InsertCacheNoLock(request.CacheKey, data, _timeProvider.GetUtcNow());
                }
            }
        }
        catch (OperationCanceledException)
        {
            data = null;
        }
        catch (Exception e)
        {
            _sawmill.Error($"AltHub TTS request for speaker '{request.SpeakerId}' failed: {e}");
        }
        finally
        {
            lock (_sync)
            {
                _activeRequests = Math.Max(0, _activeRequests - 1);
                _inFlight.Remove(request.CacheKey);
            }

            request.Completion.TrySetResult(data);

            lock (_sync)
            {
                if (_queue.Count > 0)
                    _queueSignal.Release();
            }
        }
    }

    private async Task<byte[]?> ExecuteRequestAsync(QueuedRequest request, CancellationToken token)
    {
        var options = _options();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1f, options.RequestTimeoutSeconds)));

        var requestUri = BuildRequestUri(options.ApiUrl, request.SpeakerId, request.Text, request.EffectId);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, requestUri);
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiToken);

        if (options.DebugLogging)
        {
            _sawmill.Info(
                $"AltHub TTS enqueue -> GET {requestUri}, priority={request.Priority}, text='{request.Text}'");
        }

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            timeoutCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            if (options.DebugLogging || (int) response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                _sawmill.Warning(
                    $"AltHub TTS returned {(int) response.StatusCode} {response.StatusCode} for speaker '{request.SpeakerId}'.");
            }

            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(timeoutCts.Token);
        if (bytes.Length == 0)
            return null;

        return bytes;
    }

    private static string BuildRequestUri(string apiUrl, string speakerId, string text, string? effectId)
    {
        var normalizedBase = apiUrl.Trim();
        if (!normalizedBase.EndsWith("/api/v1/tts", StringComparison.OrdinalIgnoreCase))
            normalizedBase = normalizedBase.TrimEnd('/') + "/api/v1/tts";

        var uri = string.Create(
            CultureInfo.InvariantCulture,
            $"{normalizedBase}?speaker={Uri.EscapeDataString(speakerId)}&text={Uri.EscapeDataString(text)}&ext=ogg");

        if (string.IsNullOrWhiteSpace(effectId))
            return uri;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{uri}&effect={Uri.EscapeDataString(effectId)}");
    }

    private bool TryTakeTokenNoLock(TTSProviderOptions options, DateTimeOffset now, out TimeSpan delay)
    {
        var burst = Math.Max(1, options.RateBurst);
        var rps = Math.Max(0.01f, options.RateLimitRps);

        var elapsedSeconds = (now - _lastTokenRefill).TotalSeconds;
        if (elapsedSeconds > 0)
        {
            _availableTokens = Math.Min(burst, _availableTokens + elapsedSeconds * rps);
            _lastTokenRefill = now;
        }

        if (_availableTokens >= 1)
        {
            _availableTokens -= 1;
            delay = TimeSpan.Zero;
            return true;
        }

        var secondsUntilToken = (1 - _availableTokens) / rps;
        delay = TimeSpan.FromSeconds(Math.Max(0.01, secondsUntilToken));
        return false;
    }

    private bool TryGetCachedNoLock(string cacheKey, DateTimeOffset now, out byte[]? data)
    {
        if (!_cache.TryGetValue(cacheKey, out var entry))
        {
            data = null;
            return false;
        }

        if (entry.ExpiresAt <= now)
        {
            RemoveCacheEntryNoLock(cacheKey, entry);
            data = null;
            return false;
        }

        _cacheLru.Remove(entry.Node);
        _cacheLru.AddLast(entry.Node);
        data = entry.Data;
        return true;
    }

    private void InsertCacheNoLock(string cacheKey, byte[] data, DateTimeOffset now)
    {
        var options = _options();
        var maxBytes = Math.Max(0, options.CacheMaxBytes);
        if (maxBytes == 0 || data.Length > maxBytes)
            return;

        if (_cache.TryGetValue(cacheKey, out var existing))
            RemoveCacheEntryNoLock(cacheKey, existing);

        var expiresAt = now + TimeSpan.FromSeconds(Math.Max(1, options.CacheTtlSeconds));
        var node = new LinkedListNode<string>(cacheKey);
        _cacheLru.AddLast(node);
        _cache[cacheKey] = new CacheEntry(data, expiresAt, node);
        _cachedBytes += data.Length;

        EvictExpiredCacheNoLock(now);
        while (_cachedBytes > maxBytes && _cacheLru.First is { } first)
        {
            var oldestKey = first.Value;
            if (!_cache.TryGetValue(oldestKey, out var oldestEntry))
                break;

            RemoveCacheEntryNoLock(oldestKey, oldestEntry);
        }
    }

    private void EvictExpiredCacheNoLock(DateTimeOffset now)
    {
        if (_cache.Count == 0)
            return;

        var toRemove = new List<string>();
        foreach (var (key, entry) in _cache)
        {
            if (entry.ExpiresAt <= now)
                toRemove.Add(key);
        }

        foreach (var key in toRemove)
        {
            if (_cache.TryGetValue(key, out var entry))
                RemoveCacheEntryNoLock(key, entry);
        }
    }

    private void RemoveCacheEntryNoLock(string cacheKey, CacheEntry entry)
    {
        _cache.Remove(cacheKey);
        _cacheLru.Remove(entry.Node);
        _cachedBytes -= entry.Data.Length;
    }

    private static string NormalizeForCache(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previousWhitespace = false;

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsControl(rune) && !Rune.IsWhiteSpace(rune))
                continue;

            if (Rune.IsWhiteSpace(rune))
            {
                if (previousWhitespace)
                    continue;

                builder.Append(' ');
                previousWhitespace = true;
                continue;
            }

            previousWhitespace = false;
            builder.Append(rune.ToString());
        }

        return builder.ToString().Trim();
    }

    private static string? NormalizeEffectId(string? effectId)
    {
        return string.IsNullOrWhiteSpace(effectId)
            ? null
            : effectId.Trim();
    }

    private readonly record struct QueuedRequest(
        string CacheKey,
        string SpeakerId,
        string Text,
        string? EffectId,
        TTSRequestPriority Priority,
        DateTimeOffset CreatedAt,
        TimeSpan? MaxStaleness,
        TaskCompletionSource<byte[]?> Completion);

    private readonly record struct CacheEntry(
        byte[] Data,
        DateTimeOffset ExpiresAt,
        LinkedListNode<string> Node);
}
