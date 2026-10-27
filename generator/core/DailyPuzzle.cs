using System;
using System.Collections.Generic;
using System.Globalization;

namespace BirdTrozei.Core
{
    /// <summary>
    /// One day's puzzle: its letters, three target words and the seed for the tile deal. Version 1 of a day is
    /// exactly what the app generates offline from the date; a regenerated day gets a higher version and a
    /// different (but still deterministic) roll. The puzzle publisher and the app share this code.
    /// </summary>
    public sealed class DailyPuzzle
    {
        public const string DateFormat = "yyyy-MM-dd";

        public string Date;
        public int Version = 1;
        /// <summary>The set's tile letters as text, one character per tile ("q" is the Qu tile), e.g. "aeilnoqrst".</summary>
        public string Letters;
        public string[] Targets;
        public int Seed;

        /// <summary>Same number for every player on the same UTC day.</summary>
        public static int DateSeed(DateTime utcDate) => utcDate.Year * 10000 + utcDate.Month * 100 + utcDate.Day;

        public static string DateKey(DateTime utcDate) => utcDate.ToString(DateFormat, CultureInfo.InvariantCulture);

        public static bool TryParseDate(string key, out DateTime date) =>
            DateTime.TryParseExact(key, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

        /// <summary>Builds a day's puzzle. The same date, version and rules always give the same puzzle.</summary>
        public static DailyPuzzle Generate(DateTime utcDate, int version, LetterSetRules rules, IWordList words, string[] targetPool)
        {
            version = Math.Max(1, version);
            int dateSeed = DateSeed(utcDate);
            var rng = new Random(unchecked(dateSeed * 31 + 7 + (version - 1) * 7919));
            var set = LetterSets.Generate(rng, rules, words, targetPool);
            return new DailyPuzzle
            {
                Date = DateKey(utcDate),
                Version = version,
                Letters = LettersToText(set.Letters),
                Targets = set.Targets,
                Seed = unchecked(dateSeed + (version - 1) * 100003),
            };
        }

        public LetterSet ToLetterSet(IWordList words)
        {
            var letters = TextToLetters(Letters);
            return new LetterSet
            {
                Letters = letters,
                Targets = Targets,
                PlayableWords = LetterSets.CountPlayable(words, LetterSets.MaskOf(letters)),
            };
        }

        /// <summary>Problems that would make the puzzle unplayable or unfair; empty when it's fine.</summary>
        public List<string> Validate(IWordList words, int minPlayableWords = 0)
        {
            var problems = new List<string>();
            if (!TryParseDate(Date, out _)) problems.Add($"bad date '{Date}'");
            if (Version < 1) problems.Add("version must be 1 or more");

            int[] letters = null;
            try { letters = TextToLetters(Letters); }
            catch (ArgumentException e) { problems.Add(e.Message); }

            if (letters != null)
            {
                if (letters.Length < 6 || letters.Length > 16) problems.Add($"{letters.Length} letters (expected 6-16)");
                bool vowel = false;
                foreach (int l in letters) vowel |= Core.Letters.IsVowel(l);
                if (!vowel) problems.Add("no vowels");
            }

            if (Targets == null || Targets.Length != LetterSets.TargetCount)
                problems.Add($"expected {LetterSets.TargetCount} target words");
            else if (letters != null)
            {
                int mask = LetterSets.MaskOf(letters);
                var seen = new HashSet<string>();
                foreach (var t in Targets)
                {
                    if (string.IsNullOrEmpty(t)) { problems.Add("empty target word"); continue; }
                    if (!seen.Add(t)) problems.Add($"target '{t}' is repeated");
                    if (!words.Contains(t)) problems.Add($"target '{t}' is not in the dictionary");
                    if (!LetterSets.Fits(t, mask)) problems.Add($"target '{t}' uses letters outside the set");
                }

                if (minPlayableWords > 0)
                {
                    int playable = LetterSets.CountPlayable(words, mask);
                    if (playable < minPlayableWords) problems.Add($"only {playable} playable words (need {minPlayableWords})");
                }
            }

            return problems;
        }

        public static string LettersToText(int[] letters)
        {
            var chars = new char[letters.Length];
            for (int i = 0; i < letters.Length; i++) chars[i] = (char)('a' + letters[i]);
            return new string(chars);
        }

        public static int[] TextToLetters(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new ArgumentException("no letters");
            var list = new List<int>();
            foreach (char c in text.ToLowerInvariant())
            {
                int l = c - 'a';
                if (l < 0 || l >= Core.Letters.Count) throw new ArgumentException($"'{c}' is not a letter");
                if (list.Contains(l)) throw new ArgumentException($"letter '{c}' is repeated");
                list.Add(l);
            }

            list.Sort();
            return list.ToArray();
        }
    }
}
