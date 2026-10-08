using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 간단한 uGUI 부품을 코드로 만드는 도우미.
/// 에디터 메뉴(COM → 5. UI 만들기)와 OptionPanelUI(옵션 목록 줄)에서 같이 씀.
/// 글자는 기본 Text + LegacyRuntime 폰트 → Windows에서 한글이 별도 폰트 없이 나옴.
/// </summary>
public static class UIFactory
{
    public static readonly Color PanelColor  = new Color(0.10f, 0.11f, 0.13f, 0.88f);
    public static readonly Color ButtonColor = new Color(0.22f, 0.24f, 0.28f, 1f);
    public static readonly Color SelectedColor = new Color(0.20f, 0.45f, 0.85f, 1f);
    public static readonly Color TextColor   = new Color(0.95f, 0.95f, 0.95f, 1f);

    private static Font font;
    public static Font DefaultFont
    {
        get
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return font;
        }
    }

    public static RectTransform CreateRect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    /// <summary>왼쪽 위 기준으로 위치·크기 지정</summary>
    public static void PlaceTopLeft(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    public static void Stretch(RectTransform rt, float padding = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
    }

    public static Image CreatePanel(Transform parent, string name, Color color)
    {
        RectTransform rt = CreateRect(parent, name);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    public static Text CreateText(Transform parent, string name, string text, int size,
                                  TextAnchor align = TextAnchor.MiddleLeft)
    {
        RectTransform rt = CreateRect(parent, name);
        var t = rt.gameObject.AddComponent<Text>();
        t.font = DefaultFont;
        t.text = text;
        t.fontSize = size;
        t.color = TextColor;
        t.alignment = align;
        t.supportRichText = true;
        t.raycastTarget = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    public static Button CreateButton(Transform parent, string name, string label, int fontSize = 20,
                                      TextAnchor align = TextAnchor.MiddleCenter)
    {
        Image bg = CreatePanel(parent, name, ButtonColor);
        var btn = bg.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;

        Text t = CreateText(bg.transform, "Label", label, fontSize, align);
        Stretch(t.rectTransform, 10f);
        return btn;
    }

    public static void SetButtonLabel(Button b, string label)
    {
        Text t = b.GetComponentInChildren<Text>();
        if (t != null) t.text = label;
    }

    /// <summary>레이아웃 안에서 쓸 높이·너비 지정</summary>
    public static LayoutElement SetLayoutSize(Component c, float width = -1f, float height = -1f)
    {
        var le = c.GetComponent<LayoutElement>();
        if (le == null) le = c.gameObject.AddComponent<LayoutElement>();
        if (width >= 0) le.preferredWidth = width;
        if (height >= 0) le.preferredHeight = height;
        return le;
    }

    public static VerticalLayoutGroup AddVertical(GameObject go, float spacing, int padding)
    {
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.padding = new RectOffset(padding, padding, padding, padding);
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return v;
    }

    public static HorizontalLayoutGroup AddHorizontal(GameObject go, float spacing, int padding)
    {
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.padding = new RectOffset(padding, padding, padding, padding);
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;
        return h;
    }
}
