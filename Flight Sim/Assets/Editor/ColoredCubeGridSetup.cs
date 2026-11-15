using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates ordinary, persistent scene objects; no runtime component is required.</summary>
[InitializeOnLoad]
public static class ColoredCubeGridSetup
{
    const string GridName = "Colored Cube Grid";
    const string RequestPath = "Library/ColoredCubeGridSetup.request";
    const string ResultPath = "Library/ColoredCubeGridSetup.result.txt";

    static ColoredCubeGridSetup()
    {
        EditorApplication.delayCall += ProcessRequest;
    }

    static void ProcessRequest()
    {
        // A local one-shot request allows setup in an already running editor.
        if (!File.Exists(RequestPath))
            return;

        File.Delete(RequestPath);
        try
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/OpenXR Setup.unity")
                throw new InvalidOperationException("OpenXR Setup must be the active scene.");

            CreateGrid();
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Could not save the grid scene.");

            File.WriteAllText(ResultPath, "Saved 124 colored cubes, spacing 8, centered at origin in " + scene.path);
        }
        catch (Exception exception)
        {
            File.WriteAllText(ResultPath, exception.ToString());
            Debug.LogException(exception);
        }
    }

    [MenuItem("Tools/Flight Sim/Create Colored Cube Grid")]
    public static void CreateGrid()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Create the grid outside Play mode.");

        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded || PrefabStageUtility.GetCurrentPrefabStage() != null)
            throw new InvalidOperationException("Open a scene before creating the grid.");

        foreach (var existing in scene.GetRootGameObjects())
        {
            if (existing.name != GridName)
                continue;

            UpdateLayout(existing);
            Selection.activeGameObject = existing;
            return;
        }

        var colorNames = new[] { "Red", "Yellow", "Green", "Cyan", "Blue", "Magenta" };
        var materials = new Material[colorNames.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            var path = "Assets/Materials/" + colorNames[i] + ".mat";
            materials[i] = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (materials[i] == null)
                throw new InvalidOperationException("Missing grid material: " + path);
        }

        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Create Colored Cube Grid");

        var root = new GameObject(GridName);
        SceneManager.MoveGameObjectToScene(root, scene);
        root.transform.position = Vector3.zero;
        Undo.RegisterCreatedObjectUndo(root, "Create Colored Cube Grid");

        for (int x = 0; x < 5; x++)
        for (int y = 0; y < 5; y++)
        for (int z = 0; z < 5; z++)
        {
            if (x == 2 && y == 2 && z == 2)
                continue;

            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = $"Cube ({x}, {y}, {z})";
            cube.transform.SetParent(root.transform, false);
            cube.transform.localPosition = new Vector3(x - 2, y - 2, z - 2) * 8f;
            cube.GetComponent<MeshRenderer>().sharedMaterial = materials[(x + y + z) % materials.Length];
            UnityEngine.Object.DestroyImmediate(cube.GetComponent<BoxCollider>());
            Undo.RegisterCreatedObjectUndo(cube, "Create Grid Cube");
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root;
        Debug.Log("Created 124 colored cubes with 8-unit spacing, centered at the origin.", root);
    }

    static void UpdateLayout(GameObject root)
    {
        Undo.IncrementCurrentGroup();
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Update Colored Cube Grid");
        Undo.RecordObject(root.transform, "Center Grid");
        root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.transform.localScale = Vector3.one;

        for (int x = 0; x < 5; x++)
        for (int y = 0; y < 5; y++)
        for (int z = 0; z < 5; z++)
        {
            var cube = root.transform.Find($"Cube ({x}, {y}, {z})");
            if (cube == null)
                continue;

            if (x == 2 && y == 2 && z == 2)
            {
                Undo.DestroyObjectImmediate(cube.gameObject);
                continue;
            }

            Undo.RecordObject(cube, "Space Grid Cube");
            cube.localPosition = new Vector3(x - 2, y - 2, z - 2) * 8f;
        }

        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("Updated cube grid: 8-unit spacing, centered at origin, center cube removed.", root);
    }
}
