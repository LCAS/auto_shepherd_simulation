using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ursaanimation.CubicFarmAnimals
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))] // Ensure a collider exists for physics
    public class SheepController : MonoBehaviour
    {
        /* ───────── BASELINE TUNABLES ───────── */
        private const float AMBLER_SPEED = 3.0f;
        private const float TROT_SPEED = 4.5f;
        private const float BASE_MAX_FORCE = 6f; // Max steering force applied

        private const float BASE_NEIGHBOUR_RADIUS = 6f;
        private const float BASE_SEP_SIDE_RADIUS = 2f;
        private const float BASE_SEP_FORWARD_RADIUS = 2f;
        private const float BASE_SEPARATION_WEIGHT = 1.2f;
        private const float BASE_ALIGNMENT_WEIGHT = 1.0f;
        private const float BASE_COHESION_WEIGHT = 1.2f;
        private const int BASE_MAX_NEIGH_FOR_COH = 8;

        private const float BASE_SIT_CHECK_INTERVAL = 10f;
        private const float BASE_SIT_PROBABILITY = 0.12f;
        private const float BASE_MIN_SIT_TIME = 20f;
        private const float BASE_MAX_SIT_TIME = 60f;
        private const float STAND_SLOW_TIME = 3f;

        private const float OBSTACLE_AVOID_FACTOR = 0.5f;
        private const float FENCE_AVOID_RADIUS = 3f;
        private const float FENCE_AVOID_WEIGHT = 3f;

        private const float PARAM_VARIANCE = 0.25f;
        private const float WEIGHT_VARIANCE = 0.30f;
        private const float BASE_JITTER_STRENGTH = 0.15f;

        private const float GROUND_HEIGHT = -0.5f; // Height of the ground plane
        private const float MIN_HEIGHT = GROUND_HEIGHT + 0.05f; // Slightly above ground
        private const float MAX_HEIGHT = GROUND_HEIGHT + 0.1f; // Very close to ground

        // Force multiplier for movement - **TUNE THIS!**
        [SerializeField] private float movementForceMultiplier = 50f;

        // Momentum and smoothing parameters
        private const float MOMENTUM_FACTOR = 0.85f; // Higher values mean more momentum
        private const float DIRECTION_CHANGE_SPEED = 5f; // Lower values mean slower direction changes
        private Vector3 _currentMomentum = Vector3.zero;

        private const string IDLE_ANIM = "idle";
        private const string WALK_ANIM = "walk_forward";
        private const string TROT_ANIM = "trot_forward";
        private const string STAND2SIT_ANIM = "stand_to_sit";
        private const string SIT2STAND_ANIM = "sit_to_stand";

        private float neighbourRadius, sepSideRadius, sepForwardRadius;
        private float separationWeight, alignmentWeight, cohesionWeight;
        private int maxNeighboursForFullCohesion;
        private float maxForce; // Max steering force, not direct movement force

        private float sitCheckInterval, sitProbability, minSitTime, maxSitTime;

        // _velocity is now more like a 'desired' velocity from boid logic
        private Vector3 _desiredVelocity;
        private Animator _anim;
        private Rigidbody _rb;
        private CapsuleCollider _collider; // Reference to the collider for physics material

        private bool _isSitting;
        private float _standSlowTimer;
        private float _jitterStrength;

        private static readonly List<SheepController> _flock = new();

        private void Awake()
        {
            float V(float v) => v * Random.Range(1f - PARAM_VARIANCE, 1f + PARAM_VARIANCE);
            float W(float w) => w * Random.Range(1f - WEIGHT_VARIANCE, 1f + WEIGHT_VARIANCE);

            neighbourRadius = V(BASE_NEIGHBOUR_RADIUS);
            sepSideRadius = Mathf.Max(V(BASE_SEP_SIDE_RADIUS), 0.3f * neighbourRadius);
            sepForwardRadius = Mathf.Max(V(BASE_SEP_FORWARD_RADIUS), 0.3f * neighbourRadius);

            separationWeight = W(BASE_SEPARATION_WEIGHT);
            alignmentWeight = W(BASE_ALIGNMENT_WEIGHT);
            cohesionWeight = W(BASE_COHESION_WEIGHT);

            maxNeighboursForFullCohesion = Mathf.Max(1, Mathf.RoundToInt(W(BASE_MAX_NEIGH_FOR_COH)));
            maxForce = V(BASE_MAX_FORCE);

            sitCheckInterval = V(BASE_SIT_CHECK_INTERVAL);
            sitProbability = Mathf.Clamp01(W(BASE_SIT_PROBABILITY));
            minSitTime = V(BASE_MIN_SIT_TIME);
            maxSitTime = V(BASE_MAX_SIT_TIME);

            _jitterStrength = W(BASE_JITTER_STRENGTH);

            // Initialize desired velocity for starting movement
            _desiredVelocity = Quaternion.Euler(0, Random.Range(0, 360f), 0) * Vector3.forward * AMBLER_SPEED;

            _anim = GetComponent<Animator>();
            _rb = GetComponent<Rigidbody>();
            _collider = GetComponent<CapsuleCollider>();

            // Configure rigidbody for stable movement
            _rb.isKinematic = false;
            _rb.useGravity = true;
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ; // Only freeze rotations
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _rb.angularDamping = 0.5f;
            _rb.linearDamping = 0.8f; // Increased damping for more controlled movement

            // Set initial position to be at the correct height
            Vector3 pos = transform.position;
            pos.y = MIN_HEIGHT;
            transform.position = pos;

            _flock.Add(this);
        }

        private void Start()
        {
            StartCoroutine(RestRoutine());
        }

        private void OnDestroy() => _flock.Remove(this);

        // --- IMPORTANT CHANGE: Use FixedUpdate for physics calculations ---
        private void FixedUpdate()
        {
            if (_isSitting)
            {
                if (!_rb.isKinematic)
                {
                    _rb.linearVelocity = Vector3.zero;
                    _rb.angularVelocity = Vector3.zero;
                    _currentMomentum = Vector3.zero;
                }
                return;
            }

            // Ensure sheep stays at the correct height
            Vector3 currentPos = transform.position;
            if (currentPos.y != MIN_HEIGHT)
            {
                currentPos.y = MIN_HEIGHT;
                transform.position = currentPos;
                Vector3 vel = _rb.linearVelocity;
                vel.y = 0f;
                _rb.linearVelocity = vel;
            }
            else if (currentPos.y > MAX_HEIGHT)
            {
                _rb.AddForce(Vector3.down * _rb.mass * 9.81f, ForceMode.Acceleration);
            }

            Vector3 steering = ComputeBoidSteering();
            Vector3 jitter = Random.insideUnitSphere;
            jitter.y = 0f;
            steering += jitter * maxForce * _jitterStrength;

            int neighbourCount = CountNeighbours();
            float targetSpeed = (neighbourCount >= 2 && _standSlowTimer <= 0f) ? TROT_SPEED : AMBLER_SPEED;

            // Apply momentum to desired velocity
            _desiredVelocity = Vector3.ClampMagnitude(_desiredVelocity + steering * Time.fixedDeltaTime, targetSpeed);
            _currentMomentum = Vector3.Lerp(_currentMomentum, _desiredVelocity, Time.fixedDeltaTime * DIRECTION_CHANGE_SPEED);
            _currentMomentum = Vector3.ClampMagnitude(_currentMomentum, targetSpeed);

            Vector3 currentHorizontalVelocity = new Vector3(_rb.linearVelocity.x, 0, _rb.linearVelocity.z);
            Vector3 desiredHorizontalVelocity = new Vector3(_currentMomentum.x, 0, _currentMomentum.z);

            // Calculate force with momentum consideration
            Vector3 forceToApply = desiredHorizontalVelocity - currentHorizontalVelocity;
            forceToApply.y = 0f;

            // Apply force with momentum
            _rb.AddForce(forceToApply * movementForceMultiplier, ForceMode.Acceleration);

            // Smooth rotation based on momentum
            if (_currentMomentum.sqrMagnitude > 0.0001f)
            {
                Vector3 flatMomentum = new Vector3(_currentMomentum.x, 0, _currentMomentum.z);
                if (flatMomentum.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(flatMomentum.normalized);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, 8f * Time.fixedDeltaTime);
                }
            }

            string moveAnim = (_rb.linearVelocity.magnitude > (AMBLER_SPEED + 0.1f)) ? TROT_ANIM : WALK_ANIM;
            if (!string.IsNullOrEmpty(moveAnim)) _anim.Play(moveAnim, 0);
        }

        private int CountNeighbours()
        {
            int count = 0;
            Vector3 pos = transform.position;
            foreach (var other in _flock)
            {
                if (other == this) continue;
                if ((other.transform.position - pos).sqrMagnitude < neighbourRadius * neighbourRadius) count++;
            }
            return count;
        }

        private Vector3 ComputeBoidSteering()
        {
            Vector3 pos = transform.position;
            Vector3 separation = Vector3.zero;
            Vector3 alignment = Vector3.zero;
            Vector3 cohesion = Vector3.zero;
            Vector3 fenceAvoid = Vector3.zero;
            int neighbourCount = 0;

            // Stronger Fence avoidance (fear)
            Collider[] fences = Physics.OverlapSphere(pos, FENCE_AVOID_RADIUS);
            foreach (var col in fences)
            {
                if (!col.CompareTag("Fence")) continue;
                Vector3 closest = col.ClosestPoint(pos);
                Vector3 toFence = pos - closest;
                float dist = toFence.magnitude;
                if (dist < 0.0001f) continue;

                float strength = Mathf.Clamp01((FENCE_AVOID_RADIUS - dist) / FENCE_AVOID_RADIUS);
                strength = strength * strength;
                fenceAvoid += toFence.normalized * strength * FENCE_AVOID_WEIGHT;
            }

            float obstacleRadius = neighbourRadius * OBSTACLE_AVOID_FACTOR;

            foreach (var other in _flock)
            {
                if (other == this) continue;

                Vector3 toOther = other.transform.position - pos;
                float dist = toOther.magnitude;
                if (dist > neighbourRadius) continue;

                // Enhanced separation for close proximity
                if (dist < obstacleRadius && dist > 0.0001f)
                {
                    // Stronger separation force for very close sheep
                    float separationStrength = Mathf.Pow((obstacleRadius - dist) / obstacleRadius, 2) * 3f;
                    Vector3 separationDir = -toOther.normalized;
                    separationDir.y = 0f; // Remove upward component
                    separation += separationDir * separationStrength;
                }

                neighbourCount++;
                alignment += other._rb.linearVelocity;
                cohesion += other.transform.position;

                // Enhanced side and forward separation
                Vector3 local = transform.InverseTransformDirection(toOther);
                float sx = local.x / sepSideRadius;
                float sz = local.z / sepForwardRadius;
                float inside = sx * sx + sz * sz;
                if (inside < 1f && dist > 0.0001f)
                {
                    float strength = Mathf.Pow(1f - inside, 2); // Quadratic falloff for stronger near-field effect
                    Vector3 sepDir = -toOther.normalized;
                    sepDir.y = 0f; // Remove upward component
                    separation += sepDir * strength;
                }
            }

            if (neighbourCount > 0)
            {
                alignment = (alignment / neighbourCount).normalized * TROT_SPEED - _rb.linearVelocity; // Use current Rigidbody velocity
                Vector3 centre = (cohesion / neighbourCount);
                Vector3 toCentre = centre - pos;
                float densityFactor = Mathf.Clamp01((float)neighbourCount / maxNeighboursForFullCohesion);
                cohesion = toCentre.normalized * TROT_SPEED - _rb.linearVelocity; // Use current Rigidbody velocity
                cohesion *= (1f - densityFactor);
            }

            separation = separation.normalized * TROT_SPEED - _rb.linearVelocity; // Use current Rigidbody velocity

            Vector3 steer =
                separation * separationWeight +
                alignment * alignmentWeight +
                cohesion * cohesionWeight +
                fenceAvoid; // already scaled above

            return Vector3.ClampMagnitude(steer, maxForce);
        }

        private IEnumerator RestRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(sitCheckInterval);
                if (_isSitting) continue;
                if (Random.value < sitProbability)
                {
                    StartCoroutine(SitCoroutine());
                }
            }
        }

        private IEnumerator SitCoroutine()
        {
            if (_isSitting) yield break;
            _isSitting = true;

            // Stop movement while sitting
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true; // Make kinematic during sitting to freeze position precisely

            if (!string.IsNullOrEmpty(STAND2SIT_ANIM)) _anim.Play(STAND2SIT_ANIM, 0);
            yield return new WaitForSeconds(1f); // Animation duration

            float wait = Random.Range(minSitTime, maxSitTime);
            yield return new WaitForSeconds(wait);

            if (!string.IsNullOrEmpty(SIT2STAND_ANIM)) _anim.Play(SIT2STAND_ANIM, 0);
            yield return new WaitForSeconds(1f); // Animation duration

            _rb.isKinematic = false; // Return to physics control after standing up
            _isSitting = false;
            _standSlowTimer = STAND_SLOW_TIME;
        }
    }
}