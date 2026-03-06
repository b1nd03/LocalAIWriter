using System.Text.RegularExpressions;
using LocalAIWriter.Core.Extensions;

namespace LocalAIWriter.Core.Services;

/// <summary>
/// The rule-based correction engine providing instant deterministic
/// fixes for common writing errors that don't need ML inference.
/// Covers: spelling, grammar, capitalization, punctuation, style.
/// </summary>
public sealed partial class RuleBasedEngine
{
    /// <summary>
    /// Applies all deterministic rules to the input text.
    /// </summary>
    public RuleBasedResult ApplyRules(string text)
        => ApplyRules(text, CorrectionAggressiveness.Balanced);

    /// <summary>
    /// Applies deterministic rules to the input text using the specified aggressiveness.
    /// </summary>
    public RuleBasedResult ApplyRules(string text, CorrectionAggressiveness aggressiveness)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new RuleBasedResult(text, Array.Empty<RuleCorrection>());

        var corrections = new List<RuleCorrection>();
        var result = text;

        // Always-safe baseline rules
        result = FixDoubleSpaces(result, corrections);
        result = FixDuplicateWords(result, corrections);
        result = FixCommonMisspellings(result, corrections);
        result = FixCapitalization(result, corrections);
        result = FixPunctuation(result, corrections);
        result = FixMissingPeriod(result, corrections);
        result = FixSmartQuotes(result, corrections);

        if (aggressiveness != CorrectionAggressiveness.Low)
        {
            result = FixSubjectVerbAgreement(result, corrections);
            result = FixCommonGrammarMistakes(result, corrections);
            result = FixArticles(result, corrections);
            result = FixProperNouns(result, corrections);
            result = FixAdvancedGrammar(result, corrections, allowBroadPatterns: aggressiveness == CorrectionAggressiveness.High);
        }

