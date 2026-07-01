using System.IO;
using UnityEngine;

public static class SavePaths
{
    public const string DefaultSlotId = "slot_0";
    public const string GeometryFileName = "geometry.json";
    public const string ProvincePngFileName = "provinces.png";
    public const string ProvincePaletteFileName = "provinces.palette.json";
    public const string MetadataFileName = "metadata.json";

    public static string SavesRoot => Path.Combine(Application.persistentDataPath, "saves");

    public static string SlotDirectory(string slotId) => Path.Combine(SavesRoot, SanitizeSlotId(slotId));
    public static string GeometryPath(string slotId) => Path.Combine(SlotDirectory(slotId), GeometryFileName);
    public static string ProvincePngPath(string slotId) => Path.Combine(SlotDirectory(slotId), ProvincePngFileName);
    public static string ProvincePalettePath(string slotId) => Path.Combine(SlotDirectory(slotId), ProvincePaletteFileName);
    public static string MetadataPath(string slotId) => Path.Combine(SlotDirectory(slotId), MetadataFileName);

    public static string SanitizeSlotId(string slotId)
    {
        if (string.IsNullOrWhiteSpace(slotId)) return DefaultSlotId;
        foreach (char invalid in Path.GetInvalidFileNameChars())
            slotId = slotId.Replace(invalid, '_');
        return slotId;
    }
}
