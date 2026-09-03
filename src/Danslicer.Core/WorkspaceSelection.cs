namespace Danslicer.Core;

public enum WorkspaceMode { Layout, Support, Slicing }

/// <summary>Mode-dependent selection semantics shared by the UI and headless tests.</summary>
public static class WorkspaceSelection
{
    public static void SelectAll(Document document, WorkspaceMode mode)
    {
        switch (mode)
        {
            case WorkspaceMode.Layout:
                document.ClearSupportSelection();
                document.SelectAll();
                break;
            case WorkspaceMode.Support:
                document.ClearSelection();
                document.SelectAllSupportElements();
                break;
            case WorkspaceMode.Slicing:
                break;
        }
    }
}
