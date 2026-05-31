namespace EraTranslator.Services;

public sealed class JapaneseReadingFallbackService
{
    private const int HangulBase = 0xAC00;
    private const int HangulEnd = 0xD7A3;
    private const int HangulLeadUnit = 588;
    private const int HangulVowelUnit = 28;
    private const int LeadNieun = 2;
    private const int LeadRieul = 5;
    private const int LeadIeung = 11;

    private static readonly Dictionary<string, string> DigraphMap = new(StringComparer.Ordinal)
    {
        ["キャ"] = "캬", ["キュ"] = "큐", ["キョ"] = "쿄",
        ["シャ"] = "샤", ["シュ"] = "슈", ["ショ"] = "쇼",
        ["チャ"] = "차", ["チュ"] = "추", ["チョ"] = "초",
        ["ニャ"] = "냐", ["ニュ"] = "뉴", ["ニョ"] = "뇨",
        ["ヒャ"] = "햐", ["ヒュ"] = "휴", ["ヒョ"] = "효",
        ["ミャ"] = "먀", ["ミュ"] = "뮤", ["ミョ"] = "묘",
        ["リャ"] = "랴", ["リュ"] = "류", ["リョ"] = "료",
        ["ギャ"] = "갸", ["ギュ"] = "규", ["ギョ"] = "교",
        ["ジャ"] = "자", ["ジュ"] = "주", ["ジョ"] = "조",
        ["ヂャ"] = "자", ["ヂュ"] = "주", ["ヂョ"] = "조",
        ["ビャ"] = "뱌", ["ビュ"] = "뷰", ["ビョ"] = "뵤",
        ["ピャ"] = "퍄", ["ピュ"] = "퓨", ["ピョ"] = "표",
        ["ファ"] = "파", ["フィ"] = "피", ["フェ"] = "페", ["フォ"] = "포",
        ["ティ"] = "티", ["ディ"] = "디",
        ["トゥ"] = "투", ["ドゥ"] = "두",
        ["ウィ"] = "위", ["ウェ"] = "웨", ["ウォ"] = "워",
        ["ヴァ"] = "바", ["ヴィ"] = "비", ["ヴェ"] = "베", ["ヴォ"] = "보", ["ヴュ"] = "뷰",
        ["ツァ"] = "차", ["ツィ"] = "치", ["ツェ"] = "체", ["ツォ"] = "초",
        ["シェ"] = "셰", ["ジェ"] = "제", ["チェ"] = "체",
    };

    private static readonly Dictionary<char, string> MonographMap = new()
    {
        ['ア'] = "아", ['イ'] = "이", ['ウ'] = "우", ['エ'] = "에", ['オ'] = "오",
        ['カ'] = "카", ['キ'] = "키", ['ク'] = "쿠", ['ケ'] = "케", ['コ'] = "코",
        ['サ'] = "사", ['シ'] = "시", ['ス'] = "스", ['セ'] = "세", ['ソ'] = "소",
        ['タ'] = "타", ['チ'] = "치", ['ツ'] = "츠", ['テ'] = "테", ['ト'] = "토",
        ['ナ'] = "나", ['ニ'] = "니", ['ヌ'] = "누", ['ネ'] = "네", ['ノ'] = "노",
        ['ハ'] = "하", ['ヒ'] = "히", ['フ'] = "후", ['ヘ'] = "헤", ['ホ'] = "호",
        ['マ'] = "마", ['ミ'] = "미", ['ム'] = "무", ['メ'] = "메", ['モ'] = "모",
        ['ヤ'] = "야", ['ユ'] = "유", ['ヨ'] = "요",
        ['ラ'] = "라", ['リ'] = "리", ['ル'] = "루", ['レ'] = "레", ['ロ'] = "로",
        ['ワ'] = "와", ['ヲ'] = "오",
        ['ガ'] = "가", ['ギ'] = "기", ['グ'] = "구", ['ゲ'] = "게", ['ゴ'] = "고",
        ['ザ'] = "자", ['ジ'] = "지", ['ズ'] = "즈", ['ゼ'] = "제", ['ゾ'] = "조",
        ['ダ'] = "다", ['ヂ'] = "지", ['ヅ'] = "즈", ['デ'] = "데", ['ド'] = "도",
        ['バ'] = "바", ['ビ'] = "비", ['ブ'] = "부", ['ベ'] = "베", ['ボ'] = "보",
        ['パ'] = "파", ['ピ'] = "피", ['プ'] = "푸", ['ペ'] = "페", ['ポ'] = "포",
        ['ヴ'] = "브", ['ー'] = string.Empty, ['・'] = "·",
    };

    private static readonly Dictionary<char, char> TenseLeadMap = new()
    {
        ['가'] = '까', ['기'] = '끼', ['구'] = '꾸', ['게'] = '께', ['고'] = '꼬',
        ['다'] = '따', ['디'] = '띠', ['두'] = '뚜', ['데'] = '떼', ['도'] = '또',
        ['바'] = '빠', ['비'] = '삐', ['부'] = '뿌', ['베'] = '뻬', ['보'] = '뽀',
        ['사'] = '싸', ['시'] = '씨', ['스'] = '쓰', ['세'] = '쎄', ['소'] = '쏘',
        ['자'] = '짜', ['지'] = '찌', ['주'] = '쭈', ['제'] = '쩨', ['조'] = '쪼',
        ['차'] = '짜', ['치'] = '찌', ['추'] = '쭈', ['체'] = '쩨', ['초'] = '쪼',
    };

