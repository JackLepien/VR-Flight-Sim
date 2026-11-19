using UnityEngine;

[DisallowMultipleComponent]
public sealed class BatRiderMount : MonoBehaviour
{
    const float IdleWingSpeed = 0.5f;
    const float WingSpeedResponsiveness = 6f;

    [SerializeField] Transform m_RiderView;
    [Tooltip("Seat position relative to the headset in the flight rig's axes.")]
    [SerializeField] Vector3 m_SeatOffset = new Vector3(0f, -1.35f, 0.25f);

    TiltController m_FlightController;
    Animator m_WingAnimator;

    void Awake()
    {
        m_FlightController = GetComponentInParent<TiltController>();
        m_WingAnimator = GetComponentInChildren<Animator>();
        if (m_WingAnimator != null)
            m_WingAnimator.speed = IdleWingSpeed;
    }

    void LateUpdate()
    {
        var rig = transform.parent;
        if (m_RiderView != null && rig != null)
            transform.localPosition = rig.InverseTransformPoint(m_RiderView.position) + m_SeatOffset;

        if (m_WingAnimator != null)
        {
            float speedFraction = m_FlightController != null ? m_FlightController.MovementSpeedFraction : 0f;
            float targetSpeed = Mathf.Lerp(IdleWingSpeed, 1f, speedFraction);
            float blend = 1f - Mathf.Exp(-WingSpeedResponsiveness * Time.deltaTime);
            m_WingAnimator.speed = Mathf.Lerp(m_WingAnimator.speed, targetSpeed, blend);
        }
    }
}
