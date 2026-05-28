using System.Text.RegularExpressions;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed class JosaPatternAnalyzer
{
    private static readonly string[] MacroBases = ["플레이어", "마스터", "MASTER", "타겟", "TARGET", "조교자", "조수", "ARG"];
    private static readonly string[] GenericFunctionNames = ["조사처리", "조사만처리", "조사선택", "조사만선택"];
    private static readonly IReadOnlyList<JosaRule> Rules = CreateRules();
    private static readonly IReadOnlyDictionary<string, JosaRule> RuleLookup = CreateRuleLookup(Rules);
    private static readonly string[] SortedParticleInputs = RuleLookup.Keys
        .OrderByDescending(static key => key.Length)
        .ToArray();
    private static readonly string[] SortedCompositeParticleInputs = SortedParticleInputs
        .Where(static key => key.Contains('/') || key.Contains('(') || key.Contains(')'))
        .ToArray();
    private static readonly string[] SortedPlainParticleInputs = SortedParticleInputs
        .Where(static key => !key.Contains('/') && !key.Contains('(') && !key.Contains(')'))
        .ToArray();
    private static readonly string[] SortedMacroMatchParticles = Rules
        .SelectMany(static rule => rule.MacroMatchParticles)
        .Distinct(StringComparer.Ordinal)
        .OrderByDescending(static particle => particle.Length)
        .ToArray();
    private static readonly Regex GenericFunctionRegex = new(
        $@"%(?<func>{BuildAlternation(GenericFunctionNames)})\((?<expr>.+?)," + "\"(?<particle>[^\"]+)\"" + @"\)%",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex LegacyShorthandRegex = new(
        $@"%(?<base>{BuildAlternation(MacroBases)})(?<particle>{BuildAlternation(SortedMacroMatchParticles)})\((?<arg>[^)]*)\)%",
        RegexOptions.Compiled);
    private static readonly Regex MacroRegex = new(
        $@"%(?<base>{BuildAlternation(MacroBases)})(?<particle>{BuildAlternation(SortedMacroMatchParticles)})%",
        RegexOptions.Compiled);
    private static readonly Regex PostfixPairRegex = new(
        $@"%(?<expr>CALLNAME(?:\s*:\s*[^%]+)?|NAME\s*:\s*[^%]+|~)%(?<particle>{BuildAlternation(SortedParticleInputs)})",
        RegexOptions.Compiled);
    private static readonly Regex TrailingJosaExpressionRegex = new(
        """%(?<expr>CALLNAME(?:\s*:\s*[^%]+)?|NAME\s*:\s*[^%]+|~)%\s*$""",
        RegexOptions.Compiled);
    private static readonly Regex CompositeLiteralParticleRegex = new(
        $@"(?<![\p{{L}}\p{{Nd}}_])(?<token>[가-힣0-9]+)(?<particle>{BuildAlternation(SortedCompositeParticleInputs)})(?=$|[\s\.,!?\;:\)\]\}}>""'…。、」』）])",
        RegexOptions.Compiled);
    private static readonly Regex PlainLiteralWordRegex = new(
        @"(?<![\p{L}\p{Nd}_])(?<word>[가-힣0-9]+)(?=$|[\s\.,!?\;:\)\]\}}>""'…。、」』）])",
        RegexOptions.Compiled);
    private static readonly Regex TrailingLiteralTokenRegex = new(
        @"(?<![\p{L}\p{Nd}_])(?<token>[가-힣0-9]+)\s*$",
        RegexOptions.Compiled);

    private readonly InlineSymbolReferenceRewriter _inlineSymbolReferenceRewriter = new();
    private readonly PlaceholderProtector _placeholderProtector = new();

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
        var normalized = ApplyJosaOccurrences(text, occurrences);
        normalized = RewriteLiteralParticles(normalized);
        var analysis = BuildAnalysis(occurrences, packageInfo);
        return new JosaRewriteTextResult(normalized, analysis, occurrences.Any(static occurrence => occurrence.RequiresErh));
    }

    public string RewriteLeadingSplitParticle(string previousText, string currentText)
    {
        if (string.IsNullOrWhiteSpace(previousText) || string.IsNullOrWhiteSpace(currentText))
        {
            return currentText;
        }

        var leadingWhitespaceLength = currentText.Length - currentText.TrimStart().Length;
        var leadingWhitespace = currentText[..leadingWhitespaceLength];
        var trimmedCurrent = currentText[leadingWhitespaceLength..];
        if (!TryMatchLeadingParticle(trimmedCurrent, out var inputParticle, out var contentStart))
        {
            return currentText;
        }

        if (!TryNormalizeParticle(inputParticle, out var rule) || rule.PassThrough)
        {
            return currentText;
        }

        var previousExpressionMatch = TrailingJosaExpressionRegex.Match(previousText);
        if (previousExpressionMatch.Success)
        {
            var expression = NormalizeWhitespace(previousExpressionMatch.Groups["expr"].Value);
            var rewritten = $"%조사만처리({expression},\"{rule.FunctionParticle}\")%{trimmedCurrent[contentStart..]}";
            return leadingWhitespace + rewritten;
        }

        if (!TryGetTrailingLiteralToken(previousText, out var trailingToken))
        {
            return currentText;
        }

        var surface = rule.GetLiteralSurface(KoreanParticleClassifier.ClassifyToken(trailingToken));
        return leadingWhitespace + surface + trimmedCurrent[contentStart..];
    }

    public IReadOnlyList<PlannedTextReplacement> CreateDocumentReplacements(
        SourceFileDocument document,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap,
        JosaSupportPackageInfo packageInfo)
    {
        var occurrences = CollectOccurrences(document.OriginalText, renameMap);
        return occurrences
            .Where(static occurrence => !string.Equals(occurrence.Replacement, occurrence.OriginalText, StringComparison.Ordinal))
            .Select(static occurrence => new PlannedTextReplacement(occurrence.Start, occurrence.Length, occurrence.Replacement))
            .OrderByDescending(static replacement => replacement.Start)
            .ToList();
    }

    private static JosaDocumentAnalysis BuildAnalysis(
        IReadOnlyCollection<JosaOccurrence> occurrences,
        JosaSupportPackageInfo packageInfo)
    {
        var macroCount = occurrences.Count(static occurrence => occurrence.OutputKind == JosaOutputKind.Macro);
        var genericCount = occurrences.Count(static occurrence => occurrence.OutputKind == JosaOutputKind.GenericFunction);
        var legacyCount = occurrences.Count(static occurrence => occurrence.InputKind == JosaInputKind.LegacyShorthand);
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
            AutoConvertibleCount = occurrences.Count(static occurrence => occurrence.AutoConvertible),
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
            .OrderBy(static occurrence => occurrence.Start)
            .ThenByDescending(static occurrence => occurrence.Length)
            .Aggregate(
                new List<JosaOccurrence>(),
                static (accepted, occurrence) =>
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
        foreach (Match match in GenericFunctionRegex.Matches(text))
        {
            var functionName = match.Groups["func"].Value;
            var expression = NormalizeWhitespace(match.Groups["expr"].Value);
            var particleText = match.Groups["particle"].Value;
            if (!TryNormalizeParticle(particleText, out var rule))
            {
                continue;
            }

            var rewrittenExpression = _inlineSymbolReferenceRewriter.Rewrite(expression, renameMap);
            var replacement = BuildReplacement(functionName, rewrittenExpression, rule);
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
        foreach (Match match in LegacyShorthandRegex.Matches(text))
        {
            var helperBase = match.Groups["base"].Value;
            var particleText = match.Groups["particle"].Value;
            var argText = match.Groups["arg"].Value.Trim();
            if (!TryNormalizeParticle(particleText, out var rule))
            {
                continue;
            }

            var replacement = BuildLegacyReplacement(helperBase, rule, argText, renameMap);
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
        foreach (Match match in MacroRegex.Matches(text))
        {
            var macroBase = match.Groups["base"].Value;
            var particleText = match.Groups["particle"].Value;
            if (!TryNormalizeParticle(particleText, out var rule))
            {
                continue;
            }

            var replacement = BuildMacroReplacement(macroBase, rule);
            yield return new JosaOccurrence(
                match.Index,
                match.Length,
                match.Value,
                replacement,
                JosaInputKind.Macro,
                replacement.StartsWith("%조사", StringComparison.Ordinal) ? JosaOutputKind.GenericFunction : JosaOutputKind.Macro,
                !string.Equals(match.Value, replacement, StringComparison.Ordinal),
                replacement.StartsWith("%조사", StringComparison.Ordinal) is false);
        }
    }

    private IEnumerable<JosaOccurrence> CollectPostfixPairs(
        string text,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap)
    {
        foreach (Match match in PostfixPairRegex.Matches(text))
        {
            var expression = NormalizeWhitespace(match.Groups["expr"].Value);
            var particleText = match.Groups["particle"].Value;
            if (!TryNormalizeParticle(particleText, out var rule) || rule.PassThrough)
            {
                continue;
            }

            var rewrittenExpression = _inlineSymbolReferenceRewriter.Rewrite(expression, renameMap);
            var replacement = BuildMacroOrGenericFromExpression(rewrittenExpression, rule);
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

    private string BuildReplacement(string functionName, string expression, JosaRule rule)
    {
        if (string.Equals(functionName, "조사만처리", StringComparison.Ordinal)
            || string.Equals(functionName, "조사만선택", StringComparison.Ordinal))
        {
            return $"%조사만처리({expression},\"{rule.FunctionParticle}\")%";
        }

        return BuildMacroOrGenericFromExpression(expression, rule);
    }

    private string BuildLegacyReplacement(
        string helperBase,
        JosaRule rule,
        string argText,
        IReadOnlyDictionary<(string Namespace, string OriginalKey), string> renameMap)
    {
        var canonicalBase = NormalizeMacroBase(helperBase);
        var hasExplicitArg = !string.IsNullOrWhiteSpace(argText);
        if (!hasExplicitArg && rule.SupportsMacroShorthand)
        {
            return $"%{canonicalBase}{rule.MacroSuffix}%";
        }

        var expression = BuildLegacyExpression(canonicalBase, useNameExpression: hasExplicitArg);
        if (expression.Length == 0)
        {
            return string.Empty;
        }

        expression = _inlineSymbolReferenceRewriter.Rewrite(expression, renameMap);
        return $"%조사처리({expression},\"{rule.FunctionParticle}\")%";
    }

    private string BuildMacroReplacement(string macroBase, JosaRule rule)
    {
        var canonicalBase = NormalizeMacroBase(macroBase);
        if (rule.SupportsMacroShorthand)
        {
            return $"%{canonicalBase}{rule.MacroSuffix}%";
        }

        var expression = BuildLegacyExpression(canonicalBase, useNameExpression: false);
        return expression.Length == 0
            ? $"%{canonicalBase}{rule.MacroSuffix}%"
            : $"%조사처리({expression},\"{rule.FunctionParticle}\")%";
    }

    private string BuildMacroOrGenericFromExpression(string expression, JosaRule rule)
    {
        var canonicalExpression = NormalizeExpression(expression);
        if (!rule.SupportsMacroShorthand)
        {
            return $"%조사처리({expression},\"{rule.FunctionParticle}\")%";
        }

        return canonicalExpression switch
        {
            "CALLNAME" or "CALLNAME:TARGET" => $"%타겟{rule.MacroSuffix}%",
            "CALLNAME:MASTER" => $"%플레이어{rule.MacroSuffix}%",
            "CALLNAME:PLAYER" => $"%조교자{rule.MacroSuffix}%",
            "CALLNAME:ARG" => $"%ARG{rule.MacroSuffix}%",
            "CALLNAME:ASSI" => $"%조수{rule.MacroSuffix}%",
            _ => $"%조사처리({expression},\"{rule.FunctionParticle}\")%",
        };
    }

    private static string ApplyJosaOccurrences(string text, IReadOnlyCollection<JosaOccurrence> occurrences)
    {
        if (occurrences.Count == 0)
        {
            return text;
        }

        var buffer = text;
        foreach (var occurrence in occurrences
                     .Where(static occurrence => !string.Equals(occurrence.Replacement, occurrence.OriginalText, StringComparison.Ordinal))
                     .OrderByDescending(static occurrence => occurrence.Start))
        {
            buffer = buffer.Remove(occurrence.Start, occurrence.Length)
                .Insert(occurrence.Start, occurrence.Replacement);
        }

        return buffer;
    }

    private string RewriteLiteralParticles(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var protectedText = _placeholderProtector.Protect(text);
        var replacements = new List<PlannedTextReplacement>();

        foreach (Match match in CompositeLiteralParticleRegex.Matches(protectedText.Text))
        {
            var token = match.Groups["token"].Value;
            if (!IsSupportedLiteralToken(token))
            {
                continue;
            }

            var particleText = match.Groups["particle"].Value;
            if (!TryNormalizeParticle(particleText, out var rule))
            {
                continue;
            }

            if (!rule.ShouldRewriteLiteral(token, particleText))
            {
                continue;
            }

            var replacement = token + rule.GetLiteralSurface(KoreanParticleClassifier.ClassifyToken(token));
            if (string.Equals(match.Value, replacement, StringComparison.Ordinal))
            {
                continue;
            }

            replacements.Add(new PlannedTextReplacement(match.Index, match.Length, replacement));
        }

        foreach (Match match in PlainLiteralWordRegex.Matches(protectedText.Text))
        {
            if (replacements.Any(existing => RangesOverlap(existing.Start, existing.Length, match.Index, match.Length)))
            {
                continue;
            }

            var word = match.Groups["word"].Value;
            foreach (var particleText in SortedPlainParticleInputs)
            {
                if (!word.EndsWith(particleText, StringComparison.Ordinal) || word.Length <= particleText.Length)
                {
                    continue;
                }

                var token = word[..^particleText.Length];
                if (!IsSupportedLiteralToken(token))
                {
                    continue;
                }

                if (!TryNormalizeParticle(particleText, out var rule) || !rule.ShouldRewriteLiteral(token, particleText))
                {
                    continue;
                }

                var replacement = token + rule.GetLiteralSurface(KoreanParticleClassifier.ClassifyToken(token));
                if (string.Equals(word, replacement, StringComparison.Ordinal))
                {
                    break;
                }

                replacements.Add(new PlannedTextReplacement(match.Index, match.Length, replacement));
                break;
            }
        }

        if (replacements.Count == 0)
        {
            return text;
        }

        var rewritten = protectedText.Text;
        foreach (var replacement in replacements.OrderByDescending(static replacement => replacement.Start))
        {
            rewritten = rewritten.Remove(replacement.Start, replacement.Length)
                .Insert(replacement.Start, replacement.Value);
        }

        return _placeholderProtector.Restore(rewritten, protectedText.Placeholders);
    }

    private static bool IsSupportedLiteralToken(string token)
    {
        return token.Length > 0
            && token.All(KoreanParticleClassifier.IsLiteralTokenCharacter)
            && token.Any(static character => character is >= '\uAC00' and <= '\uD7A3' or >= '0' and <= '9');
    }

    private static bool TryGetTrailingLiteralToken(string text, out string token)
    {
        var match = TrailingLiteralTokenRegex.Match(text);
        if (match.Success)
        {
            token = match.Groups["token"].Value;
            return true;
        }

        token = string.Empty;
        return false;
    }

    private static string BuildLegacyExpression(string canonicalBase, bool useNameExpression)
    {
        return (canonicalBase, useNameExpression) switch
        {
            ("플레이어", false) => "CALLNAME:MASTER",
            ("플레이어", true) => "NAME:MASTER",
            ("타겟", false) => "CALLNAME:TARGET",
            ("타겟", true) => "NAME:TARGET",
            ("조교자", false) => "CALLNAME:PLAYER",
            ("조교자", true) => "NAME:PLAYER",
            ("조수", false) => "CALLNAME:ASSI",
            ("조수", true) => "NAME:ASSI",
            ("ARG", false) => "CALLNAME:ARG",
            ("ARG", true) => "NAME:ARG",
            _ => string.Empty,
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

    private static bool TryNormalizeParticle(string text, out JosaRule rule)
    {
        return RuleLookup.TryGetValue(text.Trim(), out rule!);
    }

    private static bool TryMatchLeadingParticle(string text, out string particleText, out int contentStart)
    {
        particleText = string.Empty;
        contentStart = 0;

        foreach (var input in SortedParticleInputs)
        {
            if (!text.StartsWith(input, StringComparison.Ordinal))
            {
                continue;
            }

            var nextIndex = input.Length;
            if (nextIndex < text.Length && !char.IsWhiteSpace(text[nextIndex]) && !IsJosaBoundary(text[nextIndex]))
            {
                continue;
            }

            particleText = input;
            contentStart = nextIndex;
            return true;
        }

        return false;
    }

    private static bool IsJosaBoundary(char character)
    {
        return character is '.' or ',' or '!' or '?' or ';' or ':' or ')' or ']' or '}' or '>' or '"' or '\'' or '…' or '。' or '、' or '」' or '』' or '）';
    }

    private static bool RangesOverlap(int leftStart, int leftLength, int rightStart, int rightLength)
    {
        var leftEnd = leftStart + leftLength;
        var rightEnd = rightStart + rightLength;
        return leftStart < rightEnd && rightStart < leftEnd;
    }

    private static string BuildAlternation(IEnumerable<string> values)
    {
        return string.Join("|", values.Select(Regex.Escape));
    }

    private static IReadOnlyDictionary<string, JosaRule> CreateRuleLookup(IEnumerable<JosaRule> rules)
    {
        var lookup = new Dictionary<string, JosaRule>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            foreach (var input in rule.AllAcceptedInputs)
            {
                lookup[input] = rule;
            }
        }

        return lookup;
    }

    private static IReadOnlyList<JosaRule> CreateRules()
    {
        return
        [
            new JosaRule("topic", "는", "은", "는", "는", supportsMacroShorthand: true),
            new JosaRule("subject", "가", "이", "가", "이", supportsMacroShorthand: true),
            new JosaRule("object", "를", "을", "를", "을", supportsMacroShorthand: true),
            new JosaRule("comitative", "와", "과", "와", "과", supportsMacroShorthand: true),
            new JosaRule("direction", "로", "으로", "로", "으로", supportsMacroShorthand: true, rieulSurface: "로"),
            new JosaRule("companion", "랑", "이랑", "랑", "랑", supportsMacroShorthand: true),
            new JosaRule("connective", "며", "이며", "며", "며", supportsMacroShorthand: true, literalRewriteMode: LiteralRewriteMode.LongOrCompositeOnly),
            new JosaRule("conjunctive", "고", "이고", "고", "고", supportsMacroShorthand: true, literalRewriteMode: LiteralRewriteMode.LongOrCompositeOnly),
            new JosaRule("predicative", "라", "이라", "라", "라", supportsMacroShorthand: true, literalRewriteMode: LiteralRewriteMode.LongOrCompositeOnly),
            new JosaRule("copula", "다", "이다", "다", "다", supportsMacroShorthand: true, literalRewriteMode: LiteralRewriteMode.LongOrCompositeOnly),
            new JosaRule("pastCopula", "였", "이었", "였", "였", supportsMacroShorthand: true, literalRewriteMode: LiteralRewriteMode.LongOrCompositeOnly, batchimInputs: ["이었", "이였"]),
            new JosaRule("endingYeo", "여", "이여", "여", "여", supportsMacroShorthand: true, literalRewriteMode: LiteralRewriteMode.LongOrCompositeOnly),
            new JosaRule("endingYa", "야", "이야", "야", "야", supportsMacroShorthand: true, literalRewriteMode: LiteralRewriteMode.LongOrCompositeOnly),
            new JosaRule("orChoice", "나", "이나", "나", "이나", supportsMacroShorthand: true, literalRewriteMode: LiteralRewriteMode.LongOrCompositeOnly),
            new JosaRule("conditional", "면", "이면", "면", "이면", supportsMacroShorthand: true, literalRewriteMode: LiteralRewriteMode.LongOrCompositeOnly),
            new JosaRule("future", "겠", "이겠", "겠", "겠", supportsMacroShorthand: false, literalRewriteMode: LiteralRewriteMode.LongOrCompositeOnly),
            new JosaRule("honorificPast", "셨", "이셨", "셨", "셨", supportsMacroShorthand: false, literalRewriteMode: LiteralRewriteMode.LongOrCompositeOnly),
            new JosaRule("emphatic", "잖", "이잖", "잖", "잖", supportsMacroShorthand: false, literalRewriteMode: LiteralRewriteMode.LongOrCompositeOnly),
            new JosaRule("interrogative", "니", "이니", "니", "니", supportsMacroShorthand: false, literalRewriteMode: LiteralRewriteMode.LongOrCompositeOnly),
            new JosaRule("genitive", "의", "의", "의", "의", passThrough: true, supportsMacroShorthand: true),
            new JosaRule("dative", "에게", "에게", "에게", "에게", passThrough: true, supportsMacroShorthand: true),
        ];
    }

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

    private sealed class JosaRule
    {
        private readonly string[] _noBatchimInputs;
        private readonly string[] _batchimInputs;

        public JosaRule(
            string key,
            string noBatchimSurface,
            string batchimSurface,
            string macroSuffix,
            string functionParticle,
            bool supportsMacroShorthand,
            bool passThrough = false,
            string? rieulSurface = null,
            LiteralRewriteMode literalRewriteMode = LiteralRewriteMode.SafeDirect,
            IReadOnlyList<string>? noBatchimInputs = null,
            IReadOnlyList<string>? batchimInputs = null)
        {
            Key = key;
            NoBatchimSurface = noBatchimSurface;
            BatchimSurface = batchimSurface;
            RieulSurface = rieulSurface;
            MacroSuffix = macroSuffix;
            FunctionParticle = functionParticle;
            SupportsMacroShorthand = supportsMacroShorthand;
            PassThrough = passThrough;
            LiteralRewriteMode = literalRewriteMode;
            _noBatchimInputs = (noBatchimInputs ?? [noBatchimSurface]).Distinct(StringComparer.Ordinal).ToArray();
            _batchimInputs = (batchimInputs ?? [batchimSurface]).Distinct(StringComparer.Ordinal).ToArray();
            AllAcceptedInputs = BuildAcceptedInputs().ToArray();
            MacroMatchParticles = _noBatchimInputs.Concat(_batchimInputs).Distinct(StringComparer.Ordinal).ToArray();
        }

        public string Key { get; }

        public string NoBatchimSurface { get; }

        public string BatchimSurface { get; }

        public string? RieulSurface { get; }

        public string MacroSuffix { get; }

        public string FunctionParticle { get; }

        public bool SupportsMacroShorthand { get; }

        public bool PassThrough { get; }

        public LiteralRewriteMode LiteralRewriteMode { get; }

        public IReadOnlyList<string> AllAcceptedInputs { get; }

        public IReadOnlyList<string> MacroMatchParticles { get; }

        public string GetLiteralSurface(TokenBatchimKind batchimKind)
        {
            return batchimKind switch
            {
                TokenBatchimKind.None => NoBatchimSurface,
                TokenBatchimKind.RieulBatchim when !string.IsNullOrEmpty(RieulSurface) => RieulSurface!,
                _ => BatchimSurface,
            };
        }

        public bool ShouldRewriteLiteral(string token, string particleText)
        {
            var isComposite = particleText.IndexOfAny(['/', '(', ')']) >= 0;
            var isLongBatchimForm = _batchimInputs.Any(input =>
                input.Length > NoBatchimSurface.Length
                && string.Equals(input, particleText, StringComparison.Ordinal));

            if (token.Length == 1 && !isComposite && particleText.Length == 1)
            {
                return false;
            }

            return LiteralRewriteMode switch
            {
                LiteralRewriteMode.SafeDirect => true,
                LiteralRewriteMode.LongOrCompositeOnly => isComposite || isLongBatchimForm,
                _ => false,
            };
        }

        private IEnumerable<string> BuildAcceptedInputs()
        {
            foreach (var value in _noBatchimInputs.Concat(_batchimInputs).Distinct(StringComparer.Ordinal))
            {
                yield return value;
            }

            if (PassThrough)
            {
                yield break;
            }

            foreach (var batchimInput in _batchimInputs)
            {
                foreach (var noBatchimInput in _noBatchimInputs)
                {
                    if (string.Equals(batchimInput, noBatchimInput, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    yield return $"{batchimInput}/{noBatchimInput}";
                    yield return $"{noBatchimInput}/{batchimInput}";
                    yield return $"{batchimInput}({noBatchimInput})";
                    yield return $"{noBatchimInput}({batchimInput})";
                    yield return $"({batchimInput}){noBatchimInput}";
                    yield return $"({noBatchimInput}){batchimInput}";
                }
            }
        }
    }

    private enum LiteralRewriteMode
    {
        SafeDirect,
        LongOrCompositeOnly,
    }
}

public readonly record struct JosaRewriteTextResult(
    string Text,
    JosaDocumentAnalysis Analysis,
    bool RequiresErh);
