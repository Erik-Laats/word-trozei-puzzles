using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace BirdTrozei.Core
{
    /// <summary>How many letters of each kind a run's letter set gets.</summary>
    public sealed class LetterSetRules
    {
        public int Vowels = 4;
        /// <summary>1-point consonants: L N R S T.</summary>
        public int Common = 4;
        /// <summary>2-4 point consonants: B C D F G H M P V W Y.</summary>
        public int Mid = 2;
        /// <summary>5-10 point letters: J K Q X Z.</summary>
        public int Hard = 1;
        /// <summary>Minimum dictionary words (3-7 letters) spellable from the set, so the board stays playable.</summary>
        public int MinPlayableWords = 400;
        public int Attempts = 60;

        /// <summary>How often each letter of a kind is dealt, relative to the others (vowels most, tough letters least).</summary>
        public int VowelWeight = 7;
        public int CommonWeight = 6;
        public int MidWeight = 3;
        public int HardWeight = 1;

        public int WeightOf(int letter)
        {
            var tier = LetterSets.TierOf(letter);
            return tier == LetterSets.VowelTier ? VowelWeight
                : tier == LetterSets.CommonTier ? CommonWeight
                : tier == LetterSets.MidTier ? MidWeight
                : HardWeight;
        }
    }

    /// <summary>A run's letters and the three target words spelled only from them.</summary>
    public sealed class LetterSet
    {
        public int[] Letters;
        public string[] Targets;
        /// <summary>Dictionary words (3-7 letters) that can be spelled from the set.</summary>
        public int PlayableWords;
        /// <summary>The letter this set dropped from the one before (endless), or -1. It won't come straight back.</summary>
        public int Retired = -1;

        public int Mask => LetterSets.MaskOf(Letters);

        /// <summary>
        /// Bag weights by kind of letter (zero for letters outside the set): the easier the letter, the more often
        /// it's dealt, and vowels most of all.
        /// </summary>
        public int[] Weights(LetterSetRules rules)
        {
            var w = new int[Core.Letters.Count];
            foreach (int l in Letters) w[l] = Math.Max(1, rules.WeightOf(l));
            return w;
        }

        public bool Contains(int letter) => Array.IndexOf(Letters, letter) >= 0;
    }

    /// <summary>
    /// Picks a small, balanced set of letters for a run (a few vowels, some common consonants, one or two
    /// tougher letters, by Scrabble value) and three target words made only from those letters. Endless
    /// stages evolve the set one letter at a time.
    /// </summary>
    public static class LetterSets
    {
        public static readonly int[] VowelTier = Tier("aeiou");
        public static readonly int[] CommonTier = Tier("lnrst");
        public static readonly int[] MidTier = Tier("bcdfghmpvwy");
        public static readonly int[] HardTier = Tier("jkqxz");

        public const int TargetCount = 3;

        static readonly ConditionalWeakTable<IWordList, int[]> MaskCache = new ConditionalWeakTable<IWordList, int[]>();

        static int[] Tier(string letters)
        {
            var t = new int[letters.Length];
            for (int i = 0; i < letters.Length; i++) t[i] = Letters.Index(letters[i]);
            return t;
        }

        /// <summary>Bit mask of the tile letters a word needs, or -1 if it can't be built from tiles (a q without u).</summary>
        public static int WordMask(string word)
        {
            int mask = 0;
            for (int i = 0; i < word.Length; i++)
            {
                int l = Letters.Index(word[i]);
                if (l < 0 || l >= Letters.Count) return -1;
                if (l == Letters.Q)
                {
                    if (i + 1 >= word.Length || word[i + 1] != 'u') return -1;
                    i++; // the Qu tile covers both letters
                }

                mask |= 1 << l;
            }

            return mask;
        }

        public static int MaskOf(IEnumerable<int> letters)
        {
            int mask = 0;
            foreach (int l in letters) mask |= 1 << l;
            return mask;
        }

        public static bool Fits(string word, int setMask)
        {
            int m = WordMask(word);
            return m >= 0 && (m & ~setMask) == 0;
        }

        static int[] Masks(IWordList words) => MaskCache.GetValue(words, w =>
        {
            var list = new List<int>();
            foreach (var word in w.All)
                if (word.Length >= 3 && word.Length <= 7)
                {
                    int m = WordMask(word);
                    if (m >= 0) list.Add(m);
                }

            return list.ToArray();
        });

        public static int CountPlayable(IWordList words, int setMask)
        {
            int n = 0;
            foreach (int m in Masks(words))
                if ((m & ~setMask) == 0) n++;
            return n;
        }

        /// <summary>A fresh letter set (daily puzzle, or the start of an endless run).</summary>
        public static LetterSet Generate(Random rng, LetterSetRules rules, IWordList words, string[] targetPool)
        {
            LetterSet best = null;
            for (int attempt = 0; attempt < rules.Attempts; attempt++)
            {
                var letters = new List<int>();
                PickFrom(rng, VowelTier, rules.Vowels, letters);
                PickFrom(rng, CommonTier, rules.Common, letters);
                PickFrom(rng, MidTier, rules.Mid, letters);
                PickFrom(rng, HardTier, rules.Hard, letters);
                var candidate = Evaluate(rng, letters, words, targetPool);
                if (candidate == null) continue;
                if (candidate.PlayableWords >= rules.MinPlayableWords) return candidate;
                if (best == null || candidate.PlayableWords > best.PlayableWords) best = candidate;
            }

            return best ?? Fallback(rng, words, targetPool);
        }

        /// <summary>
        /// Next endless stage: swap one letter for a new one of the same kind (vowel for vowel, tough for tough),
        /// keeping the set playable and picking three new targets. When <paramref name="rules"/> allows more tough
        /// letters than the set has, a consonant is swapped for a tough letter instead.
        /// </summary>
        public static LetterSet Evolve(Random rng, LetterSet previous, LetterSetRules rules, IWordList words, string[] targetPool)
        {
            LetterSet best = null;
            for (int attempt = 0; attempt < rules.Attempts; attempt++)
            {
                var letters = new List<int>(previous.Letters);
                int hardNow = letters.FindAll(l => TierOf(l) == HardTier).Count;

                int removed;
                int[] addFrom;
                if (hardNow < rules.Hard)
                {
                    removed = RandomOf(rng, letters, l => TierOf(l) == CommonTier || TierOf(l) == MidTier);
                    addFrom = HardTier;
                }
                else if (hardNow > rules.Hard)
                {
                    removed = RandomOf(rng, letters, l => TierOf(l) == HardTier);
                    addFrom = MidTier;
                }
                else
                {
                    // Vowels change now and then; consonants most stages.
                    removed = rng.Next(4) == 0
                        ? RandomOf(rng, letters, l => TierOf(l) == VowelTier)
                        : RandomOf(rng, letters, l => TierOf(l) != VowelTier);
                    addFrom = removed >= 0 ? TierOf(removed) : null;
                }

                if (removed < 0) continue;
                letters.Remove(removed);

                var options = new List<int>();
                foreach (int l in addFrom)
                    if (!letters.Contains(l) && l != removed && l != previous.Retired) options.Add(l);
                if (options.Count == 0) continue;
                letters.Add(options[rng.Next(options.Count)]);

                var candidate = Evaluate(rng, letters, words, targetPool);
                if (candidate == null) continue;
                candidate.Retired = removed;
                if (candidate.PlayableWords >= rules.MinPlayableWords) return candidate;
                if (best == null || candidate.PlayableWords > best.PlayableWords) best = candidate;
            }

            // Couldn't find a good swap: keep the letters, change the targets.
            return best ?? Evaluate(rng, new List<int>(previous.Letters), words, targetPool) ?? previous;
        }

        static LetterSet Evaluate(Random rng, List<int> letters, IWordList words, string[] targetPool)
        {
            letters.Sort();
            int mask = MaskOf(letters);
            var fitting = new List<string>();
            foreach (var t in targetPool)
                if (Fits(t, mask)) fitting.Add(t);
            if (fitting.Count < TargetCount) return null;

            return new LetterSet
            {
                Letters = letters.ToArray(),
                Targets = PickTargets(rng, fitting),
                PlayableWords = CountPlayable(words, mask),
            };
        }

        /// <summary>Three different targets; prefer ones that don't all share the same letters.</summary>
        static string[] PickTargets(Random rng, List<string> fitting)
        {
            var picks = new List<string>();
            int guard = 0;
            while (picks.Count < TargetCount && guard++ < 500)
            {
                string w = fitting[rng.Next(fitting.Count)];
                if (picks.Contains(w)) continue;
                // Avoid near-anagrams of an earlier pick when there's a choice.
                if (guard < 200 && picks.Exists(p => WordMask(p) == WordMask(w))) continue;
                picks.Add(w);
            }

            return picks.ToArray();
        }

        public static int[] TierOf(int letter) =>
            Array.IndexOf(VowelTier, letter) >= 0 ? VowelTier
            : Array.IndexOf(CommonTier, letter) >= 0 ? CommonTier
            : Array.IndexOf(MidTier, letter) >= 0 ? MidTier
            : HardTier;

        static int RandomOf(Random rng, List<int> letters, Predicate<int> match)
        {
            var found = letters.FindAll(match);
            return found.Count == 0 ? -1 : found[rng.Next(found.Count)];
        }

        static void PickFrom(Random rng, int[] tier, int count, List<int> into)
        {
            // Weighted by Scrabble frequency, so E/A/N/R/T show up more often than U/L.
            var pool = new List<int>(tier);
            for (int n = 0; n < count && pool.Count > 0; n++)
            {
                int total = 0;
                foreach (int l in pool) total += Letters.DefaultWeights[l];
                int roll = rng.Next(total);
                foreach (int l in pool)
                {
                    roll -= Letters.DefaultWeights[l];
                    if (roll >= 0) continue;
                    into.Add(l);
                    pool.Remove(l);
                    break;
                }
            }
        }

        /// <summary>Never fails: the whole alphabet with any three targets.</summary>
        static LetterSet Fallback(Random rng, IWordList words, string[] targetPool)
        {
            var all = new List<int>();
            for (int i = 0; i < Letters.Count; i++) all.Add(i);
            return Evaluate(rng, all, words, targetPool) ?? new LetterSet { Letters = all.ToArray(), Targets = new string[0] };
        }
    }
}
