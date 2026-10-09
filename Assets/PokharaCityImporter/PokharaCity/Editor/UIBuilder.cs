// =============================================================================
//  UIBuilder.cs  —  Week 6: Tools > Pokhara City > Add Menus and Minimap
// =============================================================================
//  Adds one object, "Pokhara UI", with:
//    GameMenu  - title screen, pause menu (Esc / P), settings, credits
//    Minimap   - the round map in the bottom-right corner
//
//  Running it again replaces the old one (found by its script, so it still
//  works if you renamed the object).
// =============================================================================

using UnityEditor;
using UnityEngine;

public static class UIBuilder
{
    [MenuItem("Tools/Pokhara City/Add Menus and Minimap")]
    public static void Build()
    {
        if (Object.FindFirstObjectByType<PokharaCar>() == null)
        {
            EditorUtility.DisplayDialog("No car yet", "Create the player car first (Tools > Pokhara City > Create Player Car).", "OK");
            return;
        }

        // Remove an older copy, whatever its name is now.
        GameMenu oldMenu = Object.FindFirstObjectByType<GameMenu>();
        if (oldMenu != null) Undo.DestroyObjectImmediate(oldMenu.gameObject);
        Minimap oldMap = Object.FindFirstObjectByType<Minimap>();
        if (oldMap != null) Undo.DestroyObjectImmediate(oldMap.gameObject);

        var ui = new GameObject("Pokhara UI");
        Undo.RegisterCreatedObjectUndo(ui, "Add Menus and Minimap");
        ui.AddComponent<GameMenu>();
        ui.AddComponent<Minimap>();
        Selection.activeGameObject = ui;

        string note = Object.FindFirstObjectByType<MissionManager>() == null
            ? "\n\n(No missions found: the Missions button will do nothing until you run Add Missions.)" : "";
        EditorUtility.DisplayDialog("Menus and minimap added",
            "Press Play: you will see the title screen.\n\n" +
            "While driving: Esc or P = pause menu,  - and = = zoom the minimap.\n\n" +
            "Testing a lot? Select 'Pokhara UI' and untick 'Show Title On Start' to skip the title screen." + note, "Great!");
    }
}
