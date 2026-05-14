namespace Sepp2048Web.Services;

public sealed class GameLobbyService
{
    public const int BoardSize = 4;

    private readonly object sync = new();
    private readonly Random random = new();
    private readonly List<Player> players = new();
    private int[,] board = new int[BoardSize, BoardSize];

    public event Action? Changed;

    public IReadOnlyList<Player> Players
    {
        get
        {
            lock (sync)
            {
                return players.ToArray();
            }
        }
    }

    public int[,] Board
    {
        get
        {
            lock (sync)
            {
                return (int[,])board.Clone();
            }
        }
    }

    // Winning score for the game (points required to win). Default is 2048.
    public int WinningScore { get; private set; } = 2048;
    public bool Started { get; private set; }
    public bool HasWon { get; private set; }
    public bool IsGameOver { get; private set; }
    public Guid CurrentPlayerId { get; private set; }

    public Player AddPlayer(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new ArgumentException("Naam mag niet leeg zijn.", nameof(name));
        }

        lock (sync)
        {
            if (Started)
            {
                throw new InvalidOperationException("Het spel is al gestart.");
            }

            if (players.Any(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Deze naam is al in gebruik.");
            }

            var player = new Player(Guid.NewGuid(), trimmed);
            players.Add(player);
            NotifyChanged();
            return player;
        }
    }

    public void RemovePlayer(Guid playerId)
    {
        lock (sync)
        {
            var index = players.FindIndex(p => p.Id == playerId);
            if (index < 0)
            {
                return;
            }

            players.RemoveAt(index);

            if (Started && players.Count == 0)
            {
                ResetCore();
            }
            else if (Started && CurrentPlayerId == playerId)
            {
                var next = index % players.Count;
                CurrentPlayerId = players[next].Id;
            }

            NotifyChanged();
        }
    }

    public void StartGame(Guid playerId, int winningScore = 2048)
    {
        lock (sync)
        {
            if (!players.Any(p => p.Id == playerId))
            {
                return;
            }

            if (players.Count < 1)
            {
                return;
            }

            Array.Clear(board);
            // set the winning score for this game and reset player scores
            WinningScore = winningScore > 0 ? winningScore : 2048;

            for (var i = 0; i < players.Count; i++)
            {
                players[i] = players[i] with { Score = 0, HighestTile = 0 };
            }

            HasWon = false;
            IsGameOver = false;
            Started = true;
            CurrentPlayerId = players[0].Id;

            AddRandomTile();
            AddRandomTile();
            UpdateGameState();
            NotifyChanged();
        }
    }

    public void ResetLobby()
    {
        lock (sync)
        {
            players.Clear();
            ResetCore();
            NotifyChanged();
        }
    }

    public bool TryMove(Guid playerId, MoveDirection direction)
    {
        lock (sync)
        {
            if (!Started || IsGameOver)
            {
                return false;
            }

            if (CurrentPlayerId != playerId)
            {
                return false;
            }

            if (!ApplyMove(direction))
            {
                return false;
            }

            AddRandomTile();
            UpdateGameState();
            AdvanceTurn();
            NotifyChanged();
            return true;
        }
    }

    public Player? GetCurrentPlayer()
    {
        lock (sync)
        {
            return players.FirstOrDefault(p => p.Id == CurrentPlayerId);
        }
    }

    private void AdvanceTurn()
    {
        if (players.Count == 0)
        {
            return;
        }

        var index = players.FindIndex(p => p.Id == CurrentPlayerId);
        var next = (index + 1) % players.Count;
        CurrentPlayerId = players[next].Id;
    }

    private void ResetCore()
    {
        Array.Clear(board);
        WinningScore = 2048;
        HasWon = false;
        IsGameOver = false;
        Started = false;
        CurrentPlayerId = Guid.Empty;
    }

