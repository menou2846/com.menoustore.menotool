using UnityEngine;
using UnityEditor;
using System.Linq;
using MenouStore.License;

public class SelectionArranger : EditorWindow
{
    private const string ProductId = "default";
    private enum ArrangeMode
    {
        X,
        Y,
        Z,
        GridXY,
        GridXZ,
        GridYZ
    }

    private ArrangeMode arrangeMode = ArrangeMode.X;
    private Vector3 startPosition = Vector3.zero;
    private float spacing = 0.5f;
    private int gridColumns = 5;
    private bool sortByName = true;
    private bool keepWorldRotation = true;
    private bool registerUndo = true;

 [MenuItem("Meno Tools/Prefab Grid Placer")]
    public static void Open()
    {
        if (!LicenseAuth.IsAuthenticated(ProductId))
        {
            LicenseAuth.OpenAuthWindow(ProductId);
            return;
        }

        GetWindow<SelectionArranger>("Prefab Grid Placer");
    }

    private void OnGUI()
    {
        if (!LicenseAuth.IsAuthenticated(ProductId))
        {
            EditorGUILayout.HelpBox("認証が必要です。一度ウィンドウを閉じてメニューから開き直してください。", MessageType.Warning);
            if (GUILayout.Button("認証する"))
            {
                LicenseAuth.OpenAuthWindow(ProductId);
                Close();
            }
            return;
        }

        EditorGUILayout.LabelField("Prefab Grid Placer", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        arrangeMode = (ArrangeMode)EditorGUILayout.EnumPopup("配置モード", arrangeMode);
        startPosition = EditorGUILayout.Vector3Field("開始位置", startPosition);
        spacing = EditorGUILayout.FloatField("間隔", spacing);

        if (arrangeMode == ArrangeMode.GridXY ||
            arrangeMode == ArrangeMode.GridXZ ||
            arrangeMode == ArrangeMode.GridYZ)
        {
            gridColumns = Mathf.Max(1, EditorGUILayout.IntField("列数", gridColumns));
        }

        sortByName = EditorGUILayout.Toggle("名前順で並べる", sortByName);
        keepWorldRotation = EditorGUILayout.Toggle("回転はそのまま", keepWorldRotation);
        registerUndo = EditorGUILayout.Toggle("Undo対応", registerUndo);

        EditorGUILayout.Space();

        EditorGUILayout.HelpBox(
            "Hierarchy で複数オブジェクトを選択して実行します。\n" +
            "Prefabインスタンスでも通常オブジェクトでも使えます。",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(Selection.transforms.Length == 0))
        {
            if (GUILayout.Button($"選択中オブジェクトを並べる ({Selection.transforms.Length}個)"))
            {
                ArrangeSelectedObjects();
            }
        }
    }

    private void ArrangeSelectedObjects()
    {
        Transform[] selected = Selection.transforms;

        if (selected == null || selected.Length == 0)
        {
            EditorUtility.DisplayDialog("Selection Arranger", "オブジェクトを選択してください。", "OK");
            return;
        }

        // 親子同時選択で二重に見づらくなるのを少し避けるため、
        // 選択内に親がいる場合はその子を除外
        selected = selected
            .Where(t => !selected.Any(other => other != t && t.IsChildOf(other)))
            .ToArray();

        if (sortByName)
        {
            selected = selected.OrderBy(t => t.name).ToArray();
        }

        for (int i = 0; i < selected.Length; i++)
        {
            Transform tr = selected[i];

            if (registerUndo)
            {
                Undo.RecordObject(tr, "Arrange Selected Objects");
            }

            Vector3 targetPos = GetPosition(i);

            tr.position = targetPos;

            if (!keepWorldRotation)
            {
                tr.rotation = Quaternion.identity;
            }

            EditorUtility.SetDirty(tr);
        }

        EditorUtility.DisplayDialog(
            "Selection Arranger",
            $"{selected.Length}個のオブジェクトを並べました。",
            "OK");
    }

    private Vector3 GetPosition(int index)
    {
        switch (arrangeMode)
        {
            case ArrangeMode.X:
                return startPosition + new Vector3(index * spacing, 0f, 0f);

            case ArrangeMode.Y:
                return startPosition + new Vector3(0f, index * spacing, 0f);

            case ArrangeMode.Z:
                return startPosition + new Vector3(0f, 0f, index * spacing);

            case ArrangeMode.GridXY:
            {
                int col = index % gridColumns;
                int row = index / gridColumns;
                return startPosition + new Vector3(col * spacing, row * spacing, 0f);
            }

            case ArrangeMode.GridXZ:
            {
                int col = index % gridColumns;
                int row = index / gridColumns;
                return startPosition + new Vector3(col * spacing, 0f, row * spacing);
            }

            case ArrangeMode.GridYZ:
            {
                int col = index % gridColumns;
                int row = index / gridColumns;
                return startPosition + new Vector3(0f, col * spacing, row * spacing);
            }

            default:
                return startPosition;
        }
    }
}
