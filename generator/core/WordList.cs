using System;
using System.Collections.Generic;

namespace BirdTrozei.Core
{
    public interface IWordList
    {
        /// <summary>True if <paramref name="word"/> (lower case) is a valid word.</summary>
        bool Contains(string word);
        int MaxLength { get; }
        /// <summary>Every word in the list.</summary>
        IEnumerable<string> All { get; }
    }

    /// <summary>In-memory dictionary of lower-case a-z words.</summary>
    public sealed class WordList : IWordList
    {
        readonly HashSet<string> words = new HashSet<string>(StringComparer.Ordinal);

        public int MaxLength { get; private set; }
        public int Count => words.Count;

        public WordList(IEnumerable<string> source)
        {
            foreach (var raw in source)
            {
                var w = raw?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(w)) continue;
                if (words.Add(w) && w.Length > MaxLength) MaxLength = w.Length;
            }
        }

        public static WordList FromText(string text) =>
            new WordList(text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries));

        public bool Contains(string word) => word != null && words.Contains(word);

        public IEnumerable<string> All => words;
    }
}
