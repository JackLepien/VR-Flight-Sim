using Unity.XR.CoreUtils;
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
    public enum ControlSource { VRController, LocalServer }

    [Header("Control Source")]
    [Tooltip("VR Controller preserves the calibrated hand controls. Local Server uses browser cyclic, collective and pedals.")]
    [SerializeField] ControlSource m_ControlSource = ControlSource.VRController;

    [Header("Local Server Movement")]
    [SerializeField, Min(0f)] float m_ClimbSpeed = 3f;
    [SerializeField, Min(0f)] float m_YawSpeed = 45f;

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

    [Header("Helicopter Tilt")]
    [Tooltip("Tilt the cockpit view with movement: pitch forward/back and bank into sideways movement. " +
             "Added on top of headset rotation.")]
    [SerializeField] bool m_TiltWithMovement = true;

    [Tooltip("Degrees the view pitches down at full forward speed (and up at full backward speed).")]
    [SerializeField, Range(0f, 45f)] float m_MaxPitch = 15f;

    [Tooltip("Degrees the view banks toward the side at full sideways speed.")]
    [SerializeField, Range(0f, 45f)] float m_MaxRoll = 15f;

    [Tooltip("How quickly the tilt follows movement. Higher = snappier, lower = floatier.")]
    [SerializeField, Range(0.5f, 15f)] float m_TiltResponsiveness = 3f;

    [Tooltip("Optional. Defaults to the XROrigin on this GameObject.")]
    [SerializeField] XROrigin m_Origin;

    Vector3 m_Neutral;
    bool m_HasNeutral;
    HelicopterControlReceiver m_ServerInput;
    float m_Pitch; // current applied tilt, degrees (positive = nose down)
    float m_Roll;  // current applied tilt, degrees (positive = bank right)

    void Awake()
    {
        if (m_Origin == null)
            m_Origin = GetComponent<XROrigin>();
    }

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
        if (m_ServerInput != null)
            m_ServerInput.enabled = false;
    }

    void Update()
    {
        bool useServer = m_ControlSource == ControlSource.LocalServer;
        Vector3 input;
        if (useServer)
        {
            if (m_ServerInput == null)
            {
                m_ServerInput = GetComponent<HelicopterControlReceiver>();
                if (m_ServerInput == null)
                    m_ServerInput = gameObject.AddComponent<HelicopterControlReceiver>();
            }
            m_ServerInput.enabled = true;
            m_ServerInput.TryRead(out var controls);
            input = new Vector3(controls.cyclicX, controls.collective, controls.cyclicY);

            var pivot = m_Origin != null && m_Origin.Camera != null ? m_Origin.Camera.transform : transform;
            transform.RotateAround(pivot.position, Vector3.up, controls.yaw * m_YawSpeed * Time.deltaTime);
        }
        else
        {
            if (m_ServerInput != null)
                m_ServerInput.enabled = false;
            input = ReadMoveInput();
        }

        if (input != Vector3.zero)
        {
            // Tracking-space offset -> world direction using the source's yaw.
            var source = m_ForwardSource != null ? m_ForwardSource : transform;
            var forward = Vector3.ProjectOnPlane(source.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(source.right, Vector3.up).normalized;

            float verticalSpeed = useServer ? m_ClimbSpeed : m_MaxSpeed;
            var velocity = (right * input.x + forward * input.z) * m_MaxSpeed
                + Vector3.up * (input.y * verticalSpeed);
            transform.position += velocity * Time.deltaTime;
        }

        // Runs every frame (even with no input) so the view eases back to level when you stop.
        ApplyTilt(input);
    }

    /// <summary>Returns the dead-zoned, curved hand offset from neutral, or zero if untracked.</summary>
    Vector3 ReadMoveInput()
    {
        var positionAction = m_PositionAction.action;
        if (positionAction == null || positionAction.controls.Count == 0)
            return Vector3.zero; // Controller not connected/tracked yet.

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

        return ApplyDeadZoneAndCurve(offset);
    }

    /// <summary>
    /// Helicopter effect: pitches the XR Origin forward/back and banks it toward sideways movement.
    /// The rotation pivots around the camera and is added on top of the headset's own rotation, so
    /// looking around still works normally. Tilt is rebuilt from the origin's yaw each frame
    /// (yaw * pitch * roll), so it never drifts and turning with other providers still works.
    /// </summary>
    void ApplyTilt(Vector3 input)
    {
        var targetPitch = m_TiltWithMovement ? input.z * m_MaxPitch : 0f;
        var targetRoll = m_TiltWithMovement ? input.x * m_MaxRoll : 0f;

        var blend = 1f - Mathf.Exp(-m_TiltResponsiveness * Time.deltaTime);
        m_Pitch = Mathf.Lerp(m_Pitch, targetPitch, blend);
        m_Roll = Mathf.Lerp(m_Roll, targetRoll, blend);

        // Nothing to do once level and not tilting.
        if (!m_TiltWithMovement && Mathf.Approximately(m_Pitch, 0f) && Mathf.Approximately(m_Roll, 0f)
            && transform.up == Vector3.up)
            return;

        // Heading is the flattened forward; roll doesn't move forward and pitch < 90 keeps it valid.
        var flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 1e-6f)
            return;
        var yaw = Quaternion.LookRotation(flatForward.normalized, Vector3.up);

        // Positive pitch = nose down (Unity +X). Positive roll = bank right (Unity -Z).
        var tilt = Quaternion.Euler(m_Pitch, 0f, -m_Roll);

        // Rotate about the camera so the head stays put instead of swinging around the origin.
        var pivot = m_Origin != null && m_Origin.Camera != null ? m_Origin.Camera.transform : transform;
        var pivotBefore = pivot.position;
        transform.rotation = yaw * tilt;
        transform.position += pivotBefore - pivot.position;
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
