using System.Text;
using System.Text.RegularExpressions;

namespace ShowroomBot.Core.Scenarios;

public sealed record CodeRoutine(string Name, int StartLine, int EndLine, int CodeLines);

/// <summary>Line numbers are one-based, including comments, directives and blank lines.</summary>
public static class OneCCode
{
    private static readonly Regex Start = new(@"^\s*(Процедура|Функция|Procedure|Function)\s+([\p{L}_][\p{L}\p{Nd}_]*)\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex End = new(@"^\s*(КонецПроцедуры|КонецФункции|EndProcedure|EndFunction)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<CodeRoutine> Parse(string text)
    {
        var result = new List<CodeRoutine>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        (string Name, int Line, bool Function)? current = null;
        var codeLines = 0;
        var inString = false;
        for (var index = 0; index < lines.Length; index++)
        {
            // Strip comments and strings, preserving multi-line BSL strings and escaped quotes.
            var code = new StringBuilder();
            var hasString = inString;
            var beganInString = inString;
            for (var c = 0; c < lines[index].Length; c++)
            {
                var ch = lines[index][c];
                if (ch == '"')
                {
                    hasString = true;
                    if (inString && c + 1 < lines[index].Length && lines[index][c + 1] == '"') { c++; continue; }
                    inString = !inString;
                    code.Append(' ');
                }
                else if (!inString)
                {
                    if (ch == '/' && c + 1 < lines[index].Length && lines[index][c + 1] == '/') break;
                    code.Append(ch);
                }
            }
            var value = code.ToString();
            var start = beganInString ? Match.Empty : Start.Match(value);
            if (start.Success)
            {
                current = (start.Groups[2].Value, index + 1,
                    start.Groups[1].Value.Equals("Функция", StringComparison.OrdinalIgnoreCase) ||
                    start.Groups[1].Value.Equals("Function", StringComparison.OrdinalIgnoreCase));
                codeLines = 0;
            }
            else if (!beganInString && End.Match(value) is { Success: true } end)
            {
                var isFunction = end.Groups[1].Value.Equals("КонецФункции", StringComparison.OrdinalIgnoreCase) ||
                    end.Groups[1].Value.Equals("EndFunction", StringComparison.OrdinalIgnoreCase);
                if (current is { } routine && routine.Function == isFunction)
                    result.Add(new(routine.Name, routine.Line, index + 1, codeLines));
                current = null;
            }
            else if (current != null && (hasString || (!string.IsNullOrWhiteSpace(value) && !value.TrimStart().StartsWith('#') && !value.TrimStart().StartsWith('&'))))
                codeLines++;
        }
        return result;
    }

    public static IReadOnlyList<CodeRoutine> Select(IReadOnlyList<CodeRoutine> routines, Random random) =>
        routines.Count <= 3 ? routines.ToArray() : routines.OrderBy(_ => random.Next()).Take(random.Next(1, 4)).ToArray();
}
