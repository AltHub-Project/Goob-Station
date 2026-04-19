// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

#nullable enable

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._AltHub.TTS;
using NUnit.Framework;
using Robust.Shared.Log;

namespace Content.Tests.Server._AltHub.TTS;

[TestFixture]
public sealed class TTSProviderClientTests
{
    [Test]
    public async Task SynthesizeAsync_SendsExpectedRequest()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var handler = new RecordingHandler(_ =>
            Task.FromResult(CreateResponse("payload"u8.ToArray())));
        var sawmill = new TestSawmill();

        using var http = new HttpClient(handler);
        using var provider = new TTSProviderClient(http, () => DefaultOptions(), sawmill, time);

        var result = await provider.SynthesizeAsync("planya", "Hello world", TTSRequestPriority.Speech);

        Assert.That(result, Is.EqualTo("payload"u8.ToArray()));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));

        var request = handler.Requests[0];
        Assert.That(request.Headers.Authorization, Is.EqualTo(new AuthenticationHeaderValue("Bearer", "token")));
        Assert.That(request.RequestUri, Is.Not.Null);
        Assert.That(request.RequestUri!.ToString(), Does.Contain("/api/v1/tts?speaker=planya"));
        Assert.That(request.RequestUri!.ToString(), Does.Contain("text=Hello world"));
        Assert.That(request.RequestUri!.ToString(), Does.Contain("ext=ogg"));
    }

    [Test]
    public async Task SynthesizeAsync_UsesCacheUntilTtlExpires()
    {
        var time = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var handler = new RecordingHandler(_ =>
            Task.FromResult(CreateResponse("cache"u8.ToArray())));

        using var http = new HttpClient(handler);
        using var provider = new TTSProviderClient(http, () => DefaultOptions(cacheTtlSeconds: 60), new TestSawmill(), time);

        var first = await provider.SynthesizeAsync("planya", "same", TTSRequestPriority.Speech);
        var second = await provider.SynthesizeAsync("planya", "same", TTSRequestPriority.Speech);
        time.Advance(TimeSpan.FromSeconds(61));
        var third = await provider.SynthesizeAsync("planya", "same", TTSRequestPriority.Speech);

        Assert.That(first, Is.EqualTo("cache"u8.ToArray()));
        Assert.That(second, Is.EqualTo("cache"u8.ToArray()));
        Assert.That(third, Is.EqualTo("cache"u8.ToArray()));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task SynthesizeAsync_DeduplicatesInflightRequests()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler(async _ =>
        {
            await gate.Task;
            return CreateResponse("dedupe"u8.ToArray());
        });

        using var http = new HttpClient(handler);
        using var provider = new TTSProviderClient(http, () => DefaultOptions(), new TestSawmill(), new MutableTimeProvider(DateTimeOffset.UtcNow));

        var firstTask = provider.SynthesizeAsync("planya", "parallel", TTSRequestPriority.Speech).AsTask();
        var secondTask = provider.SynthesizeAsync("planya", "parallel", TTSRequestPriority.Speech).AsTask();

        await Task.Delay(50);
        Assert.That(handler.Requests, Has.Count.EqualTo(1));

        gate.SetResult();

        var first = await firstTask;
        var second = await secondTask;

        Assert.That(first, Is.EqualTo("dedupe"u8.ToArray()));
        Assert.That(second, Is.EqualTo("dedupe"u8.ToArray()));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task SynthesizeAsync_EvictsCacheByByteBudget()
    {
        var responses = new Dictionary<string, byte[]>
        {
            ["first"] = "aaa"u8.ToArray(),
            ["second"] = "bbb"u8.ToArray(),
        };

        var handler = new RecordingHandler(request =>
        {
            var uri = request.RequestUri!.ToString();
            var payload = uri.Contains("text=first", StringComparison.Ordinal)
                ? responses["first"]
                : responses["second"];

            return Task.FromResult(CreateResponse(payload));
        });

        using var http = new HttpClient(handler);
        using var provider = new TTSProviderClient(
            http,
            () => DefaultOptions(cacheMaxBytes: 3),
            new TestSawmill(),
            new MutableTimeProvider(DateTimeOffset.UtcNow));

        await provider.SynthesizeAsync("planya", "first", TTSRequestPriority.Speech);
        await provider.SynthesizeAsync("planya", "second", TTSRequestPriority.Speech);
        await provider.SynthesizeAsync("planya", "first", TTSRequestPriority.Speech);

        Assert.That(handler.Requests, Has.Count.EqualTo(3));
    }

    private static TTSProviderOptions DefaultOptions(
        int cacheMaxBytes = 1024,
        int cacheTtlSeconds = 900)
    {
        return new TTSProviderOptions(
            Enabled: true,
            ApiUrl: "https://ntts.fdev.team",
            ApiToken: "token",
            RateLimitRps: 100,
            RateBurst: 100,
            MaxConcurrency: 4,
            CacheMaxBytes: cacheMaxBytes,
            CacheTtlSeconds: cacheTtlSeconds,
            RequestTimeoutSeconds: 5,
            DebugLogging: false);
    }

    private static HttpResponseMessage CreateResponse(byte[] payload)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        };
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan delta)
        {
            _now += delta;
        }
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> onSend) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(CloneRequest(request));
            return await onSend(request);
        }

        private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);

            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return clone;
        }
    }

    private sealed class TestSawmill : ISawmill
    {
        public string Name => "test.tts";
        public LogLevel? Level { get; set; }
        public void AddHandler(ILogHandler handler) {}
        public void RemoveHandler(ILogHandler handler) {}
        public bool IsLogLevelEnabled(LogLevel level) => false;
        public void Log(LogLevel level, string message, params object?[] args) {}
        public void Log(LogLevel level, Exception? exception, string message, params object?[] args) {}
        public void Log(LogLevel level, string message) {}
        public void Debug(string message, params object?[] args) {}
        public void Debug(string message) {}
        public void Info(string message, params object?[] args) {}
        public void Info(string message) {}
        public void Warning(string message, params object?[] args) {}
        public void Warning(string message) {}
        public void Error(string message, params object?[] args) {}
        public void Error(string message) {}
        public void Fatal(string message, params object?[] args) {}
        public void Fatal(string message) {}
    }
}
