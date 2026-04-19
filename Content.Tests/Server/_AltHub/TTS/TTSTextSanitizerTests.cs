// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

#nullable enable

using Content.Server._AltHub.TTS;
using NUnit.Framework;

namespace Content.Tests.Server._AltHub.TTS;

[TestFixture]
public sealed class TTSTextSanitizerTests
{
    [TestCase("Первая. Вторая.", "Первая; Вторая")]
    [TestCase("Первая...\nВторая", "Первая; Вторая")]
    [TestCase("Первая,\nВторая", "Первая, Вторая")]
    [TestCase("Первая; Вторая", "Первая; Вторая")]
    [TestCase("Первая: Вторая", "Первая: Вторая")]
    [TestCase("Что? Правда!", "Что? Правда!")]
    [TestCase("1.5 14.2.1", "1.5 14.2.1")]
    [TestCase("goobstation.com", "goobstation.com")]
    public void PrepareForSynthesis_SanitizesExpectedText(string source, string expected)
    {
        var result = TTSTextSanitizer.PrepareForSynthesis(source);

        Assert.That(result, Is.EqualTo(expected));
    }
}