    private bool ApplyMove(MoveDirection direction)
    {
        var moved = false;
        var totalGained = 0;

        for (var index = 0; index < BoardSize; index++)
        {
            var originalLine = GetLine(index, direction);
            var mergedLine = MergeLine(originalLine, out var gainedScore);

            if (originalLine.SequenceEqual(mergedLine))
            {
                continue;
            }

            SetLine(index, direction, mergedLine);
            totalGained += gainedScore;
            moved = true;
        }

        if (moved)
        {
            // assign gained points to the current player
            var player = players.FirstOrDefault(p => p.Id == CurrentPlayerId);
            if (player is not null)
            {
                var idx = players.FindIndex(p => p.Id == player.Id);
                var newScore = player.Score + totalGained;
                // update player's highest tile based on the current board state
                var boardMax = board.Cast<int>().Max();
                var newHighest = Math.Max(player.HighestTile, boardMax);
                players[idx] = player with { Score = newScore, HighestTile = newHighest };
            }
        }

        return moved;
    }

    private int[] GetLine(int index, MoveDirection direction)
    {
        var line = new int[BoardSize];

        for (var offset = 0; offset < BoardSize; offset++)
        {
            line[offset] = direction switch
            {
                MoveDirection.Left => board[index, offset],
                MoveDirection.Right => board[index, BoardSize - 1 - offset],
                MoveDirection.Up => board[offset, index],
                MoveDirection.Down => board[BoardSize - 1 - offset, index],
                _ => 0
            };
        }

        return line;
    }

    private void SetLine(int index, MoveDirection direction, int[] line)
    {
        for (var offset = 0; offset < BoardSize; offset++)
        {
            switch (direction)
            {
                case MoveDirection.Left:
                    board[index, offset] = line[offset];
                    break;
                case MoveDirection.Right:
                    board[index, BoardSize - 1 - offset] = line[offset];
                    break;
                case MoveDirection.Up:
                    board[offset, index] = line[offset];
                    break;
                case MoveDirection.Down:
                    board[BoardSize - 1 - offset, index] = line[offset];
                    break;
            }
        }
    }

    private static int[] MergeLine(int[] line, out int gainedScore)
    {
        var compacted = line.Where(value => value != 0).ToList();
        var merged = new List<int>(BoardSize);
        gainedScore = 0;

        for (var index = 0; index < compacted.Count; index++)
        {
            if (index + 1 < compacted.Count && compacted[index] == compacted[index + 1])
            {
                var value = compacted[index] * 2;
                merged.Add(value);
                gainedScore += value;
                index++;
            }
            else
            {
                merged.Add(compacted[index]);
            }
        }

        while (merged.Count < BoardSize)
        {
            merged.Add(0);
        }

        return merged.ToArray();
    }

    private void AddRandomTile()
    {
        var emptyCells = new List<(int Row, int Column)>();

        for (var row = 0; row < BoardSize; row++)
        {
            for (var column = 0; column < BoardSize; column++)
            {
                if (board[row, column] == 0)
                {
                    emptyCells.Add((row, column));
                }
            }
        }

        if (emptyCells.Count == 0)
        {
            return;
        }

        var cell = emptyCells[random.Next(emptyCells.Count)];
        board[cell.Row, cell.Column] = random.NextDouble() < 0.9 ? 2 : 4;
    }

    private void UpdateGameState()
    {
        // A player wins when any player's score reaches or exceeds the winning score
        if (!HasWon && players.Any(p => p.Score >= WinningScore))
        {
            HasWon = true;
        }

        IsGameOver = !CanMakeAnyMove();
    }

    private bool CanMakeAnyMove()
    {
        for (var row = 0; row < BoardSize; row++)
        {
            for (var column = 0; column < BoardSize; column++)
            {
                var current = board[row, column];

                if (current == 0)
                {
                    return true;
                }

                if (row + 1 < BoardSize && board[row + 1, column] == current)
                {
                    return true;
                }

                if (column + 1 < BoardSize && board[row, column + 1] == current)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void NotifyChanged() => Changed?.Invoke();
}

public sealed record Player(Guid Id, string Name, int Score = 0, int HighestTile = 0);

public enum MoveDirection
{
    Up,
    Down,
    Left,
    Right
}
