using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// 게임다운 진입 흐름을 담당하는 간단한 런처 UI.
/// 로그인/접속 화면 → 로딩 화면 → 메인 메뉴 → 실제 맵 편집/플레이 화면으로 전환한다.
/// 기존 HexGrid/HexMapEditor는 "게임 시작" 이후에만 켜서, 처음 실행 시 바로 에디터가 노출되지 않게 한다.
/// </summary>
public class GameShell : MonoBehaviour
{
    enum ShellState { Connect, Loading, MainMenu, Settings, InGame, ExitConfirm }

    [Header("연결 대상")]
    public HexGrid Grid;
    public HexMapEditor Editor;
    public HexCameraController CameraController;

    [Header("게임 표시")]
    public string GameTitle = "Province Architect";
    public string VersionText = "Prototype Build";
    public bool HideMapUntilGameStarts = true;

    [Header("로딩 연출")]
    [Min(0.1f)] public float MinimumLoadingSeconds = 1.2f;
    public string[] LoadingMessages =
    {
        "지형 팔레트 초기화 중...",
        "프로빈스 경계 계산 중...",
        "카메라 위치 보정 중...",
        "게임 세계 접속 완료"
    };

    ShellState state = ShellState.Connect;
    ShellState loadingTarget = ShellState.MainMenu;
    float loadingProgress;
    string loadingMessage;
    string profileName = "Player";
    string status = "서버 선택: 로컬 샌드박스";
    bool hasSave;
    bool fullscreen = true;
    bool showEditorOnStart = true;
    float uiScale = 1f;
    Vector2 menuScroll;

    string SavePath => Path.Combine(Application.persistentDataPath, "edited_geometry.json");

    void Awake()
    {
        if (Grid == null) Grid = FindFirstObjectByType<HexGrid>();
        if (Editor == null) Editor = FindFirstObjectByType<HexMapEditor>();
        if (CameraController == null) CameraController = FindFirstObjectByType<HexCameraController>();

        SetGameplayActive(!HideMapUntilGameStarts);
        RefreshSaveState();
        loadingMessage = LoadingMessages != null && LoadingMessages.Length > 0 ? LoadingMessages[0] : "로딩 중...";
    }

    void SetGameplayActive(bool active)
    {
        if (Grid != null) Grid.enabled = active;
        if (Editor != null) Editor.enabled = active && showEditorOnStart;
        if (CameraController != null) CameraController.enabled = active;
    }

    void RefreshSaveState()
    {
        hasSave = File.Exists(SavePath);
    }

    void OnGUI()
    {
        Matrix4x4 prevMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * uiScale);

        switch (state)
        {
            case ShellState.Connect: DrawConnect(); break;
            case ShellState.Loading: DrawLoading(); break;
            case ShellState.MainMenu: DrawMainMenu(); break;
            case ShellState.Settings: DrawSettings(); break;
            case ShellState.InGame: DrawInGameHud(); break;
            case ShellState.ExitConfirm: DrawExitConfirm(); break;
        }

