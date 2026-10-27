using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BirdTrozei.Core;

namespace PuzzleGen
{
    /// <summary>
    /// Publishes Trozei daily puzzles as static JSON files. Uses the game's own Core code, so version 1 of a day is
    /// exactly the puzzle the app would generate offline.
    ///
    ///   publish     make sure the next N days exist (never changes a published day), rebuild upcoming.json
    ///   regenerate  replace one future day with a new version (live or past days need --force)
    ///   validate    check every published puzzle; exits 1 on problems
    ///
    /// Common options: --site DIR (folder with puzzles/ and upcoming.json), --words DIR (words.txt, targets.txt),
    /// --today yyyy-MM-dd (defaults to the UTC date).
    /// </summary>
    static class Program
    {
        const int DefaultDays = 14;
        const int PastDaysInFeed = 7;

        static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        sealed class PuzzleFile
        {
            public string Date { get; set; }
            public int Version { get; set; }
            public string Letters { get; set; }
            public string[] Targets { get; set; }
            public int Seed { get; set; }
            public string Note { get; set; }
        }

        sealed class Feed
        {
            public string Generated { get; set; }
            public List<PuzzleFile> Puzzles { get; set; }
        }

        static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0) return Usage();
                var opts = Options(args.Skip(1).ToArray());
                string site = Get(opts, "site", "docs");
                string wordsDir = Get(opts, "words", FindWords());
                DateTime today = opts.TryGetValue("today", out var t) ? ParseDate(t) : DateTime.UtcNow.Date;
                var words = WordList.FromText(File.ReadAllText(Path.Combine(wordsDir, "words.txt")));
                var targets = File.ReadAllLines(Path.Combine(wordsDir, "targets.txt")).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
                var rules = new LetterSetRules();

