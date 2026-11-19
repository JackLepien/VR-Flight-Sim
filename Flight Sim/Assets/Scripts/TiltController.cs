using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Moves the XR Origin based on where the right controller is held relative to a calibrated
/// neutral position, like a joystick you push with your whole hand.
///   Move hand forward / back / left / right of neutral -> origin moves that way.
///   The farther from neutral, the faster it moves.
///   Squeeze the right trigger to ascend or the right grip to descend.
///   Vertical speed is proportional to the difference in squeeze amounts.
///   Move the right thumbstick left or right to turn.
///
/// Add this to the "XR Origin (VR)" GameObject. Works with the OpenXR plugin; the controller
/// pose is read directly from the Input System, so no controller objects need to be wired up.
/// The hand position on the first tracked frame is the neutral position. Press A or B on
/// the right controller to re-zero it at your current hand position and pause flight movement.
/// </summary>
[DisallowMultipleComponent]
public class TiltController : MonoBehaviour
{
    public enum ControlSource { VRController, LocalServer }

    /// <summary>Translational flight speed normalized by the horizontal and vertical speed settings.</summary>
    public float MovementSpeedFraction { get; private set; }

    /// <summary>Flight translation in world meters per second.</summary>
    public Vector3 FlightVelocity { get; private set; }

    /// <summary>Configured top horizontal flight speed in world meters per second.</summary>
    public float MaxSpeed => Mathf.Max(0f, m_MaxSpeed);

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

    [Tooltip("Right A button sets the current hand position as neutral and pauses flight movement.")]
    [SerializeField] InputActionProperty m_CalibrateAction = new InputActionProperty(
        new InputAction("Right Controller Calibrate", InputActionType.Button,
                        "<XRController>{RightHand}/primaryButton"));

    [Tooltip("Right B button sets the current hand position as neutral and pauses flight movement.")]
    [SerializeField] InputActionProperty m_SecondaryCalibrateAction = new InputActionProperty(
        new InputAction("Right Controller Secondary Calibrate", InputActionType.Button,
                        "<XRController>{RightHand}/secondaryButton"));

    [Tooltip("Right trigger squeeze (0 to 1) controls ascent.")]
    [SerializeField] InputActionProperty m_AscendAction = new InputActionProperty(
        new InputAction("Right Controller Ascend", InputActionType.Value,
                        "<XRController>{RightHand}/trigger", expectedControlType: "Axis"));

    [Tooltip("Right grip squeeze (0 to 1) controls descent.")]
    [SerializeField] InputActionProperty m_DescendAction = new InputActionProperty(
        new InputAction("Right Controller Descend", InputActionType.Value,
                        "<XRController>{RightHand}/grip", expectedControlType: "Axis"));

    [Tooltip("The right thumbstick's horizontal axis controls yaw.")]
    [SerializeField] InputActionProperty m_YawAction = new InputActionProperty(
        new InputAction("Right Controller Yaw", InputActionType.Value,
                        "<XRController>{RightHand}/primary2DAxis", expectedControlType: "Vector2"));

    [Header("Calibration")]
    [Tooltip("Seconds to hold the XR Origin's position and rotation after pressing A or B to recalibrate.")]
    [SerializeField, Min(0f)] float m_RecalibrationPauseSeconds = 1f;

    [Tooltip("Size multiplier for the recalibration panel, text, and progress bar.")]
    [SerializeField, Min(0.1f)] float m_RecalibrationOverlayScale = 1.5f;

    [Header("Movement")]
    [Tooltip("Top horizontal speed in meters/second when the hand is at Max Offset.")]
    [SerializeField] float m_MaxSpeed = 5f;

    [Tooltip("Top climb and descent speed in meters/second at full VR trigger or grip squeeze.")]
    [SerializeField, Min(0f)] float m_VRVerticalSpeed = 5f;

    [Tooltip("Top turn speed in degrees per second at full right thumbstick deflection.")]
    [SerializeField, Min(0f)] float m_VRYawSpeed = 45f;

    [Tooltip("Hand distance from neutral (meters) that produces full speed.")]
    [SerializeField, Range(0.05f, 0.6f)] float m_MaxOffset = 0.25f;

    [Tooltip("Hand distance from neutral (meters) ignored as noise/hand tremor.")]
    [SerializeField, Range(0f, 0.1f)] float m_DeadZone = 0.03f;

    [Tooltip("Shapes the response. 1 = linear, >1 = gentler near neutral.")]
    [SerializeField, Range(1f, 3f)] float m_ResponseCurve = 1.25f;

