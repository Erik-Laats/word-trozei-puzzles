namespace BirdTrozei.Core
{
    public enum PieceKind
    {
        Letter,
        /// <summary>Blank tile: stands for any letter (even two different letters in two crossing words).</summary>
        Blank,
    }

    /// <summary>A single letter tile on the board or in a spawn queue.</summary>
    public sealed class Piece
    {
        public const int BlankType = -1;

        public readonly int Id;
        public readonly PieceKind Kind;
        /// <summary>Letter index 0-25 (A-Z; Q is the "Qu" tile), or <see cref="BlankType"/>.</summary>
        public readonly int TypeId;

        /// <summary>
        /// How far above its cell the tile currently is (0..1+), so falling is smooth in the rules
        /// themselves: a tile only counts as landed once it has visibly touched down.
        /// </summary>
        public float Offset;

        /// <summary>Resting on the floor or on other landed tiles. Only landed tiles can spell words.</summary>
        public bool Landed;

        public Piece(int id, PieceKind kind, int typeId)
        {
            Id = id;
            Kind = kind;
            TypeId = kind == PieceKind.Blank ? BlankType : typeId;
        }

        public bool IsBlank => Kind == PieceKind.Blank;

        /// <summary>Lower-case letters this tile contributes to a word ("qu" for Q), or "" for a blank.</summary>
        public string Text => IsBlank ? "" : Letters.Text(TypeId);

        public override string ToString() => IsBlank ? $"#{Id}:_" : $"#{Id}:{Letters.Display(TypeId)}";
    }

    public readonly struct Cell
    {
        public readonly int X;
        public readonly int Y;

        public Cell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public override string ToString() => $"({X},{Y})";
    }

    /// <summary>Letter helpers. Tiles are indexed A=0 .. Z=25; the Q tile always reads "Qu".</summary>
    public static class Letters
    {
        public const int Count = 26;
        public const int Q = 16;

        /// <summary>Scrabble tile distribution (blanks handled separately): about 42% vowels.</summary>
        public static readonly int[] DefaultWeights =
        {
            9, 2, 2, 4, 12, 2, 3, 2, 9, 1, 1, 4, 2, 6, 8, 2, 1, 6, 4, 6, 4, 2, 2, 1, 2, 1,
        };

        public static int Index(char c) => char.ToLowerInvariant(c) - 'a';

        /// <summary>Lower-case text a tile adds to a word.</summary>
        public static string Text(int letter) => letter == Q ? "qu" : ((char)('a' + letter)).ToString();

        /// <summary>Text printed on the tile.</summary>
        public static string Display(int letter) => letter == Q ? "Qu" : ((char)('A' + letter)).ToString();

        public static bool IsVowel(int letter) => letter == 0 || letter == 4 || letter == 8 || letter == 14 || letter == 20;
    }
}