                switch (args[0])
                {
                    case "publish":
                        int days = opts.TryGetValue("days", out var d) ? int.Parse(d) : DefaultDays;
                        for (int i = 0; i < days; i++)
                        {
                            var date = today.AddDays(i);
                            if (File.Exists(PuzzlePath(site, date))) continue;
                            var p = DailyPuzzle.Generate(date, 1, rules, words, targets);
                            Write(site, p, null);
                            Console.WriteLine($"new      {p.Date} v1  {Describe(p)}");
                        }

                        WriteFeed(site, today, days);
                        return Check(site, words, rules) ? 0 : 1;

                    case "regenerate":
                    {
                        var date = ParseDate(Get(opts, "date", null) ?? throw new ArgumentException("--date is required"));
                        bool force = opts.ContainsKey("force");
                        if (date <= today && !force)
                            throw new ArgumentException($"{DailyPuzzle.DateKey(date)} is live or past; players may already have scores on it. Use --force to replace it anyway.");
                        var old = Read(PuzzlePath(site, date));
                        int version = (old?.Version ?? 0) + 1;
                        var p = DailyPuzzle.Generate(date, version, rules, words, targets);
                        Write(site, p, Get(opts, "reason", null));
                        Console.WriteLine($"replaced {p.Date} v{p.Version}  {Describe(p)}" + (old != null ? $"  (was v{old.Version}: {string.Join(", ", old.Targets)})" : ""));
                        if (date <= today) Console.WriteLine("warning: this day is live; reset its leaderboard so old and new scores don't mix.");
                        WriteFeed(site, today, DefaultDays);
                        return Check(site, words, rules) ? 0 : 1;
                    }

                    case "validate":
                        WriteFeedIfMissing(site, today);
                        return Check(site, words, rules) ? 0 : 1;

                    default:
                        return Usage();
                }
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is FormatException)
            {
                Console.Error.WriteLine($"error: {e.Message}");
                return 2;
            }
        }

        static string Describe(DailyPuzzle p) => $"{string.Join(", ", p.Targets).ToUpperInvariant(),-22} letters {p.Letters.ToUpperInvariant()}";

        static string PuzzlePath(string site, DateTime date) => Path.Combine(site, "puzzles", DailyPuzzle.DateKey(date) + ".json");

        static void Write(string site, DailyPuzzle p, string note)
        {
            ParseDate(p.Date);
            Directory.CreateDirectory(Path.Combine(site, "puzzles"));
            var file = new PuzzleFile { Date = p.Date, Version = p.Version, Letters = p.Letters, Targets = p.Targets, Seed = p.Seed, Note = note };
            File.WriteAllText(PuzzlePath(site, ParseDate(p.Date)), JsonSerializer.Serialize(file, Json) + "\n");
        }

        static PuzzleFile Read(string path) => File.Exists(path) ? JsonSerializer.Deserialize<PuzzleFile>(File.ReadAllText(path), Json) : null;

        /// <summary>upcoming.json: last week plus the coming days, which is all the app downloads.</summary>
        static void WriteFeed(string site, DateTime today, int days)
        {
            var list = new List<PuzzleFile>();
            for (int i = -PastDaysInFeed; i < Math.Max(days, DefaultDays); i++)
            {
                var p = Read(PuzzlePath(site, today.AddDays(i)));
                if (p != null) list.Add(p);
            }

            // Leave the file (and its timestamp) alone when the puzzles haven't changed, so runs with nothing new commit nothing.
            string path = Path.Combine(site, "upcoming.json");
            if (File.Exists(path))
            {
                var existing = JsonSerializer.Deserialize<Feed>(File.ReadAllText(path), Json);
                if (existing?.Puzzles != null && JsonSerializer.Serialize(existing.Puzzles, Json) == JsonSerializer.Serialize(list, Json))
                {
                    Console.WriteLine($"feed     {list.Count} puzzles in upcoming.json (unchanged)");
                    return;
                }
            }

            var feed = new Feed { Generated = DateTime.UtcNow.ToString("o"), Puzzles = list };
            File.WriteAllText(path, JsonSerializer.Serialize(feed, Json) + "\n");
            Console.WriteLine($"feed     {list.Count} puzzles in upcoming.json");
        }

        static void WriteFeedIfMissing(string site, DateTime today)
        {
            if (!File.Exists(Path.Combine(site, "upcoming.json"))) WriteFeed(site, today, DefaultDays);
        }

        /// <summary>Validates every puzzle file (including hand edits) and the feed.</summary>
        static bool Check(string site, IWordList words, LetterSetRules rules)
        {
            bool ok = true;
            var dir = Path.Combine(site, "puzzles");
            var files = Directory.Exists(dir) ? Directory.GetFiles(dir, "*.json").OrderBy(f => f).ToArray() : new string[0];
            foreach (var path in files)
            {
                PuzzleFile f;
                try { f = Read(path); }
                catch (JsonException e)
                {
                    Console.WriteLine($"PROBLEM  {Path.GetFileName(path)}: not valid JSON ({e.Message})");
                    ok = false;
                    continue;
                }

                var p = new DailyPuzzle { Date = f.Date, Version = f.Version, Letters = f.Letters, Targets = f.Targets, Seed = f.Seed };
                var problems = p.Validate(words, rules.MinPlayableWords / 2);
                if (Path.GetFileNameWithoutExtension(path) != f.Date) problems.Add("file name doesn't match its date");
                foreach (var problem in problems) Console.WriteLine($"PROBLEM  {Path.GetFileName(path)}: {problem}");
                ok &= problems.Count == 0;
            }

            Console.WriteLine(ok ? $"ok       {files.Length} puzzle files valid" : "validation failed");
            return ok;
        }

        static DateTime ParseDate(string s) =>
            DailyPuzzle.TryParseDate(s, out var d) ? d : throw new FormatException($"'{s}' is not a date (yyyy-MM-dd)");

        static string FindWords()
        {
            foreach (var candidate in new[] { "words", "generator/words", "../../Assets/_Project/Resources/Words", "Assets/_Project/Resources/Words" })
                if (File.Exists(Path.Combine(candidate, "words.txt"))) return candidate;
            return "words";
        }

        static Dictionary<string, string> Options(string[] args)
        {
            var opts = new Dictionary<string, string>();
            for (int i = 0; i < args.Length; i++)
            {
                if (!args[i].StartsWith("--")) throw new ArgumentException($"unexpected argument '{args[i]}'");
                string key = args[i].Substring(2);
                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--");
                opts[key] = hasValue ? args[++i] : "true";
            }

            return opts;
        }

        static string Get(Dictionary<string, string> opts, string key, string fallback) =>
            opts.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

        static int Usage()
        {
            Console.WriteLine("usage: PuzzleGen publish|regenerate|validate [--site DIR] [--words DIR] [--today yyyy-MM-dd]");
            Console.WriteLine("       publish    [--days 14]");
            Console.WriteLine("       regenerate --date yyyy-MM-dd [--reason TEXT] [--force]");
            return 2;
        }
    }
}
