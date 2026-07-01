using UnityEngine;

public sealed class InGameHudController : MonoBehaviour
{
    [SerializeField] string slotId = SavePaths.DefaultSlotId;
    HexGrid grid;
    HexMapEditor editor;

    public void Initialize(HexGrid grid, HexMapEditor editor)
    {
        this.grid = grid;
        this.editor = editor;
    }

    public void OnSave()
    {
        if (grid == null) return;
        SaveManager.GetOrCreate().SaveGeometry(slotId, grid.ExportData());
    }

    public void OnLoad()
    {
        SaveManager saveManager = SaveManager.GetOrCreate();
        if (grid == null || !saveManager.HasSave(slotId)) return;
        grid.Build(saveManager.LoadGeometry(slotId));
    }

    public void OnExportProvincePng()
    {
        if (grid == null) return;
        grid.SaveProvincePNG(SaveManager.GetOrCreate().GetProvincePngPath(slotId));
    }

    public void OnImportProvincePng()
    {
        if (grid == null) return;
        grid.LoadProvincePNG(SaveManager.GetOrCreate().GetProvincePngPath(slotId));
    }

    public void OnToggleEditor()
    {
        if (editor != null) editor.enabled = !editor.enabled;
    }

    public void OnReturnToMenu()
    {
        GameFlowController.GetOrCreate().ReturnToMainMenu();
    }
}
