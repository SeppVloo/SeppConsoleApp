namespace Sepp2048Web.Services;

public sealed record GameRoom(Guid Id, string Name, GameLobbyService Lobby);

public sealed class RoomManager
{
    private readonly object sync = new();
    private readonly List<GameRoom> rooms = new();

    public event Action? Changed;

    public IReadOnlyList<GameRoom> Rooms
    {
        get
        {
            lock (sync)
            {
                return rooms.ToArray();
            }
        }
    }

    public GameRoom CreateRoom(string name)
    {
        lock (sync)
        {
            var displayName = string.IsNullOrWhiteSpace(name) ? $"Room {rooms.Count + 1}" : name.Trim();
            var room = new GameRoom(Guid.NewGuid(), displayName, new GameLobbyService());
            rooms.Add(room);
            NotifyChanged();
            return room;
        }
    }

    public GameRoom? GetRoom(Guid id)
    {
        lock (sync)
        {
            return rooms.FirstOrDefault(r => r.Id == id);
        }
    }

    private void NotifyChanged() => Changed?.Invoke();
}
