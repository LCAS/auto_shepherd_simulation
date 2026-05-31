using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ursaanimation.CubicFarmAnimals
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(Rigidbody))]
    public class SheepController : MonoBehaviour
    {
        private const float AMBLER_SPEED = 3.0f;
        private const float TROT_SPEED = 4.5f;
        private const float BASE_MAX_FORCE = 6f;

        private const float BASE_NEIGHBOUR_RADIUS = 6f;
        private const float BASE_SEP_SIDE_RADIUS = 2f;
        private const float BASE_SEP_FORWARD_RADIUS = 2f;
        private const float BASE_SEPARATION_WEIGHT = 0.8f;
        private const float BASE_ALIGNMENT_WEIGHT = 1.0f;
        private const float BASE_COHESION_WEIGHT = 1.2f;
        private const int BASE_MAX_NEIGH_FOR_COH = 8;

        private const float BASE_SIT_CHECK_INTERVAL = 10f;
        private const float BASE_SIT_PROBABILITY = 0.12f;
        private const float BASE_MIN_SIT_TIME = 20f;
        private const float BASE_MAX_SIT_TIME = 60f;
        private const float STAND_SLOW_TIME = 3f;

        private const float OBSTACLE_AVOID_FACTOR = 0.1f;

        private const float PARAM_VARIANCE = 0.25f;
        private const float WEIGHT_VARIANCE = 0.30f;
        private const float BASE_JITTER_STRENGTH = 0.25f;

        private const string IDLE_ANIM = "idle";
        private const string WALK_ANIM = "walk_forward";
        private const string TROT_ANIM = "trot_forward";
        private const string STAND2SIT_ANIM = "stand_to_sit";
        private const string SIT2STAND_ANIM = "sit_to_stand";

        [Header("Dog")]
        [SerializeField] private float dogRepulsionRadius = 20f;
        [SerializeField] private float dogRepulsionWeight = 4.0f;

        [Header("Fence Repulsion")]
        [SerializeField] private float fenceRepulsionRadius = 6f;
        [SerializeField] private float fenceRepulsionWeight = 6f;
        [SerializeField] private float fenceRepulsionSpeedBoost = 1.2f;

        [Header("Motion")]
        [SerializeField] private float groundPlaneY = 0f;
        [SerializeField] private float visualForwardOffsetDegrees = 0f;

        [Header("Fence Collision")]
        [SerializeField] private float bodyCollisionRadius = 0.35f;
        [SerializeField] private float fenceStopPadding = 0.08f;
        [SerializeField] private float fenceRepelStep = 0.35f;
        [SerializeField, Range(0f, 1f)] private float fenceSlideFactor = 0.7f;

        private float neighbourRadius;
        private float sepSideRadius;
        private float sepForwardRadius;
        private float separationWeight;
        private float alignmentWeight;
        private float cohesionWeight;
        private int maxNeighboursForFullCohesion;
        private float maxForce;

        private float sitCheckInterval;
        private float sitProbability;
        private float minSitTime;
        private float maxSitTime;

        private Vector3 _velocity;
        private Animator _anim;
        private Rigidbody _rb;
        private bool _isSitting;
        private float _standSlowTimer;
        private float _jitterStrength;
        private Transform _dogTransform;
        private bool _dogCacheValid;
        private float _dogCacheTimer;
        private const float DOG_CACHE_INTERVAL = 0.5f;

        private string _currentAnimState;
        private bool _idleAnimValid;
        private bool _walkAnimValid;
        private bool _trotAnimValid;
        private bool _stand2SitAnimValid;
        private bool _sit2StandAnimValid;

        private static readonly List<SheepController> _flock = new List<SheepController>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearStaticState()
        {
            _flock.Clear();
        }

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

            _velocity = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * AMBLER_SPEED;

            _anim = GetComponent<Animator>();
            _rb = GetComponent<Rigidbody>();
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            _anim.applyRootMotion = false;

            // Validate which animation states actually exist in the controller
            _idleAnimValid     = !string.IsNullOrEmpty(IDLE_ANIM)     && _anim.HasState(0, Animator.StringToHash(IDLE_ANIM));
            _walkAnimValid     = !string.IsNullOrEmpty(WALK_ANIM)     && _anim.HasState(0, Animator.StringToHash(WALK_ANIM));
            _trotAnimValid     = !string.IsNullOrEmpty(TROT_ANIM)     && _anim.HasState(0, Animator.StringToHash(TROT_ANIM));
            _stand2SitAnimValid = !string.IsNullOrEmpty(STAND2SIT_ANIM) && _anim.HasState(0, Animator.StringToHash(STAND2SIT_ANIM));
            _sit2StandAnimValid = !string.IsNullOrEmpty(SIT2STAND_ANIM) && _anim.HasState(0, Animator.StringToHash(SIT2STAND_ANIM));

            if (!_walkAnimValid && !_trotAnimValid)
            {
                Debug.LogWarning($"[SheepController] '{name}': Neither walk ('{WALK_ANIM}') nor trot ('{TROT_ANIM}') animation states found in Animator. Check state names match exactly.", this);
            }

            _flock.Add(this);
        }

        private void Start()
        {
            StartCoroutine(RestRoutine());
        }

        private void OnDestroy()
        {
            _flock.Remove(this);
        }

        private void Update()
        {
            // Refresh dog cache periodically rather than every frame
            _dogCacheTimer -= Time.deltaTime;
            if (_dogCacheTimer <= 0f)
            {
                _dogCacheTimer = DOG_CACHE_INTERVAL;
                GameObject dog = GameObject.FindGameObjectWithTag("Dog");
                _dogTransform = (dog != null && dog.activeInHierarchy) ? dog.transform : null;
                _dogCacheValid = _dogTransform != null;
            }

            // Emergency: cancel sitting immediately if dog appears
            if (_isSitting && _dogCacheValid)
            {
                _isSitting = false;
                PlayAnim(_sit2StandAnimValid ? SIT2STAND_ANIM : null);
            }

            if (_isSitting)
            {
                return;
            }

            if (_standSlowTimer > 0f)
            {
                _standSlowTimer -= Time.deltaTime;
            }

            Vector3 steering = ComputeBoidSteering();
            Vector3 jitter = Random.insideUnitSphere;
            jitter.y = 0f;
            steering += jitter * maxForce * _jitterStrength;

            int neighbourCount = CountNeighbours();
            float targetSpeed = (neighbourCount >= 2 && _standSlowTimer <= 0f) ? TROT_SPEED : AMBLER_SPEED;

            _velocity = Vector3.ClampMagnitude(_velocity + steering * Time.deltaTime, targetSpeed);
            _velocity.y = 0f;

            if (_velocity.sqrMagnitude > 0.0001f)
            {
                Vector3 move = _velocity * Time.deltaTime;
                Vector3 nextPos = GetFenceSafePosition(transform.position, move);
                nextPos.y = groundPlaneY;
                _rb.MovePosition(nextPos);

                Quaternion targetRot = Quaternion.LookRotation(_velocity.normalized, Vector3.up) * Quaternion.Euler(0f, visualForwardOffsetDegrees, 0f);
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRot, 5f * Time.deltaTime));
            }

            UpdateAnimation(targetSpeed);
        }

        private Vector3 GetFenceSafePosition(Vector3 currentPos, Vector3 move)
        {
            Vector3 planarMove = move;
            planarMove.y = 0f;

            float moveDistance = planarMove.magnitude;
            if (moveDistance < 0.0001f)
            {
                currentPos.y = groundPlaneY;
                return currentPos;
            }

            Vector3 direction = planarMove / moveDistance;
            float castDistance = moveDistance + Mathf.Max(0.01f, fenceStopPadding);

            if (Physics.SphereCast(currentPos + Vector3.up * 0.2f, Mathf.Max(0.01f, bodyCollisionRadius), direction, out RaycastHit hit, castDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null && hit.collider.CompareTag("Fence"))
                {
                    float stopDistance = Mathf.Max(0f, hit.distance - fenceStopPadding);
                    Vector3 approach = currentPos + direction * stopDistance;

                    Vector3 normal = hit.normal;
                    normal.y = 0f;
                    if (normal.sqrMagnitude < 0.0001f)
                    {
                        normal = (approach - hit.point);
                        normal.y = 0f;
                    }
                    if (normal.sqrMagnitude < 0.0001f)
                    {
                        normal = -direction;
                    }
                    normal.Normalize();

                    Vector3 slide = Vector3.ProjectOnPlane(planarMove, normal) * Mathf.Clamp01(fenceSlideFactor);
                    Vector3 repel = normal * Mathf.Max(0.01f, fenceRepelStep);

                    Vector3 redirected = approach + slide + repel;
                    redirected.y = groundPlaneY;

                    // Bias velocity away from fence so steering reacts as repulsion instead of repeated stop.
                    float currentSpeed = _velocity.magnitude;
                    Vector3 awayVelocity = normal * Mathf.Max(AMBLER_SPEED, currentSpeed * 0.8f);
                    _velocity = Vector3.Lerp(_velocity, awayVelocity, 0.45f);
                    _velocity.y = 0f;

                    return redirected;
                }
            }

            Vector3 next = currentPos + planarMove;
            next.y = groundPlaneY;
            return next;
        }

        private void UpdateAnimation(float targetSpeed)
        {
            float speed = _velocity.magnitude;

            if (_isSitting)
            {
                PlayAnim(_idleAnimValid ? IDLE_ANIM : null);
                return;
            }

            string desired;
            if (speed > (AMBLER_SPEED + 0.1f))
            {
                desired = _trotAnimValid ? TROT_ANIM : (_walkAnimValid ? WALK_ANIM : null);
            }
            else
            {
                desired = _walkAnimValid ? WALK_ANIM : (_trotAnimValid ? TROT_ANIM : null);
            }

            PlayAnim(desired);
            _anim.speed = Mathf.Clamp(speed / Mathf.Max(0.01f, targetSpeed), 0.85f, 1.2f);
        }

        // Only calls Animator.Play when the state actually changes to prevent per-frame restarts.
        private void PlayAnim(string stateName)
        {
            if (string.IsNullOrEmpty(stateName) || stateName == _currentAnimState) return;
            _currentAnimState = stateName;
            _anim.Play(stateName, 0);
        }

        private int CountNeighbours()
        {
            int count = 0;
            Vector3 pos = transform.position;

            for (int i = 0; i < _flock.Count; i++)
            {
                SheepController other = _flock[i];
                if (other == null || other == this)
                {
                    continue;
                }

                if ((other.transform.position - pos).sqrMagnitude < neighbourRadius * neighbourRadius)
                {
                    count++;
                }
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
            float maxFenceThreat = 0f;

            float fenceRadius = Mathf.Max(0.1f, fenceRepulsionRadius);
            Collider[] fences = Physics.OverlapSphere(pos, fenceRadius);
            for (int i = 0; i < fences.Length; i++)
            {
                Collider col = fences[i];
                if (col == null || !IsFenceCollider(col))
                {
                    continue;
                }

                Vector3 closest = col.ClosestPoint(pos);
                Vector3 toFence = pos - closest;
                float dist = toFence.magnitude;
                if (dist < 0.0001f)
                {
                    continue;
                }

                float strength = Mathf.Clamp01(1f - (dist / fenceRadius));
                strength *= strength;
                maxFenceThreat = Mathf.Max(maxFenceThreat, strength);
                fenceAvoid += toFence.normalized * strength * fenceRepulsionWeight;
            }

            if (maxFenceThreat > 0f)
            {
                float speedBoost = Mathf.Lerp(1f, Mathf.Max(1f, fenceRepulsionSpeedBoost), maxFenceThreat);
                _velocity = Vector3.ClampMagnitude(_velocity, AMBLER_SPEED * speedBoost);
            }

            float obstacleRadius = neighbourRadius * OBSTACLE_AVOID_FACTOR;

            for (int i = 0; i < _flock.Count; i++)
            {
                SheepController other = _flock[i];
                if (other == null || other == this)
                {
                    continue;
                }

                Vector3 toOther = other.transform.position - pos;
                float dist = toOther.magnitude;
                if (dist > neighbourRadius)
                {
                    continue;
                }

                if (dist < obstacleRadius && dist > 0.0001f)
                {
                    separation += (-toOther.normalized) * ((obstacleRadius - dist) / obstacleRadius);
                }

                neighbourCount++;
                alignment += other._velocity;
                cohesion += other.transform.position;

                Vector3 local = transform.InverseTransformDirection(toOther);
                float sx = local.x / sepSideRadius;
                float sz = local.z / sepForwardRadius;
                float inside = sx * sx + sz * sz;
                if (inside < 1f && dist > 0.0001f)
                {
                    float strength = 1f - inside;
                    separation += (-toOther.normalized) * strength;
                }
            }

            if (neighbourCount > 0)
            {
                alignment = (alignment / neighbourCount).normalized * TROT_SPEED - _velocity;
                Vector3 centre = cohesion / neighbourCount;
                Vector3 toCentre = centre - pos;
                float densityFactor = Mathf.Clamp01((float)neighbourCount / maxNeighboursForFullCohesion);
                cohesion = toCentre.normalized * TROT_SPEED - _velocity;
                cohesion *= (1f - densityFactor);
            }

            if (separation.sqrMagnitude > 0.0001f)
            {
                separation = separation.normalized * TROT_SPEED - _velocity;
            }

            Vector3 dogAvoid = ComputeDogAvoidance(pos);
            Vector3 steer =
                separation * separationWeight +
                alignment * alignmentWeight +
                cohesion * cohesionWeight +
                fenceAvoid +
                dogAvoid;

            return Vector3.ClampMagnitude(steer, maxForce);
        }

        private static bool IsFenceCollider(Collider col)
        {
            if (col == null)
            {
                return false;
            }

            if (col.CompareTag("Fence"))
            {
                return true;
            }

            Transform parent = col.transform.parent;
            while (parent != null)
            {
                if (parent.CompareTag("Fence"))
                {
                    return true;
                }
                parent = parent.parent;
            }

            return false;
        }

        private Vector3 ComputeDogAvoidance(Vector3 pos)
        {
            if (!_dogCacheValid || _dogTransform == null)
            {
                return Vector3.zero;
            }

            Vector3 toDog = pos - _dogTransform.position;
            float dist = toDog.magnitude;
            if (dist < 0.0001f || dist > dogRepulsionRadius)
            {
                return Vector3.zero;
            }

            float strength = 1f - (dist / dogRepulsionRadius);
            float speedBoost = Mathf.Lerp(1f, 1.25f, strength);
            _velocity = Vector3.ClampMagnitude(_velocity, AMBLER_SPEED * speedBoost);

            return toDog.normalized * strength * dogRepulsionWeight;
        }

        private IEnumerator RestRoutine()
        {
            while (true)
            {
                yield return new WaitForSeconds(sitCheckInterval);

                if (_dogCacheValid || _isSitting) continue;

                if (Random.value < sitProbability)
                {
                    StartCoroutine(SitCoroutine());
                }
            }
        }

        private IEnumerator SitCoroutine()
        {
            if (_isSitting || _dogCacheValid) yield break;

            _isSitting = true;
            PlayAnim(_stand2SitAnimValid ? STAND2SIT_ANIM : null);

            yield return new WaitForSeconds(1f);
            if (_dogCacheValid) { _isSitting = false; yield break; }

            float wait = Random.Range(minSitTime, maxSitTime);
            yield return new WaitForSeconds(wait);
            if (_dogCacheValid) { _isSitting = false; yield break; }

            PlayAnim(_sit2StandAnimValid ? SIT2STAND_ANIM : null);
            yield return new WaitForSeconds(1f);
            _isSitting = false;
            _standSlowTimer = STAND_SLOW_TIME;
        }
    }
}