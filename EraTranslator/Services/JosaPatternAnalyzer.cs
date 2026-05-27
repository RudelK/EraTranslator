using System.Text.RegularExpressions;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed partial class JosaPatternAnalyzer
{
    private readonly InlineSymbolReferenceRewriter _inlineSymbolReferenceRewriter = new();

    private static readonly IReadOnlyDictionary<string, (string macroSuffix, string functionParticle)> ParticleMappings =
        new Dictionary<string, (string macroSuffix, string functionParticle)>(StringComparer.Ordinal)
        {
            ["는"] = ("는", "는"),
            ["은"] = ("는", "는"),
            ["은/는"] = ("는", "는"),
            ["는/은"] = ("는", "는"),
            ["(은)는"] = ("는", "는"),
            ["은(는)"] = ("는", "는"),
            ["는(은)"] = ("는", "는"),
            ["가"] = ("가", "이"),
            ["이"] = ("가", "이"),
            ["이/가"] = ("가", "이"),
            ["가/이"] = ("가", "이"),
            ["(이)가"] = ("가", "이"),
            ["이(가)"] = ("가", "이"),
            ["가(이)"] = ("가", "이"),
            ["를"] = ("를", "을"),
            ["을"] = ("를", "을"),
            ["을/를"] = ("를", "을"),
            ["를/을"] = ("를", "을"),
            ["(을)를"] = ("를", "을"),
            ["을(를)"] = ("를", "을"),
            ["를(을)"] = ("를", "을"),
            ["와"] = ("와", "과"),
            ["과"] = ("와", "과"),
            ["와/과"] = ("와", "과"),
            ["과/와"] = ("와", "과"),
            ["(와)과"] = ("와", "과"),
            ["와(과)"] = ("와", "과"),
            ["과(와)"] = ("와", "과"),
            ["로"] = ("로", "로"),
            ["으로"] = ("로", "으로"),
            ["으로/로"] = ("로", "으로"),
            ["로/으로"] = ("로", "으로"),
            ["(로)으로"] = ("로", "으로"),
            ["로(으로)"] = ("로", "으로"),
            ["으로(로)"] = ("로", "으로"),
            ["랑"] = ("랑", "랑"),
            ["이랑"] = ("랑", "랑"),
            ["이랑/랑"] = ("랑", "랑"),
            ["랑/이랑"] = ("랑", "랑"),
            ["(랑)이랑"] = ("랑", "랑"),
            ["랑(이랑)"] = ("랑", "랑"),
            ["이랑(랑)"] = ("랑", "랑"),
            ["며"] = ("며", "며"),
            ["이며"] = ("며", "며"),
            ["이며/며"] = ("며", "며"),
            ["며/이며"] = ("며", "며"),
            ["(며)이며"] = ("며", "며"),
            ["며(이며)"] = ("며", "며"),
            ["이며(며)"] = ("며", "며"),
            ["고"] = ("고", "고"),
            ["이고"] = ("고", "고"),
            ["이고/고"] = ("고", "고"),
            ["고/이고"] = ("고", "고"),
            ["(고)이고"] = ("고", "고"),
            ["고(이고)"] = ("고", "고"),
            ["이고(고)"] = ("고", "고"),
            ["라"] = ("라", "라"),
            ["이라"] = ("라", "라"),
            ["이라/라"] = ("라", "라"),
            ["라/이라"] = ("라", "라"),
            ["(라)이라"] = ("라", "라"),
            ["라(이라)"] = ("라", "라"),
            ["이라(라)"] = ("라", "라"),
            ["다"] = ("다", "다"),
            ["이다"] = ("다", "다"),
            ["이다/다"] = ("다", "다"),
            ["다/이다"] = ("다", "다"),
            ["(다)이다"] = ("다", "다"),
            ["다(이다)"] = ("다", "다"),
            ["이다(다)"] = ("다", "다"),
            ["였"] = ("였", "였"),
            ["이였"] = ("였", "였"),
            ["이었"] = ("였", "였"),
            ["이었/였"] = ("였", "였"),
            ["였/이었"] = ("였", "였"),
            ["(였)이었"] = ("였", "였"),
            ["였(이었)"] = ("였", "였"),
            ["이었(였)"] = ("였", "였"),
            ["여"] = ("여", "여"),
            ["이여"] = ("여", "여"),
            ["이여/여"] = ("여", "여"),
            ["여/이여"] = ("여", "여"),
            ["(여)이여"] = ("여", "여"),
            ["여(이여)"] = ("여", "여"),
            ["이여(여)"] = ("여", "여"),
            ["야"] = ("야", "야"),
            ["이야"] = ("야", "야"),
            ["이야/야"] = ("야", "야"),
            ["야/이야"] = ("야", "야"),
            ["(야)이야"] = ("야", "야"),
            ["야(이야)"] = ("야", "야"),
            ["이야(야)"] = ("야", "야"),
            ["나"] = ("나", "이나"),
            ["이나"] = ("나", "이나"),
            ["이나/나"] = ("나", "이나"),
            ["나/이나"] = ("나", "이나"),
            ["(나)이나"] = ("나", "이나"),
            ["나(이나)"] = ("나", "이나"),
            ["이나(나)"] = ("나", "이나"),
            ["면"] = ("면", "이면"),
            ["이면"] = ("면", "이면"),
            ["이면/면"] = ("면", "이면"),
            ["면/이면"] = ("면", "이면"),
            ["(면)이면"] = ("면", "이면"),
            ["면(이면)"] = ("면", "이면"),
            ["이면(면)"] = ("면", "이면"),
            ["의"] = ("의", "의"),
            ["에게"] = ("에게", "에게"),
        };

    public JosaDocumentAnalysis AnalyzeDocument(string content, JosaSupportPackageInfo packageInfo)
    {
        var occurrences = CollectOccurrences(content, new Dictionary<(string Namespace, string OriginalKey), string>());
        return BuildAnalysis(occurrences, packageInfo);
    }

    public JosaRewriteTextResult RewriteText(
        string text,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap,
        JosaSupportPackageInfo packageInfo)
    {
        var occurrences = CollectOccurrences(text, renameMap);
        if (occurrences.Count == 0)
        {
            return new JosaRewriteTextResult(text, BuildAnalysis([], packageInfo), false);
        }

        var buffer = text;
        foreach (var occurrence in occurrences
                     .Where(occurrence => !string.Equals(occurrence.Replacement, occurrence.OriginalText, StringComparison.Ordinal))
                     .OrderByDescending(occurrence => occurrence.Start))
        {
            buffer = buffer.Remove(occurrence.Start, occurrence.Length)
                .Insert(occurrence.Start, occurrence.Replacement);
        }

        var analysis = BuildAnalysis(occurrences, packageInfo);
        return new JosaRewriteTextResult(buffer, analysis, occurrences.Any(occurrence => occurrence.RequiresErh));
    }

    public string RewriteLeadingSplitParticle(string previousText, string currentText)
    {
        if (string.IsNullOrWhiteSpace(previousText) || string.IsNullOrWhiteSpace(currentText))
        {
            return currentText;
        }

        var previousMatch = TrailingJosaExpressionPattern().Match(previousText);
        if (!previousMatch.Success)
        {
            return currentText;
        }

        var leadingWhitespaceLength = currentText.Length - currentText.TrimStart().Length;
        var leadingWhitespace = currentText[..leadingWhitespaceLength];
        var trimmedCurrent = currentText[leadingWhitespaceLength..];
        if (!TryMatchLeadingParticle(trimmedCurrent, out var particleText, out var contentStart))
        {
            return currentText;
        }

        if (!TryNormalizeParticle(particleText, out var particle))
        {
            return currentText;
        }

        if (IsPassThroughParticle(particle))
        {
            return currentText;
        }

        var expression = NormalizeWhitespace(previousMatch.Groups["expr"].Value);
        var rewritten = $"%조사만처리({expression},\"{particle.functionParticle}\")%{trimmedCurrent[contentStart..]}";
        return leadingWhitespace + rewritten;
    }

    public IReadOnlyList<PlannedTextReplacement> CreateDocumentReplacements(
        SourceFileDocument document,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap,
        JosaSupportPackageInfo packageInfo)
    {
        var occurrences = CollectOccurrences(document.OriginalText, renameMap);
        return occurrences
            .Where(occurrence => !string.Equals(occurrence.Replacement, occurrence.OriginalText, StringComparison.Ordinal))
            .Select(occurrence => new PlannedTextReplacement(occurrence.Start, occurrence.Length, occurrence.Replacement))
            .OrderByDescending(replacement => replacement.Start)
            .ToList();
    }

    private static JosaDocumentAnalysis BuildAnalysis(
        IReadOnlyCollection<JosaOccurrence> occurrences,
        JosaSupportPackageInfo packageInfo)
    {
        var macroCount = occurrences.Count(occurrence => occurrence.OutputKind == JosaOutputKind.Macro);
        var genericCount = occurrences.Count(occurrence => occurrence.OutputKind == JosaOutputKind.GenericFunction);
        var legacyCount = occurrences.Count(occurrence => occurrence.InputKind == JosaInputKind.LegacyShorthand);
        var requiresErh = macroCount > 0;

        var syntaxKinds = new List<string>();
        if (macroCount > 0)
        {
            syntaxKinds.Add("매크로");
        }

        if (genericCount > 0)
        {
            syntaxKinds.Add("범용 함수");
        }

        if (legacyCount > 0)
        {
            syntaxKinds.Add("구형 축약");
        }

        var syntaxType = syntaxKinds.Count switch
        {
            0 => "없음",
            1 => syntaxKinds[0],
            _ => "혼합",
        };

        return new JosaDocumentAnalysis
        {
            PatternCount = occurrences.Count,
            AutoConvertibleCount = occurrences.Count(occurrence => occurrence.AutoConvertible),
            GenericFunctionCount = genericCount,
            MacroPatternCount = macroCount,
            LegacyShorthandCount = legacyCount,
            RequiresErh = requiresErh,
            ErhLinked = packageInfo.HasErhIncludeLinkage,
            SyntaxType = syntaxType,
            ErhLinkStatus = requiresErh
                ? (packageInfo.HasErhIncludeLinkage ? "연결됨" : "연결 확인 필요")
                : "불필요",
            PackageCompatibilityStatus = packageInfo.CompatibilityStatus,
        };
    }

    private List<JosaOccurrence> CollectOccurrences(
        string text,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap)
    {
        var raw = new List<JosaOccurrence>();
        raw.AddRange(CollectPostfixPairs(text, renameMap));
        raw.AddRange(CollectGenericFunctions(text, renameMap));
        raw.AddRange(CollectLegacyShorthand(text, renameMap));
        raw.AddRange(CollectMacroForms(text, renameMap));

        return raw
            .OrderBy(occurrence => occurrence.Start)
            .ThenByDescending(occurrence => occurrence.Length)
            .Aggregate(new List<JosaOccurrence>(), (accepted, occurrence) =>
            {
                if (accepted.Any(existing => RangesOverlap(existing.Start, existing.Length, occurrence.Start, occurrence.Length)))
                {
                    return accepted;
                }

                accepted.Add(occurrence);
                return accepted;
            });
    }

    private IEnumerable<JosaOccurrence> CollectGenericFunctions(
        string text,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap)
    {
        foreach (Match match in GenericFunctionPattern().Matches(text))
        {
            var functionName = match.Groups["func"].Value;
            var expression = NormalizeWhitespace(match.Groups["expr"].Value);
            var particleText = match.Groups["particle"].Value;
            if (!TryNormalizeParticle(particleText, out var particle))
            {
                continue;
            }

            var rewrittenExpression = _inlineSymbolReferenceRewriter.Rewrite(expression, renameMap);
            var replacement = BuildReplacement(functionName, rewrittenExpression, particle);
            yield return new JosaOccurrence(
                match.Index,
                match.Length,
                match.Value,
                replacement,
                JosaInputKind.GenericFunction,
                replacement.StartsWith("%조사", StringComparison.Ordinal) ? JosaOutputKind.GenericFunction : JosaOutputKind.Macro,
                !string.Equals(match.Value, replacement, StringComparison.Ordinal),
                replacement.StartsWith("%조사", StringComparison.Ordinal) is false);
        }
    }

    private IEnumerable<JosaOccurrence> CollectLegacyShorthand(
        string text,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap)
    {
        foreach (Match match in LegacyShorthandPattern().Matches(text))
        {
            var helperBase = match.Groups["base"].Value;
            var particleText = match.Groups["particle"].Value;
            var argText = match.Groups["arg"].Value.Trim();
            if (!TryNormalizeParticle(particleText, out var particle))
            {
                continue;
            }

            var replacement = BuildLegacyReplacement(helperBase, particle, argText, renameMap);
            if (replacement.Length == 0)
            {
                continue;
            }

            yield return new JosaOccurrence(
                match.Index,
                match.Length,
                match.Value,
                replacement,
                JosaInputKind.LegacyShorthand,
                replacement.StartsWith("%조사", StringComparison.Ordinal) ? JosaOutputKind.GenericFunction : JosaOutputKind.Macro,
                true,
                replacement.StartsWith("%조사", StringComparison.Ordinal) is false);
        }
    }

    private IEnumerable<JosaOccurrence> CollectMacroForms(
        string text,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap)
    {
        foreach (Match match in MacroPattern().Matches(text))
        {
            var macroBase = match.Groups["base"].Value;
            var particleText = match.Groups["particle"].Value;
            if (!TryNormalizeParticle(particleText, out var particle))
            {
                continue;
            }

            var canonicalBase = NormalizeMacroBase(macroBase);
            var replacement = $"%{canonicalBase}{particle.macroSuffix}%";
            yield return new JosaOccurrence(
                match.Index,
                match.Length,
                match.Value,
                replacement,
                JosaInputKind.Macro,
                JosaOutputKind.Macro,
                !string.Equals(match.Value, replacement, StringComparison.Ordinal),
                true);
        }
    }

    private IEnumerable<JosaOccurrence> CollectPostfixPairs(
        string text,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap)
    {
        foreach (Match match in PostfixPairPattern().Matches(text))
        {
            var expression = NormalizeWhitespace(match.Groups["expr"].Value);
            var particleText = match.Groups["particle"].Value;
            if (!TryNormalizeParticle(particleText, out var particle))
            {
                continue;
            }

            if (IsPassThroughParticle(particle))
            {
                continue;
            }

            var rewrittenExpression = _inlineSymbolReferenceRewriter.Rewrite(expression, renameMap);
            var replacement = BuildMacroOrGenericFromExpression(rewrittenExpression, particle);
            yield return new JosaOccurrence(
                match.Index,
                match.Length,
                match.Value,
                replacement,
                JosaInputKind.PostfixPair,
                replacement.StartsWith("%조사", StringComparison.Ordinal) ? JosaOutputKind.GenericFunction : JosaOutputKind.Macro,
                true,
                replacement.StartsWith("%조사", StringComparison.Ordinal) is false);
        }
    }

    private string BuildReplacement(string functionName, string expression, (string macroSuffix, string functionParticle) particle)
    {
        if (string.Equals(functionName, "조사만처리", StringComparison.Ordinal))
        {
            return $"%조사만처리({expression},\"{particle.functionParticle}\")%";
        }

        return BuildMacroOrGenericFromExpression(expression, particle);
    }

    private string BuildLegacyReplacement(
        string helperBase,
        (string macroSuffix, string functionParticle) particle,
        string argText,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap)
    {
        var canonicalBase = NormalizeMacroBase(helperBase);
        if (string.IsNullOrWhiteSpace(argText))
        {
            return $"%{canonicalBase}{particle.macroSuffix}%";
        }

        var expression = canonicalBase switch
        {
            "플레이어" => "NAME:MASTER",
            "타겟" => "NAME:TARGET",
            "조교자" => "NAME:PLAYER",
            "조수" => "NAME:ASSI",
            "ARG" => "NAME:ARG",
            _ => string.Empty,
        };

        if (expression.Length == 0)
        {
            return string.Empty;
        }

        expression = _inlineSymbolReferenceRewriter.Rewrite(expression, renameMap);
        return $"%조사처리({expression},\"{particle.functionParticle}\")%";
    }

    private string BuildMacroOrGenericFromExpression(string expression, (string macroSuffix, string functionParticle) particle)
    {
        var canonicalExpression = NormalizeExpression(expression);
        return canonicalExpression switch
        {
            "CALLNAME" or "CALLNAME:TARGET" => $"%타겟{particle.macroSuffix}%",
            "CALLNAME:MASTER" => $"%플레이어{particle.macroSuffix}%",
            "CALLNAME:PLAYER" => $"%조교자{particle.macroSuffix}%",
            "CALLNAME:ARG" => $"%ARG{particle.macroSuffix}%",
            "CALLNAME:ASSI" => $"%조수{particle.macroSuffix}%",
            _ => $"%조사처리({expression},\"{particle.functionParticle}\")%",
        };
    }

    private static string NormalizeExpression(string expression)
    {
        return Regex.Replace(expression, @"\s+", string.Empty);
    }

    private static string NormalizeWhitespace(string value)
    {
        return value.Trim();
    }

    private static string NormalizeMacroBase(string macroBase)
    {
        return macroBase switch
        {
            "플레이어" or "마스터" or "MASTER" => "플레이어",
            "타겟" or "TARGET" => "타겟",
            "조교자" => "조교자",
            "조수" => "조수",
            "ARG" => "ARG",
            _ => macroBase,
        };
    }

    private static bool TryNormalizeParticle(string text, out (string macroSuffix, string functionParticle) particle)
    {
        return ParticleMappings.TryGetValue(text.Trim(), out particle);
    }

    private static bool IsPassThroughParticle((string macroSuffix, string functionParticle) particle)
    {
        return particle.functionParticle is "의" or "에게";
    }

    private static bool TryMatchLeadingParticle(string text, out string particleText, out int contentStart)
    {
        particleText = string.Empty;
        contentStart = 0;

        foreach (var key in ParticleMappings.Keys.OrderByDescending(key => key.Length))
        {
            if (!text.StartsWith(key, StringComparison.Ordinal))
            {
                continue;
            }

            var nextIndex = key.Length;
            if (nextIndex < text.Length && !char.IsWhiteSpace(text[nextIndex]) && !IsJosaBoundary(text[nextIndex]))
            {
                continue;
            }

            particleText = key;
            contentStart = nextIndex;
            return true;
        }

        return false;
    }

    private static bool IsJosaBoundary(char character)
    {
        return character is '.' or ',' or '!' or '?' or ';' or ':' or ')' or ']' or '}' or '>' or '"' or '\'' or '…' or '。' or '、';
    }

    private static bool RangesOverlap(int leftStart, int leftLength, int rightStart, int rightLength)
    {
        var leftEnd = leftStart + leftLength;
        var rightEnd = rightStart + rightLength;
        return leftStart < rightEnd && rightStart < leftEnd;
    }

    [GeneratedRegex("""%(?<func>조사처리|조사만처리)\((?<expr>.+?),"(?<particle>[^"]+)"\)%""", RegexOptions.Compiled | RegexOptions.Singleline)]
    private static partial Regex GenericFunctionPattern();

    [GeneratedRegex("""%(?<base>플레이어|마스터|MASTER|타겟|TARGET|조교자|조수|ARG)(?<particle>으로|에게|이랑|이며|이고|이라|이다|이었|이였|이여|이야|이나|이면|은|는|이|가|을|를|와|과|로|랑|며|고|라|다|였|여|야|나|면|의)\((?<arg>[^)]*)\)%""", RegexOptions.Compiled)]
    private static partial Regex LegacyShorthandPattern();

    [GeneratedRegex("""%(?<base>플레이어|마스터|MASTER|타겟|TARGET|조교자|조수|ARG)(?<particle>으로|에게|이랑|이며|이고|이라|이다|이었|이였|이여|이야|이나|이면|은|는|이|가|을|를|와|과|로|랑|며|고|라|다|였|여|야|나|면|의)%""", RegexOptions.Compiled)]
    private static partial Regex MacroPattern();

    [GeneratedRegex("""%(?<expr>CALLNAME(?:\s*:\s*[^%]+)?|NAME\s*:\s*[^%]+|~)%(?<particle>은/는|는/은|\(은\)는|은\(는\)|는\(은\)|이/가|가/이|\(이\)가|이\(가\)|가\(이\)|을/를|를/을|\(을\)를|을\(를\)|를\(을\)|와/과|과/와|\(와\)과|와\(과\)|과\(와\)|으로/로|로/으로|\(로\)으로|로\(으로\)|으로\(로\)|이랑/랑|랑/이랑|\(랑\)이랑|랑\(이랑\)|이랑\(랑\)|이며/며|며/이며|\(며\)이며|며\(이며\)|이며\(며\)|이고/고|고/이고|\(고\)이고|고\(이고\)|이고\(고\)|이라/라|라/이라|\(라\)이라|라\(이라\)|이라\(라\)|이다/다|다/이다|\(다\)이다|다\(이다\)|이다\(다\)|이었/였|였/이었|\(였\)이었|였\(이었\)|이었\(였\)|이여/여|여/이여|\(여\)이여|여\(이여\)|이여\(여\)|이야/야|야/이야|\(야\)이야|야\(이야\)|이야\(야\)|이나/나|나/이나|\(나\)이나|나\(이나\)|이나\(나\)|이면/면|면/이면|\(면\)이면|면\(이면\)|이면\(면\)|으로|에게|이랑|이며|이고|이라|이다|이었|이였|이여|이야|이나|이면|은|는|이|가|을|를|와|과|로|랑|며|고|라|다|였|여|야|나|면|의)""", RegexOptions.Compiled)]
    private static partial Regex PostfixPairPattern();

    [GeneratedRegex("""%(?<expr>CALLNAME(?:\s*:\s*[^%]+)?|NAME\s*:\s*[^%]+|~)%\s*$""", RegexOptions.Compiled)]
    private static partial Regex TrailingJosaExpressionPattern();

    private enum JosaInputKind
    {
        GenericFunction,
        LegacyShorthand,
        Macro,
        PostfixPair,
    }

    private enum JosaOutputKind
    {
        Macro,
        GenericFunction,
    }

    private sealed record JosaOccurrence(
        int Start,
        int Length,
        string OriginalText,
        string Replacement,
        JosaInputKind InputKind,
        JosaOutputKind OutputKind,
        bool AutoConvertible,
        bool RequiresErh);
}

public readonly record struct JosaRewriteTextResult(
    string Text,
    JosaDocumentAnalysis Analysis,
    bool RequiresErh);
