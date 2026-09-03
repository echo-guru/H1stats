using System.Globalization;
using System.Text;

namespace H1Stats.Infrastructure.Research;

/// <summary>Strips RTF control words and destinations to plain text for LLM screening.</summary>
public static class RtfTextExtractor
{
    private static readonly HashSet<string> SkipDestinations = new(StringComparer.OrdinalIgnoreCase)
    {
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "object", "header", "footer",
        "headerl", "headerr", "footerl", "footerr", "footnote", "annotation",
        "themedata", "colorschememapping", "latentstyles", "xmlnstbl", "rsidtbl",
        "mmathPr", "datastore", "defchp", "defpap", "wgrffmtfilter",
    };

    public static string Extract(string rtf)
    {
        if (string.IsNullOrWhiteSpace(rtf))
            return "";

        var output = new StringBuilder();
        var skipStack = new Stack<bool>();
        var skip = false;
        var i = 0;

        while (i < rtf.Length)
        {
            var c = rtf[i];
            if (c == '{')
            {
                var destinationSkip = skip || IsSkipGroup(rtf, i + 1);
                skipStack.Push(skip);
                skip = destinationSkip;
                i++;
                continue;
            }

            if (c == '}')
            {
                if (skipStack.Count > 0)
                    skip = skipStack.Pop();
                i++;
                continue;
            }

            if (c == '\\')
            {
                i = ReadControl(rtf, i + 1, skip, output);
                continue;
            }

            if (!skip && !char.IsControl(c))
                output.Append(c);
            i++;
        }

        return CollapseWhitespace(output.ToString());
    }

    private static bool IsSkipGroup(string rtf, int i)
    {
        if (i >= rtf.Length)
            return false;
        if (rtf[i] == '\\' && i + 1 < rtf.Length && rtf[i + 1] == '*')
            return true;
        if (rtf[i] != '\\')
            return false;

        var (word, _) = ReadControlWord(rtf, i + 1);
        return SkipDestinations.Contains(word);
    }

    private static int ReadControl(string rtf, int i, bool skip, StringBuilder output)
    {
        if (i >= rtf.Length)
            return i;

        var c = rtf[i];
        if (c is '{' or '}' or '\\')
        {
            if (!skip)
                output.Append(c);
            return i + 1;
        }

        if (c == '\'')
        {
            if (i + 2 < rtf.Length && int.TryParse(rtf.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            {
                if (!skip)
                    output.Append((char)hex);
                return i + 3;
            }
            return i + 1;
        }

        if (!char.IsLetter(c))
        {
            if (!skip && c == '~')
                output.Append(' ');
            else if (!skip && c is '-' or '_')
                output.Append('-');
            return i + 1;
        }

        var (word, afterWord) = ReadControlWord(rtf, i);
        var (number, afterNumber) = ReadOptionalNumber(rtf, afterWord);
        var next = afterNumber < rtf.Length && rtf[afterNumber] == ' ' ? afterNumber + 1 : afterNumber;

        if (skip)
            return next;

        switch (word)
        {
            case "par":
            case "line":
            case "row":
            case "page":
            case "sect":
                output.AppendLine();
                break;
            case "tab":
            case "cell":
                output.Append('\t');
                break;
            case "bullet":
                output.Append('•');
                break;
            case "lquote":
            case "rquote":
                output.Append('\'');
                break;
            case "ldblquote":
            case "rdblquote":
                output.Append('"');
                break;
            case "emdash":
            case "endash":
                output.Append('-');
                break;
            case "u":
                if (number is int code)
                {
                    var value = code < 0 ? code + 65536 : code;
                    if (value is >= 0 and <= 0x10FFFF)
                        output.Append(char.ConvertFromUtf32(value));
                }
                if (next < rtf.Length && rtf[next] != '\\' && rtf[next] != '{' && rtf[next] != '}')
                    next++;
                break;
        }

        return next;
    }

    private static (string Word, int Next) ReadControlWord(string rtf, int i)
    {
        var start = i;
        while (i < rtf.Length && char.IsLetter(rtf[i]))
            i++;
        return (rtf[start..i], i);
    }

    private static (int? Number, int Next) ReadOptionalNumber(string rtf, int i)
    {
        if (i >= rtf.Length)
            return (null, i);
        var start = i;
        if (rtf[i] == '-')
            i++;
        var digitStart = i;
        while (i < rtf.Length && char.IsDigit(rtf[i]))
            i++;
        if (i == digitStart)
            return (null, start);
        return (int.Parse(rtf[start..i], CultureInfo.InvariantCulture), i);
    }

    private static string CollapseWhitespace(string text)
    {
        var lines = text
            .Replace('\u00a0', ' ')
            .Split('\n')
            .Select(line => line.Trim())
            .ToList();
        while (lines.Count > 0 && lines[0].Length == 0)
            lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        var collapsed = new StringBuilder();
        var blank = false;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                if (blank)
                    continue;
                collapsed.AppendLine();
                blank = true;
                continue;
            }

            blank = false;
            collapsed.AppendLine(line);
        }

        return collapsed.ToString().Trim();
    }
}
