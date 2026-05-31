using UnityEngine;

namespace Controller
{
    [RequireComponent(typeof(CreatureMover))]
    public class DogController : MonoBehaviour
    {
        [Header("Dog Movement")]
        [SerializeField] private float closeEnoughDistance = 1f;
        [SerializeField] private bool stopWhenArrived = true;
        [SerializeField] private float dogSpeedMultiplier = 2f;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLogs = true;
        [SerializeField] private float debugLogInterval = 1f;

        private CreatureMover m_Mover;
        private Vector2 m_Axis;
        private bool m_IsRun;
        private bool m_IsJump;
        private Vector3 m_Target;
        private bool m_HasTarget;
        private float m_NextDebugLogTime;
        private bool m_LoggedMissingMover;

        public Vector3 CurrentTarget => m_Target;
        public bool HasTarget => m_HasTarget;

        private void Awake()
        {
            m_Mover = GetComponent<CreatureMover>();

            if (m_Mover != null)
            {
                m_Mover.SetSpeedMultiplier(Mathf.Max(0.01f, dogSpeedMultiplier));
            }

            if (enableDebugLogs)
            {
                if (m_Mover == null)
                {
                    Debug.LogWarning("[DogController] CreatureMover component is missing.", this);
                }
                else
                {
                    Debug.Log($"[DogController] Ready on '{name}'. closeEnoughDistance={closeEnoughDistance}, stopWhenArrived={stopWhenArrived}", this);
                }
            }
        }

        private void Update()
        {
            if (m_Mover == null && !m_LoggedMissingMover)
            {
                m_LoggedMissingMover = true;
                if (enableDebugLogs)
                {
                    Debug.LogWarning("[DogController] Cannot drive dog because CreatureMover is null.", this);
                }
            }

            if (m_HasTarget)
            {
                DriveTowardsTarget(m_Target);

                if (enableDebugLogs && Time.time >= m_NextDebugLogTime)
                {
                    float distance = Vector3.Distance(transform.position, m_Target);
                    Debug.Log($"[DogController] Moving to target {m_Target}. distance={distance:F2}", this);
                    m_NextDebugLogTime = Time.time + Mathf.Max(0.1f, debugLogInterval);
                }

                if (Vector3.Distance(transform.position, m_Target) <= closeEnoughDistance && stopWhenArrived)
                {
                    ClearTarget();
                    SetIdleInput();
                }

                return;
            }

            HandleManualInput();
        }

        public void SetTarget(Vector3 target)
        {
            m_Target = target;
            m_HasTarget = true;

            if (enableDebugLogs)
            {
                Debug.Log($"[DogController] New target set to {target}", this);
            }
        }

        public void ClearTarget()
        {
            m_HasTarget = false;

            if (enableDebugLogs)
            {
                Debug.Log("[DogController] Target cleared.", this);
            }
        }

        private void DriveTowardsTarget(Vector3 destination)
        {
            m_Axis = new Vector2(0f, 1f);
            m_IsRun = true;
            m_IsJump = false;

            if (m_Mover != null)
            {
                m_Mover.SetInput(in m_Axis, in destination, in m_IsRun, m_IsJump);
            }
        }

        private void HandleManualInput()
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            float vertical = Input.GetAxisRaw("Vertical");

            if (Mathf.Abs(horizontal) < 0.01f && Mathf.Abs(vertical) < 0.01f)
            {
                SetIdleInput();
                return;
            }

            m_Axis = new Vector2(horizontal, vertical);
            if (m_Axis.sqrMagnitude > 1f)
            {
                m_Axis.Normalize();
            }

            m_Target = transform.position + transform.forward;
            m_IsRun = Input.GetKey(KeyCode.LeftShift) || vertical > 0.5f;
            m_IsJump = false;

            if (enableDebugLogs && Time.time >= m_NextDebugLogTime)
            {
                Debug.Log($"[DogController] Manual input axis=({m_Axis.x:F2},{m_Axis.y:F2}), run={m_IsRun}", this);
                m_NextDebugLogTime = Time.time + Mathf.Max(0.1f, debugLogInterval);
            }

            if (m_Mover != null)
            {
                m_Mover.SetInput(in m_Axis, in m_Target, in m_IsRun, m_IsJump);
            }
        }

        private void SetIdleInput()
        {
            m_Axis = Vector2.zero;
            m_IsRun = false;
            m_IsJump = false;
            Vector3 idleTarget = transform.position;

            if (m_Mover != null)
            {
                m_Mover.SetInput(in m_Axis, in idleTarget, in m_IsRun, m_IsJump);
            }
        }
    }
}

