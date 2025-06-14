using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Ursaanimation.CubicFarmAnimals
{
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(Rigidbody))]
    public class SheepController : MonoBehaviour
    {
        /* ───────── BASELINE TUNABLES ───────── */
        private const float AMBLER_SPEED = 3.0f;
        private const float TROT_SPEED   = 4.5f;
        private const float BASE_MAX_FORCE = 6f;

        private const float BASE_NEIGHBOUR_RADIUS   = 6f;
        private const float BASE_SEP_SIDE_RADIUS    = 2f;
        private const float BASE_SEP_FORWARD_RADIUS = 2f;
        private const float BASE_SEPARATION_WEIGHT  = 0.8f;
        private const float BASE_ALIGNMENT_WEIGHT   = 1.0f;
        private const float BASE_COHESION_WEIGHT    = 1.2f;
        private const int   BASE_MAX_NEIGH_FOR_COH  = 8;

        private const float BASE_SIT_CHECK_INTERVAL = 10f;
        private const float BASE_SIT_PROBABILITY    = 0.12f;
        private const float BASE_MIN_SIT_TIME       = 20f;
        private const float BASE_MAX_SIT_TIME       = 60f;
        private const float STAND_SLOW_TIME         = 3f;

        private const float OBSTACLE_AVOID_FACTOR   = 0.1f;
        private const float FENCE_AVOID_RADIUS      = 3f;
        private const float FENCE_AVOID_WEIGHT      = 3f;

        private const float PARAM_VARIANCE  = 0.25f;
        private const float WEIGHT_VARIANCE = 0.30f;
        private const float BASE_JITTER_STRENGTH = 0.25f;

        private const string IDLE_ANIM  = "idle";
        private const string WALK_ANIM  = "walk_forward";
        private const string TROT_ANIM  = "trot_forward";
        private const string STAND2SIT_ANIM = "stand_to_sit";
        private const string SIT2STAND_ANIM = "sit_to_stand";

        private float neighbourRadius, sepSideRadius, sepForwardRadius;
        private float separationWeight, alignmentWeight, cohesionWeight;
        private int   maxNeighboursForFullCohesion;
        private float maxForce;

        private float sitCheckInterval, sitProbability, minSitTime, maxSitTime;

        private Vector3 _velocity;
        private Animator _anim;
        private Rigidbody _rb;

        private bool _isSitting;
        private float _standSlowTimer;
        private float _jitterStrength;

        private static readonly List<SheepController> _flock = new();

        private void Awake()
        {
            float V(float v) => v * Random.Range(1f - PARAM_VARIANCE, 1f + PARAM_VARIANCE);
            float W(float w) => w * Random.Range(1f - WEIGHT_VARIANCE, 1f + WEIGHT_VARIANCE);

            neighbourRadius  = V(BASE_NEIGHBOUR_RADIUS);
            sepSideRadius    = Mathf.Max(V(BASE_SEP_SIDE_RADIUS), 0.3f * neighbourRadius);
            sepForwardRadius = Mathf.Max(V(BASE_SEP_FORWARD_RADIUS), 0.3f * neighbourRadius);

            separationWeight = W(BASE_SEPARATION_WEIGHT);
            alignmentWeight  = W(BASE_ALIGNMENT_WEIGHT);
            cohesionWeight   = W(BASE_COHESION_WEIGHT);

            maxNeighboursForFullCohesion = Mathf.Max(1, Mathf.RoundToInt(W(BASE_MAX_NEIGH_FOR_COH)));
            maxForce = V(BASE_MAX_FORCE);

            sitCheckInterval = V(BASE_SIT_CHECK_INTERVAL);
            sitProbability   = Mathf.Clamp01(W(BASE_SIT_PROBABILITY));
            minSitTime       = V(BASE_MIN_SIT_TIME);
            maxSitTime       = V(BASE_MAX_SIT_TIME);

            _jitterStrength  = W(BASE_JITTER_STRENGTH);

            _velocity = Quaternion.Euler(0, Random.Range(0,360f),0) * Vector3.forward * AMBLER_SPEED;

            _anim = GetComponent<Animator>();
            _rb   = GetComponent<Rigidbody>();
            _rb.isKinematic = true;

            _flock.Add(this);
        }

        private void Start()
        {
            StartCoroutine(RestRoutine());
        }

        private void OnDestroy() => _flock.Remove(this);

        private void Update()
        {
            if (_isSitting) return;

            Vector3 steering = ComputeBoidSteering();
            Vector3 jitter = Random.insideUnitSphere; jitter.y = 0f;
            steering += jitter * maxForce * _jitterStrength;

            int neighbourCount = CountNeighbours();
            float targetSpeed = (neighbourCount >= 2 && _standSlowTimer <= 0f) ? TROT_SPEED : AMBLER_SPEED;
            _velocity = Vector3.ClampMagnitude(_velocity + steering * Time.deltaTime, targetSpeed);

            if (_velocity.sqrMagnitude > 0.0001f)
            {
                transform.position += _velocity * Time.deltaTime;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(_velocity, Vector3.up), 5f * Time.deltaTime);
            }

            string moveAnim = (_velocity.magnitude > (AMBLER_SPEED + 0.1f)) ? TROT_ANIM : WALK_ANIM;
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
            Vector3 alignment  = Vector3.zero;
            Vector3 cohesion   = Vector3.zero;
            Vector3 fenceAvoid = Vector3.zero;
            int neighbourCount = 0;

            // Stronger Fence avoidance (fear)
            Collider[] fences = Physics.OverlapSphere(pos, FENCE_AVOID_RADIUS);
            foreach (var col in fences)
            {
                if (!col.CompareTag("Fence")) continue;
                Vector3 closest = col.ClosestPoint(pos);
                Vector3 toFence = pos - closest; // notice: flipped direction -> away from fence
                float dist = toFence.magnitude;
                if (dist < 0.0001f) continue;

                // Make the avoidance much stronger and nonlinear
                float strength = Mathf.Clamp01((FENCE_AVOID_RADIUS - dist) / FENCE_AVOID_RADIUS);
                strength = strength * strength; // quadratic for stronger near-field effect
                fenceAvoid += toFence.normalized * strength * FENCE_AVOID_WEIGHT;
            }

            float obstacleRadius = neighbourRadius * OBSTACLE_AVOID_FACTOR;

            foreach (var other in _flock)
            {
                if (other == this) continue;

                Vector3 toOther = other.transform.position - pos;
                float dist = toOther.magnitude;
                if (dist > neighbourRadius) continue;

                if (dist < obstacleRadius && dist > 0.0001f)
                    separation += (-toOther.normalized) * ((obstacleRadius - dist) / obstacleRadius);

                neighbourCount++;
                alignment += other._velocity;
                cohesion  += other.transform.position;

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
                Vector3 centre = (cohesion / neighbourCount);
                Vector3 toCentre = centre - pos;
                float densityFactor = Mathf.Clamp01((float)neighbourCount / maxNeighboursForFullCohesion);
                cohesion = toCentre.normalized * TROT_SPEED - _velocity;
                cohesion *= (1f - densityFactor);
            }

            separation = separation.normalized * TROT_SPEED - _velocity;

            Vector3 steer =
                separation * separationWeight +
                alignment  * alignmentWeight  +
                cohesion   * cohesionWeight   +
                fenceAvoid;  // already scaled above

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
            if (!string.IsNullOrEmpty(STAND2SIT_ANIM)) _anim.Play(STAND2SIT_ANIM, 0);
            yield return new WaitForSeconds(1f);

            float wait = Random.Range(minSitTime, maxSitTime);
            yield return new WaitForSeconds(wait);

            if (!string.IsNullOrEmpty(SIT2STAND_ANIM)) _anim.Play(SIT2STAND_ANIM, 0);
            yield return new WaitForSeconds(1f);
            _isSitting = false;
            _standSlowTimer = STAND_SLOW_TIME;
        }
    }
}

