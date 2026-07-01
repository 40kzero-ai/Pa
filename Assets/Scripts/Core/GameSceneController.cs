using System.Collections;
using UnityEngine;

public sealed class GameSceneController : MonoBehaviour
{
    [SerializeField] HexGrid grid;
    [SerializeField] HexMapEditor editor;
    [SerializeField] HexCameraController cameraController;
    [SerializeField] TextAsset defaultGeometryJson;
    [SerializeField] InGameHudController hud;

    IEnumerator Start()
    {
        if (grid == null) grid = FindFirstObjectByType<HexGrid>();
        if (editor == null) editor = FindFirstObjectByType<HexMapEditor>();
        if (cameraController == null) cameraController = FindFirstObjectByType<HexCameraController>();
        if (hud == null) hud = FindFirstObjectByType<InGameHudController>();

        if (grid == null)
        {
            Debug.LogError("GameSceneController: HexGrid가 없습니다.");
            yield break;
        }

        if (cameraController != null) grid.CameraController = cameraController;
        if (editor != null) editor.enabled = false;

        GeometryData data = ResolveStartGeometry();
        if (data == null)
        {
            Debug.LogError("GameSceneController: 시작 GeometryData를 로드하지 못했습니다.");
            yield break;
        }

        bool completed = false;
        void OnBuildCompleted() => completed = true;
        grid.BuildCompleted += OnBuildCompleted;
        grid.Build(data);
        while (grid.IsBuilding && !completed) yield return null;
        grid.BuildCompleted -= OnBuildCompleted;

        if (hud != null) hud.Initialize(grid, editor);
        if (editor != null) editor.enabled = true;
    }

    GeometryData ResolveStartGeometry()
    {
        GameStartRequest request = GameFlowController.Instance != null
            ? GameFlowController.Instance.ConsumeStartRequest()
            : null;

        if (request != null && request.Mode == GameStartMode.Continue && SaveManager.GetOrCreate().HasSave(request.SlotId))
            return SaveManager.Instance.LoadGeometry(request.SlotId);

        TextAsset source = defaultGeometryJson != null ? defaultGeometryJson : grid.GeometryJson;
        return source != null ? HexGeometryLoader.Load(source.text) : null;
    }
}
