using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Moves the XR Origin based on where the right controller is held relative to a calibrated
/// neutral position, like a joystick you push with your whole hand.
///   Move hand forward / back / left / right of neutral -> origin moves that way.
///   The farther from neutral, the faster it moves.
///
/// Add this to the "XR Origin (VR)" GameObject. Works with the OpenXR plugin; the controller
/// pose is read directly from the Input System, so no controller objects need to be wired up.
/// The hand position on the first tracked frame is the neutral position. Press the right
/// primary button (A) to re-zero it wherever your hand currently is.
/// </summary>
[DisallowMultipleComponent]
public class TiltController : MonoBehaviour
{
    [Header("Input")]
    [Tooltip("Right controller position (Vector3), in XR Origin tracking space.")]
    [SerializeField] InputActionProperty m_PositionAction = new InputActionProperty(
        new InputAction("Right Controller Position", InputActionType.Value,
                        "<XRController>{RightHand}/devicePosition", expectedControlType: "Vector3"));

    [Tooltip("When this button is pressed, the current hand position becomes the neutral (no movement) position.")]
    [SerializeField] InputActionProperty m_CalibrateAction = new InputActionProperty(
        new InputAction("Right Controller Calibrate", InputActionType.Button,
                        "<XRController>{RightHand}/primaryButton"));

    [Header("Movement")]
    [Tooltip("Top speed in meters/second when the hand is at Max Offset.")]
    [SerializeField] float m_MaxSpeed = 5f;

    [Tooltip("Hand distance from neutral (meters) that produces full speed.")]
    [SerializeField, Range(0.05f, 0.6f)] float m_MaxOffset = 0.25f;

    [Tooltip("Hand distance from neutral (meters) ignored as noise/hand tremor.")]
    [SerializeField, Range(0f, 0.1f)] float m_DeadZone = 0.03f;

    [Tooltip("Shapes the response. 1 = linear, >1 = gentler near neutral.")]
    [SerializeField, Range(1f, 3f)] float m_ResponseCurve = 1.25f;

    [Tooltip("Also move up/down when the hand moves up/down.")]
    [SerializeField] bool m_EnableVertical = false;

    [Tooltip("Optional. Transform whose yaw defines 'forward'. Defaults to this XR Origin (so turning steers movement). " +
             "Assign the camera to move relative to where you look.")]
    [SerializeField] Transform m_ForwardSource;

    Vector3 m_Neutral;
    bool m_HasNeutral;

    void OnEnable()
    {
        Enable(m_PositionAction);
        Enable(m_CalibrateAction);
        m_HasNeutral = false;
    }

    void OnDisable()
    {
        Disable(m_PositionAction);
        Disable(m_CalibrateAction);
    }

    void Update()
    {
        var positionAction = m_PositionAction.action;
        if (positionAction == null || positionAction.controls.Count == 0)
            return; // Controller not connected/tracked yet.

        var position = positionAction.ReadValue<Vector3>();

        var calibrate = m_CalibrateAction.action;
        if (!m_HasNeutral || (calibrate != null && calibrate.WasPressedThisFrame()))
        {
            m_Neutral = position;
            m_HasNeutral = true;
        }

        var offset = position - m_Neutral;
        if (!m_EnableVertical)
            offset.y = 0f;

        var input = ApplyDeadZoneAndCurve(offset);
        if (input == Vector3.zero)
            return;

        // Tracking-space offset -> world direction using the source's yaw.
        var source = m_ForwardSource != null ? m_ForwardSource : transform;
        var forward = Vector3.ProjectOnPlane(source.forward, Vector3.up).normalized;
        var right = Vector3.ProjectOnPlane(source.right, Vector3.up).normalized;

        var direction = right * input.x + Vector3.up * input.y + forward * input.z;
        transform.position += direction * (m_MaxSpeed * Time.deltaTime);
    }

    Vector3 ApplyDeadZoneAndCurve(Vector3 offset)
    {
        var distance = offset.magnitude;
        if (distance <= m_DeadZone)
            return Vector3.zero;

        var t = Mathf.Clamp01((distance - m_DeadZone) / Mathf.Max(0.001f, m_MaxOffset - m_DeadZone));
        return offset / distance * Mathf.Pow(t, m_ResponseCurve);
    }

    static void Enable(InputActionProperty property)
    {
        // Only enable actions we own (not references to actions managed by an InputActionManager).
        if (property.reference == null && property.action != null)
            property.action.Enable();
    }

    static void Disable(InputActionProperty property)
    {
        if (property.reference == null && property.action != null)
            property.action.Disable();
    }
}
