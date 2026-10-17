using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using TMPro;
using UnityEditor.SceneManagement;

/// <summary>
/// Builds the chapter checklist canvas beside the instruction canvases in the open scene,
/// replacing any existing one. Item titles and order follow the process chapters.
/// </summary>
public static class ChapterChecklistBuilder
{
    private const float Width = 1700f, Height = 2000f, Scale = 0.001f;
    private static readonly string[] Titles =
    {
        "Loosen the lug nuts", "Lift the car", "Remove the wheel",
        "Install the replacement wheel", "Lower the car and finish tightening",
    };

    [MenuItem("Tire Training/Create Chapter Checklist")]
    public static void Build()
    {
        GameObject instructionsObject = GameObject.Find("Instructions");
        Transform reference = instructionsObject != null ? instructionsObject.transform.Find("Canvas_Ch1_Loosen") : null;
        if (reference == null)
        {
            Debug.LogError("Chapter checklist needs Instructions/Canvas_Ch1_Loosen in the open scene.");
            return;
        }

        Transform instructions = instructionsObject.transform;
        Transform existing = instructions.Find("Canvas_Checklist");
        if (existing != null)
        {
            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        Sprite rounded = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Checklist/ChecklistRounded.png");
        Sprite outline = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Checklist/ChecklistBoxOutline.png");
        Sprite check = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/UI/Checklist/ChecklistCheckmark.png");
        int uiLayer = LayerMask.NameToLayer("UI");

        GameObject root = Make("Canvas_Checklist", instructions, uiLayer, typeof(Canvas), typeof(CanvasScaler));
        var rootRt = (RectTransform)root.transform;
        root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        root.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
        rootRt.sizeDelta = new Vector2(Width, Height);
        rootRt.localScale = Vector3.one * Scale;
        rootRt.rotation = reference.rotation;
        // On the learner's left of the instruction canvases, with a 15 cm gap.
        float referenceHalfWidth = ((RectTransform)reference).sizeDelta.x * reference.lossyScale.x / 2f;
        rootRt.position = reference.position - reference.right * (referenceHalfWidth + 0.15f + Width * Scale / 2f);
        SetImage(root, rounded, new Color(0f, 0f, 0f, 0.75f), 0.3f);

        var title = Make("Title", root.transform, uiLayer, typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        TopBand(title.rectTransform, 100f, 290f);
        title.text = "Checklist";
        title.fontSize = 140;
        title.fontStyle = FontStyles.Bold;
        title.color = Color.white;
        title.alignment = TextAlignmentOptions.MidlineLeft;
        title.raycastTarget = false;

        GameObject divider = Make("Divider", root.transform, uiLayer);
        SetImage(divider, rounded, new Color(1f, 1f, 1f, 0.3f), 8f);
        TopBand((RectTransform)divider.transform, 320f, 326f);

        GameObject list = Make("Items", root.transform, uiLayer, typeof(VerticalLayoutGroup));
        var listRt = (RectTransform)list.transform;
        listRt.anchorMin = Vector2.zero;
        listRt.anchorMax = Vector2.one;
        listRt.offsetMin = new Vector2(110f, 110f);
        listRt.offsetMax = new Vector2(-110f, -410f);
        var vertical = list.GetComponent<VerticalLayoutGroup>();
        vertical.spacing = 80f;
        vertical.childAlignment = TextAnchor.UpperLeft;
        vertical.childControlWidth = vertical.childControlHeight = true;
        vertical.childForceExpandWidth = true;
        vertical.childForceExpandHeight = false;

        var green = new Color(0.20f, 0.74f, 0.36f, 1f);
        var checklist = root.AddComponent<TireTraining.ChapterChecklist>();
        var serialized = new SerializedObject(checklist);
        SerializedProperty items = serialized.FindProperty("items");
        items.arraySize = Titles.Length;

        for (int i = 0; i < Titles.Length; i++)
        {
            GameObject row = Make("Item" + (i + 1), list.transform, uiLayer, typeof(HorizontalLayoutGroup));
            var horizontal = row.GetComponent<HorizontalLayoutGroup>();
            horizontal.spacing = 60f;
            horizontal.childAlignment = TextAnchor.MiddleLeft;
            horizontal.childControlWidth = horizontal.childControlHeight = true;
            horizontal.childForceExpandWidth = horizontal.childForceExpandHeight = false;

            GameObject box = Make("Box", row.transform, uiLayer, typeof(LayoutElement));
            var boxLayout = box.GetComponent<LayoutElement>();
            boxLayout.minWidth = boxLayout.preferredWidth = 130f;
            boxLayout.minHeight = boxLayout.preferredHeight = 130f;
            Image boxImage = SetImage(box, outline, Color.white, 0.8f);

            GameObject fill = Make("Fill", box.transform, uiLayer);
            Stretch((RectTransform)fill.transform, 0f);
            Image fillImage = SetImage(fill, rounded, green, 0.8f);
            fillImage.enabled = false;

            GameObject mark = Make("Checkmark", box.transform, uiLayer);
            Stretch((RectTransform)mark.transform, 10f);
            Image markImage = SetImage(mark, check, Color.white, 1f);
            markImage.enabled = false;

            var label = Make("Label", row.transform, uiLayer, typeof(TextMeshProUGUI), typeof(LayoutElement)).GetComponent<TextMeshProUGUI>();
            label.GetComponent<LayoutElement>().flexibleWidth = 1f;
            // The indent keeps wrapped lines aligned with the title, not the number.
            label.text = (i + 1) + ".<indent=1.3em>" + Titles[i] + "</indent>";
            label.fontSize = 100;
            label.color = Color.white;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;

            SerializedProperty item = items.GetArrayElementAtIndex(i);
            item.FindPropertyRelative("box").objectReferenceValue = boxImage;
            item.FindPropertyRelative("fill").objectReferenceValue = fillImage;
            item.FindPropertyRelative("checkmark").objectReferenceValue = markImage;
            item.FindPropertyRelative("label").objectReferenceValue = label;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        Undo.RegisterCreatedObjectUndo(root, "Create Chapter Checklist");
        EditorSceneManager.MarkSceneDirty(root.scene);
        Selection.activeGameObject = root;
    }

    private static GameObject Make(string name, Transform parent, int layer, params System.Type[] components)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = layer;
        go.transform.SetParent(parent, false);
        foreach (System.Type component in components)
        {
            go.AddComponent(component);
        }

        return go;
    }

    private static Image SetImage(GameObject go, Sprite sprite, Color color, float pixelsPerUnitMultiplier)
    {
        Image image = go.GetComponent<Image>();
        if (image == null)
        {
            image = go.AddComponent<Image>();
        }

        bool sliced = sprite.border != Vector4.zero;
        image.sprite = sprite;
        image.color = color;
        image.type = sliced ? Image.Type.Sliced : Image.Type.Simple;
        image.preserveAspect = !sliced;
        image.pixelsPerUnitMultiplier = pixelsPerUnitMultiplier;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>Full-width band between the given distances from the top edge, inset 110 on each side.</summary>
    private static void TopBand(RectTransform rect, float top, float bottom)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(110f, -bottom);
        rect.offsetMax = new Vector2(-110f, -top);
    }
}