    [Tooltip("Add vertical hand movement to VR trigger and grip altitude input.")]
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
    double m_RecalibrationStartTime;
    double m_ResumeMovementTime;
    RecalibrationOverlay m_RecalibrationOverlay;
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
        Enable(m_SecondaryCalibrateAction);
        Enable(m_AscendAction);
        Enable(m_DescendAction);
        Enable(m_YawAction);
        m_HasNeutral = false;
        m_ResumeMovementTime = 0d;
    }

    void OnDisable()
    {
        MovementSpeedFraction = 0f;
        FlightVelocity = Vector3.zero;
        Disable(m_PositionAction);
        Disable(m_CalibrateAction);
        Disable(m_SecondaryCalibrateAction);
        Disable(m_AscendAction);
        Disable(m_DescendAction);
        Disable(m_YawAction);
        m_RecalibrationOverlay?.Hide();
        if (m_ServerInput != null)
            m_ServerInput.enabled = false;
    }

    void OnDestroy() => m_RecalibrationOverlay?.Destroy();

    void Update()
    {
        MovementSpeedFraction = 0f;
        FlightVelocity = Vector3.zero;
        bool useServer = m_ControlSource == ControlSource.LocalServer;
        Vector3 input;
        float yawRate;
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
            yawRate = controls.yaw * m_YawSpeed;
        }
        else
        {
            if (m_ServerInput != null)
                m_ServerInput.enabled = false;
            input = ReadMoveInput();
            yawRate = ReadYawInput() * m_VRYawSpeed;
        }

        var now = Time.unscaledTimeAsDouble;
        if (now < m_ResumeMovementTime)
        {
            if (m_RecalibrationOverlay == null && m_Origin != null && m_Origin.Camera != null)
                m_RecalibrationOverlay = new RecalibrationOverlay(m_Origin.Camera, m_RecalibrationOverlayScale);

            var progress = (float)((now - m_RecalibrationStartTime) / (m_ResumeMovementTime - m_RecalibrationStartTime));
            m_RecalibrationOverlay?.Show(progress);
            // Freeze the current pose, including the pitch and roll applied by ApplyTilt.
            return;
        }
        m_RecalibrationOverlay?.Hide();

        if (yawRate != 0f)
        {
            var pivot = m_Origin != null && m_Origin.Camera != null ? m_Origin.Camera.transform : transform;
            transform.RotateAround(pivot.position, Vector3.up, yawRate * Time.deltaTime);
        }

        if (input != Vector3.zero)
        {
            // Tracking-space offset -> world direction using the source's yaw.
            var source = m_ForwardSource != null ? m_ForwardSource : transform;
            var forward = Vector3.ProjectOnPlane(source.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(source.right, Vector3.up).normalized;

            float verticalSpeed = useServer ? m_ClimbSpeed : m_VRVerticalSpeed;
            var horizontalInput = right * input.x + forward * input.z;
            FlightVelocity = horizontalInput * m_MaxSpeed + Vector3.up * (input.y * verticalSpeed);
            transform.position += FlightVelocity * Time.deltaTime;

            float horizontalFractionSquared = m_MaxSpeed != 0f ? horizontalInput.sqrMagnitude : 0f;
            float verticalFraction = verticalSpeed != 0f ? input.y : 0f;
            MovementSpeedFraction = Mathf.Sqrt(Mathf.Min(1f, horizontalFractionSquared + verticalFraction * verticalFraction));
        }

        // Runs every frame (even with no input) so the view eases back to level when you stop.
        ApplyTilt(input);
    }

    float ReadYawInput()
    {
        const float deadZone = 0.15f;
        var stick = m_YawAction.action?.ReadValue<Vector2>() ?? Vector2.zero;
        var magnitude = Mathf.InverseLerp(deadZone, 1f, Mathf.Abs(stick.x));
        return Mathf.Sign(stick.x) * magnitude;
    }

    /// <summary>Combines the calibrated hand offset with analog altitude input.</summary>
    Vector3 ReadMoveInput()
    {
        var ascend = Mathf.Clamp01(m_AscendAction.action?.ReadValue<float>() ?? 0f);
        var descend = Mathf.Clamp01(m_DescendAction.action?.ReadValue<float>() ?? 0f);
        var altitude = ascend - descend;

        var positionAction = m_PositionAction.action;
        if (positionAction == null || positionAction.controls.Count == 0)
            return Vector3.up * altitude;

        var position = positionAction.ReadValue<Vector3>();

        bool recalibrate = (m_CalibrateAction.action?.WasPressedThisFrame() ?? false)
            || (m_SecondaryCalibrateAction.action?.WasPressedThisFrame() ?? false);
        if (!m_HasNeutral || recalibrate)
        {
            m_Neutral = position;
            m_HasNeutral = true;
        }

        if (recalibrate)
        {
            m_RecalibrationStartTime = Time.unscaledTimeAsDouble;
            m_ResumeMovementTime = m_RecalibrationStartTime + m_RecalibrationPauseSeconds;
        }

        var offset = position - m_Neutral;
        if (!m_EnableVertical)
            offset.y = 0f;

        var input = ApplyDeadZoneAndCurve(offset);
        input.y = Mathf.Clamp(input.y + altitude, -1f, 1f);
        return input;
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
