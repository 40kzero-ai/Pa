public enum GameStartMode
{
    None,
    NewGame,
    Continue
}

public sealed class GameStartRequest
{
    public GameStartMode Mode;
    public string SlotId;
    public string MapId;
}