        GUI.matrix = prevMatrix;
    }

    void DrawBackdrop()
    {
        GUI.Box(new Rect(0, 0, Screen.width / uiScale, Screen.height / uiScale), "");
        var title = new GUIStyle(GUI.skin.label)
        {
            fontSize = 42,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.9f, 0.95f, 1f) }
        };
        GUI.Label(new Rect(0, 54, Screen.width / uiScale, 60), GameTitle, title);
        GUI.Label(new Rect(0, 110, Screen.width / uiScale, 24), VersionText, CenterStyle(13, Color.gray));
    }

    void DrawConnect()
    {
        DrawBackdrop();
        Rect panel = CenterRect(430, 255);
        GUILayout.BeginArea(panel, GUI.skin.window);
        GUILayout.Label("접속 화면", HeaderStyle());
        GUILayout.Space(10);
        GUILayout.Label("프로필 이름");
        profileName = GUILayout.TextField(profileName, GUILayout.Height(28));
        GUILayout.Label(status, CenterStyle(12, new Color(0.75f, 0.85f, 1f)));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("접속", GUILayout.Height(42))) BeginLoading(ShellState.MainMenu, "계정 인증 및 세션 생성 중...");
        if (GUILayout.Button("종료", GUILayout.Height(32))) state = ShellState.ExitConfirm;
        GUILayout.EndArea();
    }

    void DrawLoading()
    {
        DrawBackdrop();
        Rect panel = CenterRect(520, 170);
        GUILayout.BeginArea(panel, GUI.skin.window);
        GUILayout.Label("로딩", HeaderStyle());
        GUILayout.Space(20);
        GUILayout.Label(loadingMessage);
        Rect bar = GUILayoutUtility.GetRect(460, 28);
        GUI.Box(bar, "");
        GUI.Box(new Rect(bar.x + 3, bar.y + 3, (bar.width - 6) * loadingProgress, bar.height - 6), "");
        GUILayout.Label(Mathf.RoundToInt(loadingProgress * 100f) + "%", CenterStyle(12, Color.white));
        GUILayout.EndArea();
    }

    void DrawMainMenu()
    {
        DrawBackdrop();
        Rect panel = CenterRect(420, 360);
        GUILayout.BeginArea(panel, GUI.skin.window);
        GUILayout.Label($"환영합니다, {profileName}", HeaderStyle());
        GUILayout.Space(14);
        if (GUILayout.Button("새로운 게임", GUILayout.Height(46))) BeginLoading(ShellState.InGame, "새 캠페인 맵 생성 중...");
        GUI.enabled = hasSave;
        if (GUILayout.Button(hasSave ? "이어하기" : "이어하기 (저장 없음)", GUILayout.Height(46))) ContinueGame();
        GUI.enabled = true;
        if (GUILayout.Button("설정", GUILayout.Height(40))) state = ShellState.Settings;
        if (GUILayout.Button("종료", GUILayout.Height(40))) state = ShellState.ExitConfirm;
        GUILayout.FlexibleSpace();
        GUILayout.Label("현재 기능: 헥스 맵 생성 + 인게임 에디터", CenterStyle(12, Color.gray));
        GUILayout.EndArea();
    }

    void DrawSettings()
    {
        DrawBackdrop();
        Rect panel = CenterRect(460, 330);
        GUILayout.BeginArea(panel, GUI.skin.window);
        GUILayout.Label("설정", HeaderStyle());
        menuScroll = GUILayout.BeginScrollView(menuScroll);
        fullscreen = GUILayout.Toggle(fullscreen, "전체 화면 모드");
        showEditorOnStart = GUILayout.Toggle(showEditorOnStart, "게임 시작 시 에디터 패널 표시");
        GUILayout.Label($"런처 UI 배율: {uiScale:0.0}");
        uiScale = GUILayout.HorizontalSlider(uiScale, 0.8f, 1.4f);
        GUILayout.Space(12);
        GUILayout.Label($"저장 경로: {SavePath}");
        GUILayout.EndScrollView();
        if (GUILayout.Button("적용", GUILayout.Height(34))) Screen.fullScreen = fullscreen;
        if (GUILayout.Button("메인 메뉴로", GUILayout.Height(34))) state = ShellState.MainMenu;
        GUILayout.EndArea();
    }

    void DrawInGameHud()
    {
        GUILayout.BeginArea(new Rect(Screen.width / uiScale - 220, 12, 205, 118), GUI.skin.window);
        GUILayout.Label("게임 메뉴", HeaderStyle(16));
        if (GUILayout.Button(Editor != null && Editor.enabled ? "에디터 숨기기" : "에디터 보이기"))
            if (Editor != null) Editor.enabled = !Editor.enabled;
        if (GUILayout.Button("메인 메뉴")) { SetGameplayActive(false); RefreshSaveState(); state = ShellState.MainMenu; }
        if (GUILayout.Button("종료")) state = ShellState.ExitConfirm;
        GUILayout.EndArea();
    }

    void DrawExitConfirm()
    {
        DrawBackdrop();
        Rect panel = CenterRect(390, 180);
        GUILayout.BeginArea(panel, GUI.skin.window);
        GUILayout.Label("게임을 종료할까요?", HeaderStyle());
        GUILayout.Space(16);
        if (GUILayout.Button("종료", GUILayout.Height(38))) Application.Quit();
        if (GUILayout.Button("취소", GUILayout.Height(34))) state = Grid != null && Grid.enabled ? ShellState.InGame : ShellState.MainMenu;
        GUILayout.EndArea();
    }

    void BeginLoading(ShellState target, string firstMessage)
    {
        loadingTarget = target;
        loadingProgress = 0f;
        loadingMessage = firstMessage;
        state = ShellState.Loading;
        StartCoroutine(LoadingRoutine());
    }

    IEnumerator LoadingRoutine()
    {
        float elapsed = 0f;
        while (elapsed < MinimumLoadingSeconds)
        {
            elapsed += Time.deltaTime;
            loadingProgress = Mathf.Clamp01(elapsed / MinimumLoadingSeconds);
            if (LoadingMessages != null && LoadingMessages.Length > 0)
                loadingMessage = LoadingMessages[Mathf.Min(LoadingMessages.Length - 1, Mathf.FloorToInt(loadingProgress * LoadingMessages.Length))];
            yield return null;
        }

        if (loadingTarget == ShellState.InGame) StartNewGame();
        state = loadingTarget;
    }

    void StartNewGame()
    {
        SetGameplayActive(true);
    }

    void ContinueGame()
    {
        SetGameplayActive(true);
        if (Grid != null && Grid.LoadFromFile(SavePath)) BeginLoading(ShellState.InGame, "저장된 캠페인 복원 중...");
        else state = ShellState.MainMenu;
    }

    static Rect CenterRect(float width, float height) => new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
    static GUIStyle HeaderStyle(int size = 20) => new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
    static GUIStyle CenterStyle(int size, Color color) => new GUIStyle(GUI.skin.label) { fontSize = size, alignment = TextAnchor.MiddleCenter, normal = { textColor = color } };
}
