using System;
using System.IO;
using UnityEngine;

public sealed class SaveManager : MonoBehaviour
{
    public static SaveManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public static SaveManager GetOrCreate()
    {
        if (Instance != null) return Instance;
        var go = new GameObject(nameof(SaveManager));
        return go.AddComponent<SaveManager>();
    }

    public bool HasSave(string slotId) => File.Exists(GetGeometryPath(slotId));

    public GeometryData LoadGeometry(string slotId)
    {
        string path = GetGeometryPath(slotId);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Save geometry not found: {path}", path);
        return HexGeometryLoader.LoadFromFile(path);
    }

    public void SaveGeometry(string slotId, GeometryData data)
    {
        if (data == null) throw new ArgumentNullException(nameof(data));

        string path = GetGeometryPath(slotId);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(data, true));
        SaveMetadata(slotId, CreateMetadata(slotId, data));
        Debug.Log($"맵 저장 완료: {path}");
    }

    public string GetGeometryPath(string slotId) => SavePaths.GeometryPath(slotId);
    public string GetProvincePngPath(string slotId)
    {
        string path = SavePaths.ProvincePngPath(slotId);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        return path;
    }
    public string GetProvincePalettePath(string slotId)
    {
        string path = SavePaths.ProvincePalettePath(slotId);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        return path;
    }

    public SaveSlotMetadata LoadMetadata(string slotId)
    {
        string path = SavePaths.MetadataPath(slotId);
        if (!File.Exists(path)) return null;
        return JsonUtility.FromJson<SaveSlotMetadata>(File.ReadAllText(path));
    }

    public void SaveMetadata(string slotId, SaveSlotMetadata metadata)
    {
        if (metadata == null) throw new ArgumentNullException(nameof(metadata));
        string path = SavePaths.MetadataPath(slotId);
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(metadata, true));
    }

    static SaveSlotMetadata CreateMetadata(string slotId, GeometryData data) => new SaveSlotMetadata
    {
        slotId = SavePaths.SanitizeSlotId(slotId),
        displayName = SavePaths.SanitizeSlotId(slotId),
        lastSavedUtc = DateTime.UtcNow.ToString("O"),
        mapWidth = data.grid != null ? data.grid.width : 0,
        mapHeight = data.grid != null ? data.grid.height : 0,
        provinceCount = data.provinces != null ? data.provinces.Length : 0
    };
}
