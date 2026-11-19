using UnityEngine;
using UnityEngine.UI;

// Head-locked world-space UI so the same panel is visible to both eyes.
sealed class RecalibrationOverlay
{
    readonly GameObject m_Root;
    readonly RectTransform m_Fill;
    readonly Material m_Material;

    public RecalibrationOverlay(Camera camera)
    {
        m_Root = new GameObject("Recalibration Overlay", typeof(RectTransform), typeof(Canvas));
        m_Root.layer = camera.gameObject.layer;

        var canvas = m_Root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 100;

        var panel = (RectTransform)m_Root.transform;
        panel.SetParent(camera.transform, false);
        panel.localPosition = new Vector3(0f, 0f, 1.25f);
        panel.localScale = Vector3.one * 0.00065f;
        panel.sizeDelta = new Vector2(420f, 120f);

        var shader = Resources.Load<Shader>("RecalibrationOverlay");
        if (shader != null)
            m_Material = new Material(shader);

        CreateGraphic<Image>("Backdrop", panel, panel.sizeDelta, Vector2.zero, new Color32(11, 19, 30, 242));

        var label = CreateGraphic<Text>("Status", panel, new Vector2(384f, 56f), new Vector2(0f, 15f), Color.white);
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 38;
        label.alignment = TextAnchor.MiddleCenter;
        label.text = "Recalibrating...";

        var track = CreateGraphic<Image>("Progress Track", panel, new Vector2(340f, 10f),
            new Vector2(0f, -29f), new Color32(55, 70, 88, 255));
        var fill = CreateGraphic<Image>("Progress Fill", track.rectTransform, Vector2.zero,
            Vector2.zero, new Color32(96, 224, 255, 255));
        m_Fill = fill.rectTransform;
        m_Fill.anchorMin = Vector2.zero;
        m_Fill.anchorMax = Vector2.up;
        m_Fill.offsetMin = Vector2.zero;
        m_Fill.offsetMax = Vector2.zero;
    }

    public void Show(float progress)
    {
        m_Fill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
        if (!m_Root.activeSelf)
            m_Root.SetActive(true);
    }

    public void Hide()
    {
        if (m_Root != null && m_Root.activeSelf)
            m_Root.SetActive(false);
    }

    public void Destroy()
    {
        Object.Destroy(m_Root);
        Object.Destroy(m_Material);
    }

    T CreateGraphic<T>(string name, RectTransform parent, Vector2 size, Vector2 position, Color color)
        where T : Graphic
    {
        var element = new GameObject(name, typeof(RectTransform), typeof(T));
        element.layer = parent.gameObject.layer;
        var graphic = element.GetComponent<T>();
        var rect = graphic.rectTransform;
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        graphic.color = color;
        graphic.material = m_Material;
        graphic.raycastTarget = false;
        return graphic;
    }
}