    public bool TryTransliterateKatakana(string input, out string output)
    {
        output = string.Empty;
        var normalized = NormalizeKatakana(input);
        if (string.IsNullOrWhiteSpace(normalized)
            || !normalized.All(IsSupportedKatakanaCharacter)
            || !normalized.Any(IsKatakanaLetter))
        {
            return false;
        }

        var builder = new System.Text.StringBuilder(normalized.Length * 2);
        var geminate = false;
        for (var index = 0; index < normalized.Length; index++)
        {
            var current = normalized[index];
            if (current == 'ッ')
            {
                geminate = true;
                continue;
            }

            if (current is '／' or '/')
            {
                builder.Append(current);
                continue;
            }

            if (current == 'ン')
            {
                AppendBatchim(builder, 'ㄴ');
                continue;
            }

            if (index + 1 < normalized.Length)
            {
                var digraph = normalized.Substring(index, 2);
                if (DigraphMap.TryGetValue(digraph, out var digraphValue))
                {
                    builder.Append(ApplyGeminate(digraphValue, ref geminate));
                    index++;
                    continue;
                }
            }

            if (!MonographMap.TryGetValue(current, out var monographValue))
            {
                return false;
            }

            builder.Append(ApplyGeminate(monographValue, ref geminate));
        }

        output = builder.ToString().Trim();
        return !string.IsNullOrWhiteSpace(output);
    }

    public bool TryBuildKanjiReading(string input, IBundledJapaneseLexiconService lexiconService, out string output)
    {
        output = string.Empty;
        var normalized = (input ?? string.Empty).Trim();
        if (normalized.Length < 2 || normalized.Any(static ch => char.IsWhiteSpace(ch)))
        {
            return false;
        }

        if (!normalized.All(static ch => IsKanji(ch) || ch is '／' or '/' or '・'))
        {
            return false;
        }

        var builder = new System.Text.StringBuilder(normalized.Length * 2);
        foreach (var character in normalized)
        {
            if (character is '／' or '/' or '・')
            {
                builder.Append(character);
                continue;
            }

            if (!lexiconService.TryGetKanjiReading(character, out var entry)
                || string.IsNullOrWhiteSpace(entry.KoreanH))
            {
                return false;
            }

            builder.Append(entry.KoreanH);
        }

        output = ApplyKoreanInitialSoundRule(builder.ToString());
        return !string.IsNullOrWhiteSpace(output);
    }

    private static string NormalizeKatakana(string input)
    {
        return (input ?? string.Empty).Trim();
    }

    private static bool IsSupportedKatakanaCharacter(char character)
    {
        return character is 'ッ' or 'ー' or '・'
            or '／' or '/'
            || MonographMap.ContainsKey(character)
            || character is >= 'ァ' and <= 'ヶ';
    }

    private static bool IsKatakanaLetter(char character)
    {
        return MonographMap.ContainsKey(character)
            || character is >= 'ァ' and <= 'ヶ';
    }

    private static string ApplyGeminate(string value, ref bool geminate)
    {
        if (!geminate || string.IsNullOrEmpty(value))
        {
            geminate = false;
            return value;
        }

        geminate = false;
        var lead = value[0];
        if (TenseLeadMap.TryGetValue(lead, out var tenseLead))
        {
            return tenseLead + value[1..];
        }

        return value;
    }

    private static void AppendBatchim(System.Text.StringBuilder builder, char batchim)
    {
        if (builder.Length == 0)
        {
            builder.Append(batchim);
            return;
        }

        var last = builder[^1];
        if (last is < '\uAC00' or > '\uD7A3')
        {
            builder.Append(batchim);
            return;
        }

        var syllableIndex = last - '\uAC00';
        var lead = syllableIndex / 588;
        var vowel = (syllableIndex % 588) / 28;
        var tail = syllableIndex % 28;
        if (tail != 0)
        {
            builder.Append(batchim);
            return;
        }

        var batchimIndex = batchim switch
        {
            'ㄴ' => 4,
            _ => 0,
        };

        if (batchimIndex == 0)
        {
            builder.Append(batchim);
            return;
        }

        builder[^1] = (char)('\uAC00' + (lead * 588) + (vowel * 28) + batchimIndex);
    }

    private static bool IsKanji(char character)
    {
        return character is >= '\u4E00' and <= '\u9FFF';
    }

    private static string ApplyKoreanInitialSoundRule(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        var builder = new System.Text.StringBuilder(input.Length);
        var isTokenStart = true;
        foreach (var character in input)
        {
            if (character is '／' or '/' or '・')
            {
                builder.Append(character);
                isTokenStart = true;
                continue;
            }

            builder.Append(isTokenStart ? ApplyKoreanInitialSoundRuleToSyllable(character) : character);
            isTokenStart = false;
        }

        return builder.ToString();
    }

    private static char ApplyKoreanInitialSoundRuleToSyllable(char syllable)
    {
        if (syllable is < (char)HangulBase or > (char)HangulEnd)
        {
            return syllable;
        }

        var syllableIndex = syllable - HangulBase;
        var lead = syllableIndex / HangulLeadUnit;
        var vowel = (syllableIndex % HangulLeadUnit) / HangulVowelUnit;
        var tail = syllableIndex % HangulVowelUnit;

        if (lead == LeadRieul)
        {
            return ComposeHangul(IsIotizedOrIVowel(vowel) ? LeadIeung : LeadNieun, vowel, tail);
        }

        if (lead == LeadNieun && IsIotizedOrIVowel(vowel))
        {
            return ComposeHangul(LeadIeung, vowel, tail);
        }

        return syllable;
    }

    private static bool IsIotizedOrIVowel(int vowel)
    {
        return vowel is 2 or 3 or 6 or 7 or 12 or 17 or 20;
    }

    private static char ComposeHangul(int lead, int vowel, int tail)
    {
        return (char)(HangulBase + (lead * HangulLeadUnit) + (vowel * HangulVowelUnit) + tail);
    }
}
