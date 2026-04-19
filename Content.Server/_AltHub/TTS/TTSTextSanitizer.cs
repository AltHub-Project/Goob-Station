// SPDX-FileCopyrightText: Yaroslav Yudaev <ydaevy10@gmail.com>
//
// SPDX-License-Identifier: MIT

using System.Text;
using Robust.Shared.Utility;

namespace Content.Server._AltHub.TTS;

public static class TTSTextSanitizer
{
    public static string PrepareForSynthesis(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var stripped = FormattedMessage.RemoveMarkupPermissive(text);
        var cleaned = RemoveUnsupportedControlCharacters(stripped);
        if (cleaned.Length == 0)
            return string.Empty;

        var builder = new StringBuilder(cleaned.Length);

        for (var i = 0; i < cleaned.Length; i++)
        {
            var current = cleaned[i];

            if (IsLineBreak(current))
            {
                i = ConsumeLineBreakCluster(cleaned, i);
                AppendCanonicalPause(builder, cleaned, i + 1);
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                AppendWhitespace(builder);
                continue;
            }

            if (current == '…')
            {
                i = ConsumeEllipsisRun(cleaned, i);
                AppendCanonicalPause(builder, cleaned, i + 1);
                continue;
            }

            if (current == '.')
            {
                var lastDotIndex = ConsumeDotRun(cleaned, i);
                if (lastDotIndex > i)
                {
                    i = lastDotIndex;
                    AppendCanonicalPause(builder, cleaned, i + 1);
                    continue;
                }

                if (ShouldPreserveLiteralDot(cleaned, i))
                {
                    AppendDeferredSeparatorSpace(builder);
                    builder.Append(current);
                    continue;
                }

                AppendCanonicalPause(builder, cleaned, i + 1);
                continue;
            }

            if (current is ',' or ';' or ':')
            {
                AppendPauseSeparator(builder, current);
                continue;
            }

            if (current is '?' or '!')
            {
                AppendExpressionSeparator(builder, current);
                continue;
            }

            AppendDeferredSeparatorSpace(builder);
            builder.Append(current);
        }

        return TrimBoundarySeparators(builder.ToString());
    }

    private static string RemoveUnsupportedControlCharacters(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsControl(rune) && !Rune.IsWhiteSpace(rune))
                continue;

            builder.Append(rune.ToString());
        }

        return builder.ToString();
    }

    private static int ConsumeLineBreakCluster(string text, int start)
    {
        var index = start;
        while (index + 1 < text.Length && char.IsWhiteSpace(text[index + 1]))
        {
            index++;
        }

        return index;
    }

    private static int ConsumeEllipsisRun(string text, int start)
    {
        var index = start;
        while (index + 1 < text.Length && text[index + 1] == '…')
        {
            index++;
        }

        return index;
    }

    private static int ConsumeDotRun(string text, int start)
    {
        var index = start;
        while (index + 1 < text.Length && text[index + 1] == '.')
        {
            index++;
        }

        return index;
    }

    private static bool ShouldPreserveLiteralDot(string text, int index)
    {
        if (index > 0 &&
            index + 1 < text.Length &&
            char.IsDigit(text[index - 1]) &&
            char.IsDigit(text[index + 1]))
        {
            return true;
        }

        var lookahead = index + 1;
        while (lookahead < text.Length && IsClosingBoundary(text[lookahead]))
        {
            lookahead++;
        }

        if (lookahead >= text.Length)
            return false;

        return !char.IsWhiteSpace(text[lookahead]);
    }

    private static void AppendWhitespace(StringBuilder builder)
    {
        if (builder.Length == 0 || char.IsWhiteSpace(builder[builder.Length - 1]))
            return;

        var last = FindLastNonWhitespaceChar(builder);
        if (last is ',' or ';' or ':' or '?' or '!')
            return;

        builder.Append(' ');
    }

    private static void AppendCanonicalPause(StringBuilder builder, string source, int nextIndex)
    {
        if (!HasTextAhead(source, nextIndex))
            return;

        TrimTrailingWhitespace(builder);
        var last = FindLastNonWhitespaceChar(builder);
        if (last == null || last is ',' or ';' or ':' or '?' or '!')
            return;

        builder.Append(';');
    }

    private static void AppendPauseSeparator(StringBuilder builder, char separator)
    {
        TrimTrailingWhitespace(builder);
        var last = FindLastNonWhitespaceChar(builder);
        if (last == null)
            return;

        if (last is ',' or ';' or ':' or '?' or '!')
            return;

        builder.Append(separator);
    }

    private static void AppendExpressionSeparator(StringBuilder builder, char separator)
    {
        TrimTrailingWhitespace(builder);
        var last = FindLastNonWhitespaceChar(builder);
        if (last == null || last is ',' or ';' or ':')
            return;

        builder.Append(separator);
    }

    private static void AppendDeferredSeparatorSpace(StringBuilder builder)
    {
        if (builder.Length == 0 || char.IsWhiteSpace(builder[builder.Length - 1]))
            return;

        var last = FindLastNonWhitespaceChar(builder);
        if (last is ',' or ';' or ':' or '?' or '!')
            builder.Append(' ');
    }

    private static bool HasTextAhead(string text, int index)
    {
        for (var i = index; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
                continue;

            if (text[i] is ';' or ',' or ':')
                continue;

            return true;
        }

        return false;
    }

    private static char? FindLastNonWhitespaceChar(StringBuilder builder)
    {
        for (var i = builder.Length - 1; i >= 0; i--)
        {
            if (!char.IsWhiteSpace(builder[i]))
                return builder[i];
        }

        return null;
    }

    private static void TrimTrailingWhitespace(StringBuilder builder)
    {
        while (builder.Length > 0 && char.IsWhiteSpace(builder[builder.Length - 1]))
        {
            builder.Length--;
        }
    }

    private static string TrimBoundarySeparators(string text)
    {
        var start = 0;
        while (start < text.Length && (char.IsWhiteSpace(text[start]) || text[start] is ';' or ',' or ':'))
        {
            start++;
        }

        var end = text.Length - 1;
        while (end >= start && (char.IsWhiteSpace(text[end]) || text[end] is ';' or ',' or ':'))
        {
            end--;
        }

        return end < start
            ? string.Empty
            : text.Substring(start, end - start + 1);
    }

    private static bool IsClosingBoundary(char value)
    {
        return value is '"' or '\'' or ')' or ']' or '}' or '»';
    }

    private static bool IsLineBreak(char value)
    {
        return value is '\r' or '\n';
    }
}
