using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent, RequireComponent(typeof(Canvas))]
public sealed class FlightInstrumentPanel : MonoBehaviour
{
    // The needle sprites aren't drawn pointing up in their source textures.
    const float LongNeedleUp = -49.847f;
    const float ShortNeedleUp = 29f;

    [SerializeField] Transform m_FlightRig;
    [SerializeField] RectTransform m_SpeedNeedle;
    [SerializeField] RectTransform m_AltitudeNeedle;
    [SerializeField] RectTransform m_AltitudeShortNeedle;
    [SerializeField] Shader m_OverlayShader;
    [Tooltip("Scene elevation in meters where the altimeter reads zero.")]
    [SerializeField] float m_AltitudeFloor;

    TiltController m_FlightController;
    Transform m_View;
    Material m_OverlayMaterial;
    Text m_SpeedReadout;
    Text m_AltitudeReadout;
    int m_LastSpeed = int.MinValue;
    int m_LastAltitude = int.MinValue;

    void Start()
    {
        var origin = m_FlightRig != null ? m_FlightRig.GetComponent<XROrigin>() : null;
        if (origin == null || origin.Camera == null || !m_FlightRig.TryGetComponent(out m_FlightController))
        {
            Debug.LogWarning("The flight instrument panel needs an XR Origin with a camera and Tilt Controller.", this);
            enabled = false;
            return;
        }

        var canvas = GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = origin.Camera;
        canvas.sortingOrder = 10;

        m_View = origin.Camera.transform;
        var panel = (RectTransform)transform;
        panel.SetParent(m_View, false);
        panel.localPosition = new Vector3(-0.35f, 0.12f, 1.25f);
        panel.localRotation = Quaternion.identity;
        panel.localScale = Vector3.one * 0.00145f;
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(340f, 350f);

        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        m_SpeedReadout = CreateReadout("Speed readout", m_SpeedNeedle, font);
        m_AltitudeReadout = CreateReadout("Altitude readout", m_AltitudeNeedle, font);

        if (m_OverlayShader != null)
            m_OverlayMaterial = new Material(m_OverlayShader);

        foreach (var graphic in GetComponentsInChildren<Graphic>())
        {
            graphic.raycastTarget = false;
            if (m_OverlayMaterial != null)
                graphic.material = m_OverlayMaterial;
        }
    }

    void LateUpdate()
    {
        float speed = m_FlightController.FlightVelocity.magnitude * 3.6f;
        float altitude = Mathf.Max(0f, m_View.position.y - m_AltitudeFloor);

        // Scale the needle so the configured top flight speed points to 180 on the dial.
        // The numeric readout remains the actual speed and covers values below the dial's 40 mark.
        float speedFraction = m_FlightController.MaxSpeed > 0f
            ? Mathf.Clamp01(m_FlightController.FlightVelocity.magnitude / m_FlightController.MaxSpeed)
            : 0f;
        float indicatedSpeed = speedFraction * 180f;
        float speedAngle = Mathf.Max(0f, indicatedSpeed - 40f) * 1.8f;
        m_SpeedNeedle.localRotation = Quaternion.Euler(0f, 0f, LongNeedleUp - speedAngle);

        // The long hand shows the meters within the current hundred. The small hand shows hundreds.
        m_AltitudeNeedle.localRotation = Quaternion.Euler(0f, 0f, LongNeedleUp - (altitude % 100f) * 3.6f);
        m_AltitudeShortNeedle.localRotation = Quaternion.Euler(0f, 0f, ShortNeedleUp - (altitude % 1000f) * 0.36f);

        int roundedSpeed = Mathf.RoundToInt(speed);
        int roundedAltitude = Mathf.RoundToInt(altitude);
        if (m_LastSpeed != roundedSpeed)
        {
            m_LastSpeed = roundedSpeed;
            m_SpeedReadout.text = $"{roundedSpeed} km/h";
        }
        if (m_LastAltitude != roundedAltitude)
        {
            m_LastAltitude = roundedAltitude;
            m_AltitudeReadout.text = $"{roundedAltitude} m";
        }
    }

    void OnDestroy() => Destroy(m_OverlayMaterial);

    Text CreateReadout(string name, RectTransform needle, Font font)
    {
        var readout = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(Shadow));
        readout.layer = gameObject.layer;

        var label = readout.GetComponent<Text>();
        var rect = label.rectTransform;
        rect.SetParent(transform, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(191f, needle.anchoredPosition.y);
        rect.sizeDelta = new Vector2(145f, 36f);
        label.font = font;
        label.fontSize = 26;
        label.alignment = TextAnchor.MiddleLeft;
        label.color = Color.white;

        var shadow = readout.GetComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
        return label;
    }
}
