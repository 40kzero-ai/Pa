using System;

[Serializable]
public class SaveSlotMetadata
{
    public string slotId;
    public string displayName;
    public string lastSavedUtc;
    public int mapWidth;
    public int mapHeight;
    public int provinceCount;
}
