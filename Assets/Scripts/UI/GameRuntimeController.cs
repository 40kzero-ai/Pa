using System.Collections;
using UnityEngine;

/// <summary>
/// Minimal runtime UI for the current single-scene project stage.
/// It provides a visible main menu, loading overlay, and in-game HUD without requiring
/// prebuilt Canvas assets. Later PRs can replace this IMGUI bridge with Canvas scenes.
/// </summary>
public sealed class GameRuntimeController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] HexGrid grid;
    [SerializeField] HexMapEditor editor;
    [SerializeField] HexCameraController cameraController;
    [SerializeField] TextAsset defaultGeometryJson;

    [Header("Save")]
    [SerializeField] string slotId = SavePaths.DefaultSlotId;

    [Header("Startup")]
    [SerializeField] bool showMainMenuOnStart = true;
    [SerializeField] bool enableEditorAfterLoad = true;

    bool showMainMenu;
    bool loading;
    bool playing;
    string status = "";
    GUIStyle titleStyle;
    GUIStyle subtitleStyle;

    void Awake()
    {
        ResolveReferences();
        showMainMenu = showMainMenuOnStart;
        playing = !showMainMenuOnStart;
        if (editor != null && showMainMenuOnStart)
            editor.enabled = false;
    }

    void ResolveReferences()
    {
        if (grid == null) grid = FindFirstObjectByType<HexGrid>();
        if (editor == null) editor = FindFirstObjectByType<HexMapEditor>();
        if (cameraController == null) cameraController = FindFirstObjectByType<HexCameraController>();
        if (grid != null && cameraController != null) grid.CameraController = cameraController;
        if (defaultGeometryJson == null && grid != null) defaultGeometryJson = grid.GeometryJson;
    }

    void OnGUI()
    {
        EnsureStyles();
        if (showMainMenu) DrawMainMenu();
        if (playing && !showMainMenu) DrawHud();
        if (loading) DrawLoadingOverlay();
    }

    void EnsureStyles()
    {
        titleStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 34,
            fontStyle = FontStyle.Bold
        };
        subtitleStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 14
        };
    }

    void DrawMainMenu()
    {
        var full = new Rect(0, 0, Screen.width, Screen.height);
        GUI.Box(full, GUIContent.none);

        float panelWidth = 360f;
        float panelHeight = 320f;
        var panel = new Rect((Screen.width - panelWidth) * 0.5f, (Screen.height - panelHeight) * 0.5f, panelWidth, panelHeight);
        GUILayout.BeginArea(panel, GUI.skin.box);
        GUILayout.Space(18);
        GUILayout.Label("Province Architect", titleStyle, GUILayout.Height(48));
        GUILayout.Label("Map editor prototype", subtitleStyle, GUILayout.Height(24));
        GUILayout.Space(24);

        GUI.enabled = !loading;
        if (GUILayout.Button("새 게임", GUILayout.Height(42))) StartNewGame();

        SaveManager saveManager = SaveManager.GetOrCreate();
        GUI.enabled = !loading && saveManager.HasSave(slotId);
        if (GUILayout.Button("이어하기", GUILayout.Height(42))) ContinueGame();

        GUI.enabled = !loading;
        if (GUILayout.Button("종료", GUILayout.Height(34))) Application.Quit();
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(status))
        {
            GUILayout.Space(12);
            GUILayout.Label(status, subtitleStyle);
        }
        GUILayout.EndArea();
    }

    void DrawHud()
    {
        const float width = 220f;
        var rect = new Rect(Screen.width - width - 12f, 12f, width, 230f);
        GUILayout.BeginArea(rect, GUI.skin.box);
        GUILayout.Label("게임 HUD", subtitleStyle);

        GUI.enabled = !loading;
        if (GUILayout.Button("저장", GUILayout.Height(28))) SaveGame();
        if (GUILayout.Button("불러오기", GUILayout.Height(28))) ContinueGame();
        if (GUILayout.Button("프로빈스 PNG 저장", GUILayout.Height(28))) ExportProvincePng();
        if (GUILayout.Button("프로빈스 PNG 불러오기", GUILayout.Height(28))) ImportProvincePng();
        if (GUILayout.Button(editor != null && editor.enabled ? "에디터 숨기기" : "에디터 보이기", GUILayout.Height(28))) ToggleEditor();
        if (GUILayout.Button("메인 메뉴", GUILayout.Height(28))) ReturnToMenu();
        GUI.enabled = true;

        if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);
        GUILayout.EndArea();
    }

    void DrawLoadingOverlay()
    {
        var full = new Rect(0, 0, Screen.width, Screen.height);
        Color previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.Box(full, GUIContent.none);
        GUI.color = previous;

        var label = new Rect(0, Screen.height * 0.5f - 24f, Screen.width, 48f);
        GUI.Label(label, "로딩 중...", titleStyle);
    }

    void StartNewGame()
    {
        ResolveReferences();
        if (defaultGeometryJson == null)
        {
            status = "기본 geometry.json이 연결되지 않았습니다.";
            return;
        }

        StartCoroutine(LoadMap(HexGeometryLoader.Load(defaultGeometryJson.text), "새 게임 시작"));
    }

    void ContinueGame()
    {
        ResolveReferences();
        SaveManager saveManager = SaveManager.GetOrCreate();
        if (!saveManager.HasSave(slotId))
        {
            status = "저장 파일이 없습니다.";
            return;
        }

        StartCoroutine(LoadMap(saveManager.LoadGeometry(slotId), "저장 파일 불러옴"));
    }

    IEnumerator LoadMap(GeometryData data, string doneStatus)
    {
        if (grid == null || data == null) yield break;

        loading = true;
        showMainMenu = false;
        playing = false;
        if (editor != null) editor.enabled = false;

        bool completed = false;
        void OnBuildCompleted() => completed = true;
        grid.BuildCompleted += OnBuildCompleted;
        grid.Build(data);
        while (grid.IsBuilding && !completed) yield return null;
        grid.BuildCompleted -= OnBuildCompleted;

        loading = false;
        playing = true;
        status = doneStatus;
        if (editor != null) editor.enabled = enableEditorAfterLoad;
    }

    void SaveGame()
    {
        if (grid == null) return;
        SaveManager.GetOrCreate().SaveGeometry(slotId, grid.ExportData());
        status = "저장 완료";
    }

    void ExportProvincePng()
    {
        if (grid == null) return;
        grid.SaveProvincePNG(SaveManager.GetOrCreate().GetProvincePngPath(slotId));
        status = "프로빈스 PNG 저장 완료";
    }

    void ImportProvincePng()
    {
        if (grid == null) return;
        status = grid.LoadProvincePNG(SaveManager.GetOrCreate().GetProvincePngPath(slotId))
            ? "프로빈스 PNG 불러옴"
            : "프로빈스 PNG 없음/실패";
    }

    void ToggleEditor()
    {
        if (editor == null) return;
        editor.enabled = !editor.enabled;
    }

    void ReturnToMenu()
    {
        showMainMenu = true;
        playing = false;
        loading = false;
        if (editor != null) editor.enabled = false;
    }
}
