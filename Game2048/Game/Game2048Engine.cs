namespace Game2048.Game;

public enum Direction { Up, Down, Left, Right }

/// <summary>One tile on the board. The Id stays the same while a tile slides, so the UI can animate it.</summary>
public sealed class Tile
{
    public int Id { get; init; }
    public int Value { get; set; }
    public int Row { get; set; }
    public int Col { get; set; }
    public bool IsNew { get; set; }
    public bool Merged { get; set; }
}

/// <summary>Pure 2048 game logic (no UI). Mirrors PongEngine: the page drives it, JS only delivers input.</summary>
public sealed class Game2048Engine
{
    public const int Target = 2048;

    private readonly Random _rng;
    private Tile?[,] _grid;
    private int _nextId;

    public int Size { get; }
    public int Score { get; private set; }
    public int Moves { get; private set; }
    public bool Won { get; private set; }
    public bool KeepPlaying { get; set; }
    public bool GameOver { get; private set; }
    public IReadOnlyList<Tile> Tiles => _tiles;
    private readonly List<Tile> _tiles = [];

    public Game2048Engine(int size = 4, int? seed = null)
    {
        Size = size;
        _rng = seed is null ? new Random() : new Random(seed.Value);
        _grid = new Tile?[size, size];
        Reset();
    }

    public void Reset()
    {
        _grid = new Tile?[Size, Size];
        _tiles.Clear();
        Score = 0; Moves = 0; Won = false; KeepPlaying = false; GameOver = false;
        Spawn(); Spawn();
    }

    /// <summary>Slides all tiles; returns true when anything moved.</summary>
    public bool Move(Direction dir)
    {
        if (GameOver || (Won && !KeepPlaying)) return false;

        foreach (var t in _tiles) { t.IsNew = false; t.Merged = false; }
        _tiles.RemoveAll(t => _grid[t.Row, t.Col] != t);

        bool moved = false;
        for (int line = 0; line < Size; line++)
        {
            var cells = LineCells(dir, line);
            var row = cells.Select(c => _grid[c.r, c.c]).Where(t => t is not null).Cast<Tile>().ToList();
            foreach (var (r, c) in cells) _grid[r, c] = null;

            int pos = 0;
            Tile? last = null;
            foreach (var t in row)
            {
                if (last is not null && !last.Merged && last.Value == t.Value)
                {
                    // Slide onto the other tile; it stays in Tiles this turn so the UI can animate the merge.
                    last.Value *= 2;
                    last.Merged = true;
                    Score += last.Value;
                    if (last.Value >= Target) Won = true;
                    (t.Row, t.Col) = (last.Row, last.Col);
                    moved = true;
                    continue;
                }
                var (nr, nc) = cells[pos++];
                if (t.Row != nr || t.Col != nc) moved = true;
                (t.Row, t.Col) = (nr, nc);
                _grid[nr, nc] = t;
                last = t;
            }
        }

        if (moved)
        {
            Moves++;
            Spawn();
            GameOver = !CanMove();
        }
        return moved;
    }

    /// <summary>Tiles sorted so the merged-away ones are drawn underneath.</summary>
    public IEnumerable<Tile> RenderTiles() => _tiles.OrderBy(t => _grid[t.Row, t.Col] == t ? 1 : 0);

    public int MaxTile => _tiles.Count == 0 ? 0 : _tiles.Max(t => t.Value);

    private List<(int r, int c)> LineCells(Direction dir, int line)
    {
        var list = new List<(int, int)>(Size);
        for (int i = 0; i < Size; i++)
            list.Add(dir switch
            {
                Direction.Left => (line, i),
                Direction.Right => (line, Size - 1 - i),
                Direction.Up => (i, line),
                _ => (Size - 1 - i, line),
            });
        return list;
    }

    private void Spawn()
    {
        var empty = new List<(int r, int c)>();
        for (int r = 0; r < Size; r++)
            for (int c = 0; c < Size; c++)
                if (_grid[r, c] is null) empty.Add((r, c));
        if (empty.Count == 0) return;
        var (er, ec) = empty[_rng.Next(empty.Count)];
        var tile = new Tile { Id = ++_nextId, Value = _rng.NextDouble() < 0.9 ? 2 : 4, Row = er, Col = ec, IsNew = true };
        _grid[er, ec] = tile;
        _tiles.Add(tile);
    }

    private bool CanMove()
    {
        for (int r = 0; r < Size; r++)
            for (int c = 0; c < Size; c++)
            {
                var t = _grid[r, c];
                if (t is null) return true;
                if (c + 1 < Size && _grid[r, c + 1]?.Value == t.Value) return true;
                if (r + 1 < Size && _grid[r + 1, c]?.Value == t.Value) return true;
            }
        return false;
    }
}