        return new RuleBasedResult(result, corrections);
    }

    #region Rule Implementations

    private static string FixDoubleSpaces(string text, List<RuleCorrection> corrections)
    {
        if (!text.Contains("  "))
            return text;

        var fixed_ = DoubleSpaceRegex().Replace(text, " ");
        if (fixed_ != text)
            corrections.Add(new RuleCorrection("Removed extra spaces", CorrectionType.RuleBased, 1.0f));
        return fixed_;
    }

    // Valid intentional repeated-word constructions to never de-duplicate
    private static readonly HashSet<string> AllowedDuplicates = new(StringComparer.OrdinalIgnoreCase)
    {
        "had", "that", "very", "so", "quite", "rather", "more"
    };

    private static string FixDuplicateWords(string text, List<RuleCorrection> corrections)
    {
        var fixed_ = DuplicateWordRegex().Replace(text, m =>
        {
            // Keep valid intentional repetitions like "had had", "that that"
            if (AllowedDuplicates.Contains(m.Groups[1].Value))
                return m.Value;
            return m.Groups[1].Value;
        });
        if (fixed_ != text)
            corrections.Add(new RuleCorrection("Removed duplicate word", CorrectionType.RuleBased, 1.0f));
        return fixed_;
    }

    // ──────────────────────────── SUBJECT-VERB AGREEMENT ────────────────────────────

    // NOTE: Caches are initialized in the static constructor below (after data arrays)
    private static readonly (string Pattern, Regex Compiled, string Replacement, string Desc)[] SubjectVerbRulesCached;
    private static readonly (string Wrong, Regex Compiled, string Right)[] GrammarFixesCached;
    private static readonly (string Wrong, Regex Compiled, string Right)[] CommonMisspellingsCached;
    private static readonly (string Pattern, Regex Compiled, string Replacement, string Desc)[] AdvancedGrammarRulesCached;
    private static readonly (string Proper, Regex Compiled)[] ProperNounsCached;
    private static readonly HashSet<string> DisabledGrammarFixes = new(StringComparer.OrdinalIgnoreCase)
    {
        // These are highly context-dependent and can corrupt valid sentences.
        "were going",
        "lets"
    };

    // Words that can legitimately be common nouns/adjectives in lowercase.
    private static readonly HashSet<string> AmbiguousProperNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        "turkey",
        "chile",
        "china",
        "march",
        "may",
        "august",
        "earth",
        "internet",
        "bible",
        "quran"
    };

    static RuleBasedEngine()
    {
        SubjectVerbRulesCached = SubjectVerbRules
            .Select(r => (r.Pattern, new Regex(r.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled), r.Replacement, r.Desc))
            .ToArray();
        GrammarFixesCached = GrammarFixes
            .Where(r => !DisabledGrammarFixes.Contains(r.Wrong))
            .Select(r => (r.Wrong, new Regex(@"\b" + Regex.Escape(r.Wrong) + @"\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), r.Right))
            .ToArray();
        CommonMisspellingsCached = CommonMisspellings
            .Select(r => (r.Wrong, new Regex(@"\b" + Regex.Escape(r.Wrong) + @"\b", RegexOptions.IgnoreCase | RegexOptions.Compiled), r.Right))
            .ToArray();
        AdvancedGrammarRulesCached = AdvancedGrammarRules
            .Select(r => (r.Pattern, new Regex(r.Pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled), r.Replacement, r.Desc))
            .ToArray();
        ProperNounsCached = ProperNouns
            .Where(p => !AmbiguousProperNouns.Contains(p))
            .Select(p => (p, new Regex(@"\b" + Regex.Escape(p.ToLowerInvariant()) + @"\b", RegexOptions.Compiled)))
            .ToArray();
    }

    private static string FixSubjectVerbAgreement(string text, List<RuleCorrection> corrections)
    {
        var result = text;
        bool changed = false;

        foreach (var (_, regex, replacement, _) in SubjectVerbRulesCached)
        {
            // Use string overload so $1/$2 backreferences are resolved
            var newResult = regex.Replace(result, replacement);

            // Preserve original capitalization: if the matched text started with
            // uppercase but the replacement starts with lowercase, capitalize it
            if (newResult != result)
            {
                // Find where the change occurred and fix casing
                var match = regex.Match(result);
                if (match.Success && char.IsUpper(match.Value[0]))
                {
                    int idx = match.Index;
                    if (idx < newResult.Length && char.IsLower(newResult[idx]))
                    {
                        var arr = newResult.ToCharArray();
                        arr[idx] = char.ToUpper(arr[idx]);
                        newResult = new string(arr);
                    }
                }
                result = newResult;
                changed = true;
            }
        }

        if (changed)
            corrections.Add(new RuleCorrection("Fixed subject-verb agreement", CorrectionType.Grammar, 0.95f));

        return result;
    }

    // ──────────────────────────── COMMON GRAMMAR MISTAKES ────────────────────────────

    private static string FixCommonGrammarMistakes(string text, List<RuleCorrection> corrections)
    {
        var result = text;
        bool changed = false;

        foreach (var (_, regex, right) in GrammarFixesCached)
        {
            var newResult = regex.Replace(result, m =>
            {
                // Preserve case of first letter
                if (char.IsUpper(m.Value[0]))
                    return char.ToUpper(right[0]) + right[1..];
                return right;
            });
            if (newResult != result)
            {
                result = newResult;
                changed = true;
            }
        }

        if (changed)
            corrections.Add(new RuleCorrection("Fixed grammar", CorrectionType.Grammar, 0.9f));

        return result;
    }

    // ──────────────────────────── ARTICLES (a/an) ────────────────────────────

    // Words starting with a vowel letter but consonant sound — keep "a"
    private static readonly HashSet<string> VowelSoundExceptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "uni", "une", "eur", "use", "usu", "one", "once"
    };

    [GeneratedRegex(@"\b[Aa]\s+([aeiouAEIOU]\w*)", RegexOptions.None)]
    private static partial Regex ABeforeVowelRegex();

    [GeneratedRegex(@"\b[Aa]n\s+([bcdfgjklmnpqrstvwxyzBCDFGJKLMNPQRSTVWXYZ]\w*)", RegexOptions.None)]
    private static partial Regex AnBeforeConsonantRegex();

    private static string FixArticles(string text, List<RuleCorrection> corrections)
    {
        var result = text;
        bool changed = false;

        // "a" before vowel sounds → "an" (but skip consonant-sound words like "union", "university")
        var newResult = ABeforeVowelRegex().Replace(result, m =>
        {
            var word = m.Groups[1].Value.ToLowerInvariant();
            // Skip words with consonant sound despite vowel letter
            if (word.StartsWith("uni") || word.StartsWith("une") || word.StartsWith("eur") ||
                word.StartsWith("use") || word.StartsWith("usu") || word.StartsWith("one") ||
                word.StartsWith("once"))
                return m.Value;
            var article = char.IsUpper(m.Value[0]) ? "An" : "an";
            return article + " " + m.Groups[1].Value;
        });
        if (newResult != result) { result = newResult; changed = true; }

        // "an" before consonant sounds → "a" (skip "an hour", "an honest", etc.)
        newResult = AnBeforeConsonantRegex().Replace(result, m =>
        {
            var word = m.Groups[1].Value.ToLowerInvariant();
            if (word.StartsWith("hour") || word.StartsWith("honest") || word.StartsWith("honor") || word.StartsWith("heir"))
                return m.Value; // keep "an"
            var article = char.IsUpper(m.Value[0]) ? "A" : "a";
            return article + " " + m.Groups[1].Value;
        });
        if (newResult != result) { result = newResult; changed = true; }

        if (changed)
            corrections.Add(new RuleCorrection("Fixed article (a/an)", CorrectionType.Grammar, 0.95f));

        return result;
    }

    // ──────────────────────────── PROPER NOUNS ────────────────────────────

    private static string FixProperNouns(string text, List<RuleCorrection> corrections)
    {
        var result = text;
        bool changed = false;

        foreach (var (proper, regex) in ProperNounsCached)
        {
            var newResult = regex.Replace(result, proper);
            if (newResult != result)
            {
                result = newResult;
                changed = true;
            }
        }

        if (changed)
            corrections.Add(new RuleCorrection("Fixed proper noun capitalization", CorrectionType.RuleBased, 1.0f));

        return result;
    }

    // ──────────────────────────── CAPITALIZATION ────────────────────────────

    private static string FixCapitalization(string text, List<RuleCorrection> corrections)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Use StringBuilder for a single-pass O(n) capitalization fix
        var sb = new System.Text.StringBuilder(text);
        bool changed = false;

        // Capitalize first letter of text
        if (char.IsLetter(sb[0]) && char.IsLower(sb[0]))
        {
            sb[0] = char.ToUpper(sb[0]);
            changed = true;
        }

        // Capitalize after sentence-ending punctuation (single pass, no repeated char[] allocs)
        for (int i = 0; i < sb.Length - 2; i++)
        {
            if ((sb[i] == '.' || sb[i] == '!' || sb[i] == '?') &&
                sb[i + 1] == ' ' && char.IsLower(sb[i + 2]))
            {
                sb[i + 2] = char.ToUpper(sb[i + 2]);
                changed = true;
            }
        }

        var result = sb.ToString();

        // Capitalize "i" when used as pronoun (regex matches just the "i" token)
        result = StandaloneIRegex().Replace(result, m =>
        {
            changed = true;
            return "I";
        });

        if (result.StartsWith("i ", StringComparison.Ordinal))
        {
            result = "I " + result[2..];
            changed = true;
        }

        if (changed)
            corrections.Add(new RuleCorrection("Fixed capitalization", CorrectionType.RuleBased, 1.0f));

        return result;
    }

    [GeneratedRegex(@"\s+([,\.!?;:])", RegexOptions.None)]
    private static partial Regex SpaceBeforePunctRegex();

    [GeneratedRegex(@"([,])(\w)", RegexOptions.None)]
    private static partial Regex MissingSpaceAfterCommaRegex();

    // ──────────────────────────── PUNCTUATION ────────────────────────────

    private static string FixPunctuation(string text, List<RuleCorrection> corrections)
    {
        var result = text;
        bool changed = false;

        // Fix space before comma/period/exclamation/question
        var newResult = SpaceBeforePunctRegex().Replace(result, "$1");
        if (newResult != result) { result = newResult; changed = true; }

        // Fix missing space after comma (but not in numbers like 3.14)
        newResult = MissingSpaceAfterCommaRegex().Replace(result, "$1 $2");
        if (newResult != result) { result = newResult; changed = true; }

        // Fix double periods
        var noDoublePeriod = result.Replace("..", ".");
        if (noDoublePeriod != result) { result = noDoublePeriod; changed = true; }

        if (changed)
            corrections.Add(new RuleCorrection("Fixed punctuation", CorrectionType.RuleBased, 0.9f));

        return result;
    }

    // ──────────────────────────── MISSPELLINGS ────────────────────────────

    private static string FixCommonMisspellings(string text, List<RuleCorrection> corrections)
    {
        var result = text;
        bool changed = false;

        foreach (var (_, regex, right) in CommonMisspellingsCached)
        {
            var newResult = regex.Replace(result, m =>
            {
                if (char.IsUpper(m.Value[0]))
                    return char.ToUpper(right[0]) + right[1..];
                return right;
            });
            if (newResult != result)
            {
                result = newResult;
                changed = true;
            }
        }

        if (changed)
            corrections.Add(new RuleCorrection("Fixed misspelling", CorrectionType.RuleBased, 0.95f));

        return result;
    }

    // ──────────────────────────── ADVANCED GRAMMAR ────────────────────────────

    private static string FixAdvancedGrammar(string text, List<RuleCorrection> corrections, bool allowBroadPatterns)
    {
        if (!LooksLikeProse(text))
            return text;

        var result = text;
        bool changed = false;

        foreach (var (_, regex, replacement, desc) in AdvancedGrammarRulesCached)
        {
            if (IsHighRiskAdvancedRule(desc, allowBroadPatterns))
                continue;

            var newResult = regex.Replace(result, replacement);
            if (newResult != result && IsSafeAdvancedReplacement(result, newResult))
            {
                result = newResult;
                changed = true;
            }
        }

        // Keep sentence-start casing stable after advanced replacements.
        if (result.Length > 0 &&
            text.Length > 0 &&
            char.IsLetter(result[0]) &&
            char.IsUpper(text[0]) &&
            char.IsLower(result[0]))
        {
            result = char.ToUpperInvariant(result[0]) + result[1..];
            changed = true;
        }

        if (changed)
            corrections.Add(new RuleCorrection("Fixed grammar", CorrectionType.Grammar, 0.9f));

        return result;
    }

    private static bool LooksLikeProse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (text.Contains('\n') || text.Contains('\t'))
            return false;

        if (text.Contains("://", StringComparison.Ordinal) || text.Contains('@'))
            return false;

        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 4;
    }

    private static bool IsHighRiskAdvancedRule(string description, bool allowBroadPatterns)
    {
        if (description.StartsWith("third conditional", StringComparison.OrdinalIgnoreCase) ||
            description.StartsWith("passive: to be + base", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!allowBroadPatterns)
        {
            return description.StartsWith("different than", StringComparison.OrdinalIgnoreCase) ||
                   description.StartsWith("works hardly", StringComparison.OrdinalIgnoreCase) ||
                   description.StartsWith("being that", StringComparison.OrdinalIgnoreCase) ||
                   description.StartsWith("told that", StringComparison.OrdinalIgnoreCase) ||
                   description.StartsWith("data suggests", StringComparison.OrdinalIgnoreCase) ||
                   description.StartsWith("neither...nor", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsSafeAdvancedReplacement(string before, string after)
    {
        if (before.Length == 0 || after.Length == 0)
            return true;

        var ratio = (double)after.Length / before.Length;
        if (ratio < 0.75 || ratio > 1.33)
            return false;

        var beforeWords = before
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeToken)
            .Where(w => w.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var afterWords = after
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeToken)
            .Where(w => w.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (beforeWords.Count == 0)
            return true;

        int overlap = beforeWords.Intersect(afterWords).Count();
        return overlap >= Math.Max(1, beforeWords.Count / 3);
    }

    private static string NormalizeToken(string token) =>
        token.Trim().Trim('.', ',', '!', '?', ';', ':', '"', '\'').ToLowerInvariant();

    private static string FixMissingPeriod(string text, List<RuleCorrection> corrections)
    {
        var trimmed = text.TrimEnd();
        if (trimmed.Length == 0)
            return text;

        char last = trimmed[^1];
        if (char.IsLetter(last) || char.IsDigit(last))
        {
            // Only add a period if text looks like prose (has multiple words, starts uppercase,
            // and contains a verb-like structure — i.e., has at least one lowercase word after the first)
            // This avoids adding periods to titles, subject lines, filenames, and UI labels.
            if (trimmed.Contains(' ') && char.IsUpper(trimmed[0]))
            {
                // Heuristic: if text has 5+ words and contains at least one space-separated lowercase word
                // that isn't a common title word, treat it as a sentence
                var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                bool hasLowercaseContent = words.Skip(1).Any(w => w.Length > 1 && char.IsLower(w[0]));
                if (words.Length >= 5 && hasLowercaseContent)
                {
                    corrections.Add(new RuleCorrection("Added missing period", CorrectionType.RuleBased, 0.7f));
                    return trimmed + ".";
                }
            }
        }

        return text;
    }

    private static string FixSmartQuotes(string text, List<RuleCorrection> corrections)
    {
        if (!text.Contains('\u201C') && !text.Contains('\u201D') &&
            !text.Contains('\u2018') && !text.Contains('\u2019'))
            return text;

        var result = text
            .Replace('\u201C', '"')
            .Replace('\u201D', '"')
            .Replace('\u2018', '\'')
            .Replace('\u2019', '\'');

        if (result != text)
            corrections.Add(new RuleCorrection("Normalized quotation marks", CorrectionType.RuleBased, 1.0f));

        return result;
    }

    #endregion

    #region Regex Patterns

    [GeneratedRegex(@" {2,}")]
    private static partial Regex DoubleSpaceRegex();

    [GeneratedRegex(@"\b(\w+)\s+\1\b", RegexOptions.IgnoreCase)]
    private static partial Regex DuplicateWordRegex();

    [GeneratedRegex(@"(?<=\s)i(?=\s|')", RegexOptions.None)]
    private static partial Regex StandaloneIRegex();

    #endregion

    #region Data

    // ──────────────────────────── SUBJECT-VERB AGREEMENT ────────────────────────────

    private static readonly (string Pattern, string Replacement, string Desc)[] SubjectVerbRules =
    {
        // he/she/it + are → is
        (@"\b(he|she|it)\s+are\b", "$1 is", "he/she/it are → is"),
        (@"\b(he|she|it)\s+were\b", "$1 was", "he/she/it were → was"),
        (@"\b(he|she|it)\s+have\b", "$1 has", "he/she/it have → has"),
        (@"\b(he|she|it)\s+do\b", "$1 does", "he/she/it do → does"),
        (@"\b(he|she|it)\s+don't\b", "$1 doesn't", "he/she/it don't → doesn't"),

        // I + is → am
        (@"\bI\s+is\b", "I am", "I is → I am"),
        (@"\bI\s+are\b", "I am", "I are → I am"),
        (@"\bI\s+has\b", "I have", "I has → I have"),
        (@"\bI\s+does\b", "I do", "I does → I do"),
        (@"\bI\s+was\s+are\b", "I was", "I was are → I was"),

        // we/they/you + is → are
        (@"\b(we|they|you)\s+is\b", "$1 are", "we/they/you is → are"),
        (@"\b(we|they|you)\s+was\b", "$1 were", "we/they/you was → were"),
        (@"\b(we|they|you)\s+has\b", "$1 have", "we/they/you has → have"),
        (@"\b(we|they|you)\s+does\b", "$1 do", "we/they/you does → do"),
        (@"\b(we|they|you)\s+doesn't\b", "$1 don't", "we/they/you doesn't → don't"),

        // Singular nouns (this/that/everyone/nobody) + are → is
        (@"\b(this|that|everyone|nobody|somebody|anything|everything|nothing)\s+are\b", "$1 is", "singular + are → is"),
        (@"\b(this|that|everyone|nobody|somebody|anything|everything|nothing)\s+were\b", "$1 was", "singular + were → was"),

        // there is/are (common)
        (@"\bthere\s+is\s+many\b", "there are many", "there is many → there are many"),
        (@"\bthere\s+is\s+several\b", "there are several", "there is several → there are several"),

        // Uncountable nouns + are → is
        (@"\b(the\s+)?(information|news|advice|music|furniture|luggage|equipment|knowledge|homework|research|evidence|progress|traffic|weather|electricity|software|hardware|data)\s+are\b", "$1$2 is", "uncountable + are → is"),
        (@"\b(the\s+)?(information|news|advice|music|furniture|luggage|equipment|knowledge|homework|research|evidence|progress|traffic|weather|electricity|software|hardware|data)\s+were\b", "$1$2 was", "uncountable + were → was"),
    };

    // ──────────────────────────── ADVANCED GRAMMAR RULES ────────────────────────────

    private static readonly (string Pattern, string Replacement, string Desc)[] AdvancedGrammarRules =
    {
        // NOTE: Removed the overly generic modal rule that was stripping 's' from random words.
        // Only use specific safe rules below.

        // Specific modal fixes
        (@"\bcan\s+sings\b", "can sing", "can sings → can sing"),
        (@"\bcan\s+goes\b", "can go", "can goes → can go"),
        (@"\bcan\s+runs\b", "can run", "can runs → can run"),
        (@"\bcan\s+plays\b", "can play", "can plays → can play"),
        (@"\bcan\s+makes\b", "can make", "can makes → can make"),
        (@"\bcan\s+comes\b", "can come", "can comes → can come"),
        (@"\bshould\s+goes\b", "should go", "should goes → should go"),
        (@"\bwill\s+goes\b", "will go", "will goes → will go"),
        (@"\bmust\s+has\b", "must have", "must has → must have"),

        // Conditionals: "If I will see" → "If I see" (first conditional)
        (@"\b[Ii]f\s+(I|you|we|they|he|she|it)\s+will\s+(\w+)\b", "if $1 $2", "if + will → present simple"),

        // Indirect questions: "where was I going" → "where I was going"
        (@"\b(asked|wondered|know|knew|tell|said)\s+(me|him|her|us|them)\s+(where|what|when|why|how|who)\s+(was|were|is|are|did|do|does|had|has|have)\s+(I|you|he|she|it|we|they)\b", "$1 $2 $3 $5 $4", "indirect question word order"),

        // too much + countable → too many
        (@"\btoo\s+much\s+(people|students|things|books|cars|children|men|women|problems|questions|answers|items|rooms|houses|days|hours|minutes)\b", "too many $1", "too much + countable → too many"),

        // There's + plural → There are
        (@"\b[Tt]here's\s+too\s+much\s+(people|students|things|children)\b", "There are too many $1", "there's too much people → there are too many people"),
        (@"\b[Tt]here's\s+too\s+many\b", "There are too many", "there's too many → there are too many"),

        // Passive voice: "to be wash" → "to be washed", "to be fix" → "to be fixed"
        (@"\bto\s+be\s+(wash|fix|clean|paint|cook|build|make|break|send|show|tell|give|take|write|read|speak|teach|finish|complete|open|close|check|test|repair|install|remove|replace|use|do|see|hear|find|put)\b",
         "to be ${1}ed", "passive: to be + base → past participle"),
        (@"\bto\s+be\s+wrote\b", "to be written", "to be wrote → written"),
        (@"\bto\s+be\s+broke\b", "to be broken", "to be broke → broken"),
        (@"\bto\s+be\s+spoke\b", "to be spoken", "to be spoke → spoken"),
        (@"\bto\s+be\s+chose\b", "to be chosen", "to be chose → chosen"),
        (@"\bto\s+be\s+took\b", "to be taken", "to be took → taken"),

        // Double comparatives: "more better" → "better"
        (@"\bmore\s+better\b", "better", "more better → better"),
        (@"\bmore\s+worse\b", "worse", "more worse → worse"),
        (@"\bmore\s+bigger\b", "bigger", "more bigger → bigger"),
        (@"\bmore\s+smaller\b", "smaller", "more smaller → smaller"),
        (@"\bmore\s+faster\b", "faster", "more faster → faster"),
        (@"\bmore\s+smarter\b", "smarter", "more smarter → smarter"),
        (@"\bmost\s+best\b", "best", "most best → best"),
        (@"\bmost\s+worst\b", "worst", "most worst → worst"),

        // Gerunds after prepositions: "looking forward to see" → "looking forward to seeing"
        (@"\blooking\s+forward\s+to\s+(see|meet|hear|visit|go|do|work|play|start|learn|read|write|help|talk|get|have|make|find|come|try)\b",
         "looking forward to ${1}ing", "forward to + base → gerund"),
        (@"\blooking\s+forward\s+to\s+geting\b", "looking forward to getting", "fix double t"),

        // "Have + past participle" errors
        (@"\bhave\s+went\b", "have gone", "have went → have gone"),
        (@"\bhave\s+came\b", "have come", "have came → have come"),
        (@"\bhave\s+did\b", "have done", "have did → have done"),
        (@"\bhave\s+saw\b", "have seen", "have saw → have seen"),
        (@"\bhave\s+ran\b", "have run", "have ran → have run"),
        (@"\bhave\s+ate\b", "have eaten", "have ate → have eaten"),
        (@"\bhave\s+drank\b", "have drunk", "have drank → have drunk"),
        (@"\bhave\s+spoke\b", "have spoken", "have spoke → have spoken"),
        (@"\bhave\s+wrote\b", "have written", "have wrote → have written"),
        (@"\bhave\s+broke\b", "have broken", "have broke → have broken"),
        (@"\bhave\s+took\b", "have taken", "have took → have taken"),
        (@"\bhave\s+gave\b", "have given", "have gave → have given"),
        (@"\bhave\s+chose\b", "have chosen", "have chose → have chosen"),
        (@"\bhas\s+went\b", "has gone", "has went → has gone"),
        (@"\bhas\s+came\b", "has come", "has came → has come"),
        (@"\bhas\s+did\b", "has done", "has did → has done"),
        (@"\bhas\s+saw\b", "has seen", "has saw → has seen"),
        (@"\bhas\s+ate\b", "has eaten", "has ate → has eaten"),
        (@"\bhas\s+wrote\b", "has written", "has wrote → has written"),
        (@"\bhas\s+broke\b", "has broken", "has broke → has broken"),
        (@"\bhas\s+took\b", "has taken", "has took → has taken"),
        (@"\bhas\s+gave\b", "has given", "has gave → has given"),

        // "Neither/either of the answers are" → "is"
        (@"\b(neither|either)\s+of\s+(the\s+\w+|these\s+\w+|those\s+\w+)\s+are\b", "$1 of $2 is", "neither/either of + are → is"),

        // Information is uncountable
        (@"\binformations\b", "information", "informations → information"),
        (@"\badvices\b", "advice", "advices → advice"),
        (@"\bequipments\b", "equipment", "equipments → equipment"),
        (@"\bfurnitures\b", "furniture", "furnitures → furniture"),
        (@"\bhomeworks\b", "homework", "homeworks → homework"),
        (@"\bresearchs\b", "research", "researchs → research"),
        (@"\bknowledges\b", "knowledge", "knowledges → knowledge"),
        (@"\blugages\b", "luggage", "lugages → luggage"),
        (@"\bnewses\b", "news", "newses → news"),

        // "Each of the students have" → "has"
        (@"\b(each)\s+of\s+(the\s+\w+)\s+have\b", "$1 of $2 has", "each of + have → has"),

        // "The team are" → "The team is" (collective nouns, American English)
        (@"\b(the\s+)?(team|group|class|family|company|government|committee|audience|staff|police)\s+are\b", "$1$2 is", "collective noun + are → is"),

        // "My friend and me" → "My friend and I" (subject position)
        (@"\b(\w+)\s+and\s+me\s+(went|go|are|were|was|have|had|will|can|should|would|could|did)\b", "$1 and I $2", "and me → and I (subject)"),

        // "He didn't went" → "He didn't go" (specific fixes below)
        // Fix specific ones
        (@"\bdidn't\s+went\b", "didn't go", "didn't went → didn't go"),
        (@"\bdidn't\s+came\b", "didn't come", "didn't came → didn't come"),
        (@"\bdidn't\s+saw\b", "didn't see", "didn't saw → didn't see"),
        (@"\bdidn't\s+ate\b", "didn't eat", "didn't ate → didn't eat"),
        (@"\bdidn't\s+ran\b", "didn't run", "didn't ran → didn't run"),
        (@"\bdidn't\s+spoke\b", "didn't speak", "didn't spoke → didn't speak"),
        (@"\bdidn't\s+wrote\b", "didn't write", "didn't wrote → didn't write"),
        (@"\bdidn't\s+broke\b", "didn't break", "didn't broke → didn't break"),
        (@"\bdidn't\s+took\b", "didn't take", "didn't took → didn't take"),
        (@"\bdidn't\s+gave\b", "didn't give", "didn't gave → didn't give"),

        // "He go" → "He goes" (3rd person present)
        (@"\b(he|she|it)\s+go\s+to\b", "$1 goes to", "he go to → he goes to"),
        (@"\b(he|she|it)\s+go\s+(\w)", "$1 goes $2", "he go → he goes"),
        (@"\b(he|she|it)\s+have\s+a\b", "$1 has a", "he have a → he has a"),

        // "I have seen him yesterday" → "I saw him yesterday" (present perfect vs past simple)
        (@"\bhave\s+seen\s+(him|her|it|them|you|us)\s+yesterday\b", "saw $1 yesterday", "have seen yesterday → saw yesterday"),
        (@"\bhas\s+seen\s+(him|her|it|them|you|us)\s+yesterday\b", "saw $1 yesterday", "has seen yesterday → saw yesterday"),
        (@"\bhave\s+(\w+ed)\s+(yesterday|last\s+(week|month|year|night|time))\b", "$1 $2", "present perfect + past time → past simple"),

        // "every days" → "every day"
        (@"\bevery\s+days\b", "every day", "every days → every day"),
        (@"\bevery\s+times\b", "every time", "every times → every time"),
        (@"\bevery\s+years\b", "every year", "every years → every year"),
        (@"\bevery\s+weeks\b", "every week", "every weeks → every week"),
        (@"\bevery\s+months\b", "every month", "every months → every month"),
        (@"\bevery\s+mornings\b", "every morning", "every mornings → every morning"),
        (@"\bevery\s+nights\b", "every night", "every nights → every night"),

        // "has finished ... already" word order
        (@"\bhas\s+finished\s+(\w+)\s+(\w+)\s+already\b", "has already finished $1 $2", "move already before verb"),

        // "waiting for ... for two hours" → present perfect continuous
        (@"\b[Ww]e\s+was\s+waiting\b", "We were waiting", "we was → we were"),

        // ──── PREPOSITION FIXES ────
        (@"\b[Dd]espite\s+of\b", "Despite", "despite of → despite"),
        (@"\bbased\s+in\s+(previous|earlier|prior|recent|existing)\b", "based on $1", "based in → based on"),
        (@"\bresponsible\s+of\b", "responsible for", "responsible of → responsible for"),
        (@"\binterested\s+on\b", "interested in", "interested on → interested in"),
        (@"\bfocuses\s+in\b", "focuses on", "focuses in → focuses on"),
        (@"\bfocus\s+in\b", "focus on", "focus in → focus on"),
        (@"\bapplies\s+for\s+all\b", "applies to all", "applies for all → applies to all"),
        (@"\baccused\s+for\b", "accused of", "accused for → accused of"),
        (@"\bconsists?\s+from\b", "consists of", "consists from → consists of"),
        (@"\bcomprised\s+with\b", "comprised of", "comprised with → comprised of"),
        (@"\bprefer\s+(\w+)\s+than\b", "prefer $1 to", "prefer X than → prefer X to"),
        (@"\bdifferent\s+than\b", "different from", "different than → different from"),
        (@"\bbetter\s+in\s+(mathematics|math|physics|chemistry|biology|science|english|history)\b", "better at $1", "better in subject → better at"),
        (@"\bcapable\s+to\b", "capable of", "capable to → capable of"),
        (@"\bsucceeded\s+to\b", "succeeded in", "succeeded to → succeeded in"),
        (@"\baccustomed\s+to\s+(wake|eat|go|do|work|run|sleep|study|read|write|play|come|get|make|take)\b", "accustomed to ${1}ing", "accustomed to + base → gerund"),
        (@"\bmarried\s+with\b", "married to", "married with → married to"),
        (@"\bdivided\s+in\s+(two|three|four|five|six|seven|eight|nine|ten|\d+)\b", "divided into $1", "divided in → divided into"),
        (@"\bemphasized\s+on\b", "emphasized", "emphasized on → emphasized"),
        (@"\bdiscusses?\s+about\b", "discusses", "discusses about → discusses"),
        (@"\bdiscussed\s+about\b", "discussed", "discussed about → discussed"),
        (@"\bobjected\s+the\b", "objected to the", "objected the → objected to the"),

        // ──── COLLOCATION / PHRASE FIXES ────
        (@"\blocks?\s+of\s+(experience|evidence|knowledge|skill|confidence)\b", "lacks $1", "lacks of → lacks"),
        (@"\black\s+of\s+of\b", "lack of", "fix double of"),
        (@"\bis\s+consisted\s+of\b", "consists of", "is consisted of → consists of"),
        (@"\bexplained\s+me\b", "explained to me", "explained me → explained to me"),
        (@"\btold\s+that\s+the\b", "said that the", "told that → said that"),
        (@"\basked\s+that\s+where\b", "asked where", "asked that where → asked where"),
        (@"\bdue\s+to\s+the\s+fact\s+that\s+because\b", "because", "redundancy: due to the fact that because → because"),
        (@"\bin\s+order\s+to\s+(proving|showing|demonstrating|testing)\b", "in order to ${1}", "in order to + gerund"),
        (@"\bin\s+order\s+to\s+proving\b", "in order to prove", "in order to proving → prove"),
        (@"\bin\s+order\s+to\s+showing\b", "in order to show", "in order to showing → show"),
        (@"\bwithout\s+to\s+(inform|tell|say|know|ask|see|hear|understand|realize|notice)\b", "without ${1}ing", "without to X → without Xing"),
        (@"\baims\s+at\s+to\b", "aims to", "aims at to → aims to"),

        // ──── VERB FORM AFTER AUXILIARY ────
        (@"\bdidn't\s+knew\b", "didn't know", "didn't knew → didn't know"),
        (@"\bdidn't\s+finished\b", "didn't finish", "didn't finished → didn't finish"),
        (@"\bdidn't\s+saw\b", "didn't see", "didn't saw → didn't see"),
        (@"\bdidn't\s+went\b", "didn't go", "didn't went → didn't go"),
        (@"\bstopped\s+to\s+work\b", "stopped working", "stopped to work → stopped working"),
        (@"\bmade\s+me\s+to\s+(realize|understand|see|know|feel|think|believe)\b", "made me $1", "made me to X → made me X"),
        (@"\bshould\s+to\b", "should", "should to → should"),
        // Note: bare @"\bbecame\b(?=\s*\.)" was removed — it corrupted valid past tense ("She became famous.")
        (@"\bwill\s+became\b", "will become", "will became → will become"),

        // ──── LESS vs FEWER ────
        (@"\bless\s+(people|students|participants|employees|members|items|mistakes|errors|problems|questions|responsibilities|options|opportunities|things)\b", "fewer $1", "less + countable → fewer"),
        (@"\bthere\s+is\s+(less|fewer)\s+(people|participants|students|members|mistakes|errors)\b", "there are fewer $2", "there is less/fewer + plural → there are fewer"),

        // ──── COUNTABLE/UNCOUNTABLE ────
        (@"\btoo\s+much\s+(errors|mistakes|problems|issues|questions|people|students)\b", "too many $1", "too much + countable → too many"),

        // ──── SUBJUNCTIVE PATTERNS (rule-safe ones) ────
        (@"\binsisted\s+that\s+(he|she|it|the\s+\w+)\s+goes\b", "insisted that $1 go", "subjunctive: insisted that X goes → go"),
        (@"\binsisted\s+that\s+(he|she|it|the\s+\w+)\s+comes\b", "insisted that $1 come", "subjunctive: insisted that X comes → come"),
        (@"\bdemanded\s+that\s+(he|she|it|the\s+\w+)\s+leaves\b", "demanded that $1 leave", "subjunctive: demanded that X leaves → leave"),
        (@"\brecommended\s+that\s+(he|she|it|the\s+\w+)\s+studies\b", "recommended that $1 study", "subjunctive: recommended that X studies → study"),
        (@"\brequested\s+that\s+(he|she|it|the\s+\w+)\s+submits\b", "requested that $1 submit", "subjunctive: requested that X submits → submit"),
        (@"\bessential\s+that\s+(every\s+\w+|each\s+\w+|he|she|it|the\s+\w+)\s+submits\b", "essential that $1 submit", "subjunctive: essential that X submits → submit"),
        (@"\bsuggested\s+that\s+(he|she|it|the\s+\w+)\s+should\s+to\b", "suggested that $1 should", "should to → should"),
        (@"\bsuggested\s+him\s+to\b", "suggested that he", "suggested him to → suggested that he"),
        (@"\brecommended\s+that\s+(he|she|it|the\s+\w+)\s+is\b", "recommended that $1 be", "subjunctive: recommended that X is → be"),

        // ──── INVERTED WORD ORDER (safe patterns) ────
        (@"\b[Nn]o\s+sooner\s+(he|she|it|they|we|I)\s+had\b", "No sooner had $1", "no sooner X had → no sooner had X"),
        (@"\b[Hh]ardly\s+(I|he|she|they|we)\s+had\b", "Hardly had $1", "hardly X had → hardly had X"),
        (@"\b[Ss]carcely\s+(they|he|she|we|I)\s+had\b", "Scarcely had $1", "scarcely X had → scarcely had X"),
        (@"\b[Rr]arely\s+(they|he|she|we|I)\s+(encounter|see|find|meet|have|get|do)\b", "Rarely do $1 $2", "rarely X verb → rarely do X verb"),
        (@"\b[Ss]eldom\s+(we|they|he|she|I)\s+(see|find|encounter|meet|have|get|do)\b", "Seldom do $1 $2", "seldom X verb → seldom do X verb"),
        (@"\b[Oo]nly\s+after\s+(.+?),?\s+(they|he|she|we|I)\s+realized\b", "Only after $1, did $2 realize", "only after ... realized → did ... realize"),
        (@"\b[Oo]nly\s+after\s+(.+?),?\s+(they|he|she|we|I)\s+noticed\b", "Only after $1, did $2 notice", "only after ... noticed → did ... notice"),
        (@"\b[Oo]nly\s+after\s+(.+?),?\s+(they|he|she|we|I)\s+saw\b", "Only after $1, did $2 see", "only after ... saw → did ... see"),
        (@"\b[Oo]nly\s+after\s+(.+?),?\s+(they|he|she|we|I)\s+found\b", "Only after $1, did $2 find", "only after ... found → did ... find"),
        (@"\b[Oo]nly\s+after\s+(.+?),?\s+(they|he|she|we|I)\s+knew\b", "Only after $1, did $2 know", "only after ... knew → did ... know"),
        (@"\b[Oo]nly\s+after\s+(.+?),?\s+(they|he|she|we|I)\s+understood\b", "Only after $1, did $2 understand", "only after ... understood → did ... understand"),
        (@"\b[Oo]nly\s+after\s+(.+?),?\s+(they|he|she|we|I)\s+discovered\b", "Only after $1, did $2 discover", "only after ... discovered → did ... discover"),

        // ──── "no sooner...when" → "than" and "hardly...than" → "when" ────
        (@"\b[Nn]o\s+sooner\s+had\s+(.+?)\s+when\b", "No sooner had $1 than", "no sooner had...when → than"),
        (@"\b[Hh]ardly\s+had\s+(.+?)\s+than\b", "Hardly had $1 when", "hardly had...than → when"),
        (@"\b[Ss]carcely\s+had\s+(.+?)\s+than\b", "Scarcely had $1 when", "scarcely had...than → when"),

        // ──── COMPLEX SUBJECT-VERB (safe patterns) ────
        (@"\b(the\s+\w+),\s+along\s+with\s+(\w+\s+\w+),\s+were\b", "$1, along with $2, was", "X, along with Y, were → was"),
        (@"\b(the\s+\w+),\s+along\s+with\s+(the\s+\w+\s+\w+),\s+are\b", "$1, along with $2, is", "X, along with Y, are → is"),
        (@"\b(the\s+\w+)\s+as\s+well\s+as\s+(\w+\s+\w+)\s+were\b", "$1 as well as $2 was", "X as well as Y were → was"),
        (@"\b[Ee]ach\s+participant\s+were\b", "Each participant was", "each participant were → was"),
        (@"\b[Tt]he\s+number\s+of\s+(\w+)\s+(are|have)\b", "The number of $1 has", "the number of X are/have → has"),
        (@"\b[Tt]he\s+number\s+of\s+(\w+\s+\w+)\s+(are|have)\b", "The number of $1 has", "the number of X Y have → has"),
        (@"\b([Tt]he\s+number\s+of\s+[^,.!?]{1,100}?)\s+have\b", "$1 has", "the number of ... have → has"),
        (@"\b([Tt]he\s+number\s+of\s+[^,.!?]{1,100}?)\s+are\b", "$1 is", "the number of ... are → is"),
        (@"\b[Ee]veryone\s+have\b", "Everyone has", "everyone have → has"),

        // ──── NEITHER...NOR / EITHER...OR subject-verb agreement ────
        // "neither X nor Y were" where Y is singular → "was"
        (@"\b[Nn]either\s+the\s+(\w+)\s+nor\s+the\s+(\w+)\s+were\b", "Neither the $1 nor the $2 was", "neither...nor + were → was (both singular)"),
        (@"\b[Nn]either\s+the\s+(\w+)\s+of\s+(.+?)\s+nor\s+the\s+(\w+)\s+were\b", "Neither the $1 of $2 nor the $3 was", "neither...nor + were → was"),

        // ──── SUBJUNCTIVE after essential/important/necessary/vital/crucial ────
        (@"\b[Ii]t\s+is\s+essential\s+that\s+(every\s+\w+|each\s+\w+|he|she|it|they|we|every\s+\w+\s+\w+)\s+completes?\b", "It is essential that $1 complete", "subjunctive: essential that X completes → complete"),
        (@"\b[Ii]t\s+is\s+essential\s+that\s+(every\s+\w+|each\s+\w+|he|she|it|they|we)\s+(submits?|finishes?|reviews?|confirms?|verifies?|provides?|reports?)\b", "It is essential that $1 $2", "subjunctive: essential that → base form"),
        (@"\b[Ii]t\s+is\s+(important|necessary|vital|crucial|imperative|mandatory|required)\s+that\s+(every\s+\w+|each\s+\w+|he|she|they|we)\s+completes?\b", "It is $1 that $2 complete", "subjunctive: important/necessary that X completes → complete"),
        (@"\b[Ii]t\s+is\s+(important|necessary|vital|crucial|imperative)\s+that\s+(every\s+\w+|each\s+\w+|he|she|they|we)\s+submits?\b", "It is $1 that $2 submit", "subjunctive: important that X submits → submit"),

        // one of the few + plural relative clause: use plural verb in "who ..."
        (@"\b([Oo]ne)\s+of\s+the\s+few\s+(\w+)\s+who\s+understands\s+and\s+applies\b", "$1 of the few $2 who understand and apply", "one of the few ... who understands and applies → understand and apply"),
        (@"\b([Oo]ne)\s+of\s+the\s+few\s+(\w+)\s+who\s+understands\b", "$1 of the few $2 who understand", "one of the few ... who understands → understand"),

        // Conditional consistency with clear past-perfect trigger
        (@"\b([Ii]f\s+[^,.!?]+?\s+had\s+been\s+[^,.!?]+?,\s*[^,.!?]+?\s+)(could|would|should|might)\s+be\s+(prevented|avoided|fixed|solved|improved|done|made|found|seen|stopped|resolved|detected|corrected|eliminated|reduced|increased|achieved)\b",
         "$1$2 have been $3", "conditional consistency: had been ... could be → could have been"),
        (@"\b([Hh]ad\s+[^,.!?]+?,\s*[^,.!?]+?)\s+would\s+avoid\b", "$1 would have avoided", "conditional consistency: had ..., would avoid → would have avoided"),

        // ──── THIRD CONDITIONAL: "would [verb]" → "would have [past participle]" ────
        (@"\b([Hh]e|[Ss]he|[Ii]t|[Ww]e|[Tt]hey|[Ii])\s+would\s+(avoid|make|do|go|take|give|see|buy|tell|leave|come|run|use|try|help|start|stop|write|read|pay|lose|win|keep|put|bring|meet|know|show|find|think|say|become|get|choose|build|send|fall|hold|lead|grow|stand|cut|move|set|let|sit|carry|speak|drive|mean|spend|eat|feel|become|begin|break|catch|draw|drink|fly|forget|hang|hurt|lay|lie|ring|rise|seek|shake|shine|sing|sink|sleep|steal|sting|strike|swear|swim|swing|teach|tear|throw|wake|wear|win|wrap)\s+(making|a|an|the|it|that|this|those|these|his|her|my|your|our|their|him|her|us|them|me)\b",
         "$1 would have $2 $3", "third conditional: would + base → would have + base (with object)"),

        // ──── THIRD CONDITIONAL: "could be [verb]" → "could have been [verb]" ────
        (@"\b([Cc]ould|[Ww]ould|[Ss]hould|[Mm]ight)\s+be\s+(prevented|avoided|fixed|solved|improved|done|made|found|seen|stopped|resolved|detected|corrected|eliminated|reduced|increased|achieved)\b",
         "$1 have been $2", "third conditional: could be prevented → could have been prevented"),

        // ──── CONDITIONAL FIXES (safe patterns) ────
        (@"\b[Ii]f\s+(she|he|it|I|you|we|they)\s+would\s+have\s+(\w+ed)\b", "If $1 had $2", "if X would have → if X had"),
        (@"\b[Ii]f\s+(she|he|it|I|you|we|they)\s+would\s+have\s+studied\b", "If $1 had studied", "conditional fix"),
        (@"\b[Ii]\s+wish\s+I\s+was\b", "I wish I were", "I wish I was → were (subjunctive)"),
        (@"\bI\s+would\s+rather\s+you\s+will\b", "I would rather you", "would rather + will → drop will"),
        (@"\b[Oo]nly\s+if\s+you\s+will\s+complete\b", "Only if you complete", "only if + will → present"),

        // ──── GERUND vs INFINITIVE (safe patterns) ────
        (@"\bavoided?\s+to\s+(answer|talk|speak|discuss|work|go|do|say|make|take|give|come)\b", "avoided ${1}ing", "avoided to X → avoided Xing"),
        (@"\blooking\s+forward\s+to\s+(attend|see|meet|hear|visit|go|do|work|play|start|learn|read|write|help)\b", "looking forward to ${1}ing", "forward to + base → gerund"),
        (@"\bcapable\s+of\s+to\b", "capable of", "capable of to → capable of"),

        // ──── MISC ADVANCED PATTERNS ────
        (@"\bworks?\s+hardly\b", "works hard", "works hardly → works hard"),
        // Note: "most unique" no-op rule removed — it made no change
        (@"\b[Bb]eing\s+that\s+(he|she|it|they|we|I)\s+(was|were|is|are)\b", "Since $1 $2", "being that → since"),
        (@"\bsenior\s+than\b", "senior to", "senior than → senior to"),
        (@"\b(he|she|it)\s+know\s+everything\b", "$1 knows everything", "he know → he knows"),
        (@"\bno\s+sooner\s+had\s+(.+?)\s+begun?\s+when\b", "no sooner had $1 begun than", "no sooner...when → than"),
        (@"\b[Hh]ardly\s+had\s+(they|he|she|we|I)\s+began\b", "Hardly had $1 begun", "began → begun with had"),
        (@"\bhad\s+took\b", "had taken", "had took → had taken"),
        (@"\bhad\s+began\b", "had begun", "had began → had begun"),
        // Note: "have submitted their" no-op rule removed
        (@"\bcriteria\s+(for\s+\w+\s+)?is\b", "criteria $1are", "criteria is → criteria are"),
        (@"\b(the|this|that)\s+data\s+suggests?\b", "$1 data suggest", "data suggests → data suggest"),
    };

    // ──────────────────────────── COMMON GRAMMAR MISTAKES ────────────────────────────

    private static readonly (string Wrong, string Right)[] GrammarFixes =
    {
        // Wrong word usage
        ("alot", "a lot"),
        ("abit", "a bit"),
        ("infact", "in fact"),
        ("aswell", "as well"),
        ("incase", "in case"),
        ("infront", "in front"),
        ("ontop", "on top"),
        ("alright", "all right"),
        // Note: "everyday" as an adjective is valid (e.g. "everyday life") — removed to avoid false positives
        ("its a", "it's a"),
        ("its the", "it's the"),
        ("its not", "it's not"),
        ("its very", "it's very"),
        ("its been", "it's been"),
        ("your a", "you're a"),
        ("your the", "you're the"),
        ("your not", "you're not"),
        ("your very", "you're very"),
        ("your welcome", "you're welcome"),
        ("their is", "there is"),
        ("their are", "there are"),
        ("their was", "there was"),
        ("should of", "should have"),
        ("could of", "could have"),
        ("would of", "would have"),
        ("must of", "must have"),
        ("might of", "might have"),
        ("shouldnt of", "shouldn't have"),
        ("couldnt of", "couldn't have"),
        ("wouldnt of", "wouldn't have"),
        ("cant", "can't"),
        ("dont", "don't"),
        ("doesnt", "doesn't"),
        ("didnt", "didn't"),
        ("wont", "won't"),
        ("isnt", "isn't"),
        ("arent", "aren't"),
        ("wasnt", "wasn't"),
        ("werent", "weren't"),
        ("hasnt", "hasn't"),
        ("havent", "haven't"),
        ("hadnt", "hadn't"),
        ("wouldnt", "wouldn't"),
        ("couldnt", "couldn't"),
        ("shouldnt", "shouldn't"),
        ("im", "I'm"),
        ("ive", "I've"),
        // Note: "ill" and "id" are valid English words — removed to avoid false positives ("She is ill", "the id")
        ("theyre", "they're"),
        ("youre", "you're"),
        ("were going", "we're going"),
        ("thats", "that's"),
        ("whats", "what's"),
        ("heres", "here's"),
        ("theres", "there's"),
        ("whos", "who's"),
        ("lets", "let's"),

        // Tense issues
        ("did went", "went"),
        ("more better", "better"),
        ("more worse", "worse"),
        ("most best", "best"),
        ("most worst", "worst"),
    };

    // ──────────────────────────── PROPER NOUNS ────────────────────────────

    private static readonly string[] ProperNouns =
    {
        // Countries
        "Turkey", "Germany", "France", "Italy", "Spain", "Russia", "China", "Japan",
        "Korea", "India", "Brazil", "Mexico", "Canada", "Australia", "Egypt",
        "England", "Scotland", "Ireland", "Poland", "Sweden", "Norway", "Denmark",
        "Finland", "Greece", "Portugal", "Netherlands", "Belgium", "Austria",
        "Switzerland", "Argentina", "Colombia", "Peru", "Chile", "Morocco",
        "Nigeria", "Kenya", "Ethiopia", "Thailand", "Vietnam", "Indonesia",
        "Malaysia", "Philippines", "Pakistan", "Bangladesh", "Iran", "Iraq",
        "Saudi Arabia", "Israel", "Palestine", "Jordan", "Lebanon", "Syria",
        "Ukraine", "Romania", "Hungary", "Czech Republic",

        // Continents
        "Africa", "Asia", "Europe", "America", "Antarctica", "Oceania",

        // Cities
        "Istanbul", "London", "Paris", "Berlin", "Rome", "Madrid", "Moscow",
        "Tokyo", "Beijing", "Shanghai", "Delhi", "Mumbai", "Cairo", "Lagos",
        "New York", "Los Angeles", "Chicago", "Toronto", "Sydney", "Melbourne",
        "Dubai", "Singapore", "Bangkok", "Seoul", "Ankara", "Athens",

        // Languages
        "English", "Turkish", "Arabic", "Spanish", "French", "German",
        "Italian", "Portuguese", "Russian", "Chinese", "Japanese", "Korean",
        "Hindi", "Dutch", "Swedish", "Norwegian", "Danish", "Finnish",
        "Greek", "Polish", "Romanian", "Hungarian", "Czech",

        // Tech
        "Google", "Microsoft", "Apple", "Amazon", "Facebook", "Twitter",
        "Instagram", "WhatsApp", "YouTube", "Netflix", "Spotify",
        "Windows", "Android", "iPhone", "Samsung", "Tesla",
        "ChatGPT", "GitHub", "Linux", "Python", "JavaScript",

        // Days & Months
        "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday",
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",

        // Other
        "Internet", "Earth", "Bible", "Quran", "Christmas", "Easter", "Ramadan",
    };

    // ──────────────────────────── MISSPELLINGS ────────────────────────────

    private static readonly (string Wrong, string Right)[] CommonMisspellings =
    {
        ("definately", "definitely"),
        ("definatly", "definitely"),
        ("recieve", "receive"),
        ("seperate", "separate"),
        ("occured", "occurred"),
        ("untill", "until"),
        ("beleive", "believe"),
        ("wierd", "weird"),
        ("accomodate", "accommodate"),
        ("occurence", "occurrence"),
        ("neccessary", "necessary"),
        ("concious", "conscious"),
        ("goverment", "government"),
        ("independant", "independent"),
        ("refered", "referred"),
        ("begining", "beginning"),
        ("arguement", "argument"),
        ("calender", "calendar"),
        ("committment", "commitment"),
        ("dissapear", "disappear"),
        ("existance", "existence"),
        ("grammer", "grammar"),
        ("harrass", "harass"),
        ("knowlege", "knowledge"),
        ("mispell", "misspell"),
        ("posession", "possession"),
        ("prefered", "preferred"),
        ("tommorow", "tomorrow"),
        ("writting", "writing"),
        ("teh", "the"),
        ("thnk", "think"),
        ("thnig", "thing"),
        ("thier", "their"),
        ("freind", "friend"),
        ("foriegn", "foreign"),
        ("truely", "truly"),
        ("untill", "until"),
        ("beautful", "beautiful"),
        ("beatiful", "beautiful"),
        ("beautifull", "beautiful"),
        ("buisness", "business"),
        ("catagory", "category"),
        ("changable", "changeable"),
        ("collectible", "collectible"),
        ("comeing", "coming"),
        ("consciencious", "conscientious"),
        ("decieve", "deceive"),
        ("develope", "develop"),
        ("diffrence", "difference"),
        ("disapoint", "disappoint"),
        ("embarass", "embarrass"),
        ("exellent", "excellent"),
        ("exersise", "exercise"),
        ("fourty", "forty"),
        ("gaurante", "guarantee"),
        ("happyness", "happiness"),
        ("harasment", "harassment"),
        ("hygeine", "hygiene"),
        ("ignorence", "ignorance"),
        ("immediatly", "immediately"),
        ("independance", "independence"),
        ("jewellry", "jewelry"),
        ("judgement", "judgment"),
        ("liason", "liaison"),
        ("libary", "library"),
        ("maintainance", "maintenance"),
        ("millenium", "millennium"),
        ("minature", "miniature"),
        ("mischievious", "mischievous"),
        ("noticable", "noticeable"),
        ("occassion", "occasion"),
        ("passtime", "pastime"),
        ("perseverence", "perseverance"),
        ("politican", "politician"),
        ("preceed", "precede"),
        ("privelege", "privilege"),
        ("professinal", "professional"),
        ("pronouciation", "pronunciation"),
        ("publically", "publicly"),
        ("realy", "really"),
        ("reccomend", "recommend"),
        ("relevent", "relevant"),
        ("rythm", "rhythm"),
        ("sargent", "sergeant"),
        ("seize", "seize"),
        ("sieze", "seize"),
        ("supercede", "supersede"),
        ("surprize", "surprise"),
        ("temperture", "temperature"),
        ("therefor", "therefore"),
        ("threshhold", "threshold"),
        ("tounge", "tongue"),
        ("vaccuum", "vacuum"),
        ("vehical", "vehicle"),
        ("wether", "whether"),
        ("wich", "which"),
        ("usally", "usually"),
        ("probaly", "probably"),
        ("diffrent", "different"),
        ("intresting", "interesting"),
        // duplicate "goverment" entries removed — already listed above
        ("accross", "across"),
        ("acheive", "achieve"),
        ("adress", "address"),
        ("agressive", "aggressive"),
        ("apparantly", "apparently"),
        ("approxiately", "approximately"),
        ("awfull", "awful"),
        ("basicly", "basically"),
        ("becuase", "because"),
        ("beacuse", "because"),
        ("becasue", "because"),
        ("belive", "believe"),
        ("bizzare", "bizarre"),
        ("completly", "completely"),
        ("congradulations", "congratulations"),
        ("definate", "definite"),
        ("desparate", "desperate"),
        ("dissapointed", "disappointed"),
        ("dilemna", "dilemma"),
        ("documant", "document"),
        ("expierence", "experience"),
        ("enviroment", "environment"),
        ("gaurd", "guard"),
        ("happend", "happened"),
        ("humourous", "humorous"),
        ("incidently", "incidentally"),
        ("irresistable", "irresistible"),
        ("knowlegeable", "knowledgeable"),
        ("neccessity", "necessity"),
        ("occassionally", "occasionally"),
        ("paralell", "parallel"),
        ("parliment", "parliament"),
        ("persistant", "persistent"),
        ("peice", "piece"),
        ("potentialy", "potentially"),
        ("questionaire", "questionnaire"),
        ("reciept", "receipt"),
        ("refrence", "reference"),
        ("religous", "religious"),
        ("resistense", "resistance"),
        ("resturant", "restaurant"),
        ("shedule", "schedule"),
        ("strenght", "strength"),
        ("succesful", "successful"),
        ("successfull", "successful"),
        ("suficient", "sufficient"),
        ("tradegy", "tragedy"),
        // duplicates of "untill" and "wierd" removed — already listed above
        ("useing", "using"),
        ("yearh", "yeah"),
    };

    #endregion
}

/// <summary>Result from rule-based engine processing.</summary>
public record RuleBasedResult(string CorrectedText, IReadOnlyList<RuleCorrection> Corrections);

/// <summary>A single rule-based correction.</summary>
public record RuleCorrection(string Description, CorrectionType Type, float Confidence);

/// <summary>Types of corrections applied.</summary>
public enum CorrectionType
{
    None,
    RuleBased,
    Grammar,
    Style,
    Clarity,
    Tone
}
