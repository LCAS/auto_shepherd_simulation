using System.Collections.Generic;
using UnityEngine;

namespace Controller
{
    public class Control : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private DogController dogController;
        [SerializeField] private Transform goal;

        [Header("Debug Visuals")]
        [SerializeField] private bool enableDebugVisuals = true;
        [SerializeField] private GameObject boundaryPrefab;
        [SerializeField] private GameObject candidatePositionPrefab;
        [SerializeField] private GameObject chosenPositionPrefab;
        [SerializeField] private Transform debugVisualsRoot;
        [SerializeField] private int boundaryRingPointCount = 24;
        [SerializeField] private float debugVerticalOffset = 0.05f;

        [Header("Debug Logs")]
        [SerializeField] private bool enableDebugLogs = true;
        [SerializeField] private float debugLogInterval = 1f;

        [Header("Timing")]
        [SerializeField] private float controlInterval = 0.1f;

        [Header("Herding")]
        [SerializeField] private float dogRadius = 1.4f;
        [SerializeField] private int candidateCount = 15;
        [SerializeField] private float arcWidthDegrees = 90f;
        [SerializeField] private float goalArrivalDistance = 12f;
        [SerializeField] private float goalApproachDogRadiusMultiplier = 1.85f;
        [SerializeField] private float clusterJoinDistance = 6f;
        [SerializeField] private int minClusterSize = 1;
        [SerializeField] private float boundaryPadding = 1f;

        [Header("Cluster Switch Transition")]
        [SerializeField] private float clusterSwitchDetectionDistance = 5f;
        [SerializeField] private float retreatDistance = 8f;
        [SerializeField] private float arcRadius = 10f;
        [SerializeField] private int arcSteps = 6;
        [SerializeField] private float transitionWaypointReachedRadius = 1.5f;
        [SerializeField] private string sheepTag = "Sheep";
        [SerializeField] private string dogTag = "Dog";
        [SerializeField] private string goalTag = "Goal";

        private enum TransitionPhase { Idle, Retreating, Arcing }

        private float m_Timer;
        private Bounds m_FieldBounds;
        private bool m_HasFieldBounds;
        private readonly List<Transform> m_BoundaryVisuals = new List<Transform>();
        private readonly List<Transform> m_CandidateVisuals = new List<Transform>();
        private Transform m_ChosenVisual;
        private float m_NextDebugLogTime;
        private bool m_LoggedMissingDogController;

        // Cluster switch transition
        private TransitionPhase m_TransitionPhase = TransitionPhase.Idle;
        private Vector3 m_LastClusterCentroid = Vector3.positiveInfinity;
        private Vector3 m_TransitionTarget;
        private readonly Queue<Vector3> m_ArcWaypoints = new Queue<Vector3>();

        private void Start()
        {
            RefreshFieldBounds();

            if (dogController == null)
            {
                GameObject dog = GameObject.FindGameObjectWithTag(dogTag);
                if (dog != null)
                {
                    dogController = dog.GetComponent<DogController>();
                }
            }

            if (enableDebugLogs)
            {
                if (dogController == null)
                {
                    Debug.LogWarning($"[Control] DogController not found. Expected dog with tag '{dogTag}'.", this);
                }
                else
                {
                    Debug.Log($"[Control] DogController found on '{dogController.name}'.", this);
                }
            }

            if (goal == null)
            {
                Goal goalComponent = FindFirstObjectByType<Goal>();
                if (goalComponent != null)
                {
                    goal = goalComponent.transform;
                }
            }

            if (goal == null)
            {
                GameObject goalObject = GameObject.FindGameObjectWithTag(goalTag);
                if (goalObject != null)
                {
                    goal = goalObject.transform;
                }
            }

            if (goal == null)
            {
                GameObject goalObject = GameObject.Find("Goal");
                if (goalObject != null)
                {
                    goal = goalObject.transform;
                }
            }

            if (enableDebugLogs)
            {
                if (goal == null)
                {
                    Debug.LogWarning($"[Control] Goal not found. Searched Goal component/tag/name using '{goalTag}'.", this);
                }
                else
                {
                    Debug.Log($"[Control] Goal set to '{goal.name}' at {goal.position}.", this);
                }
            }

            if (enableDebugVisuals)
            {
                if (debugVisualsRoot == null)
                {
                    GameObject debugRootObject = GameObject.Find("DEBUG_VISUALS");
                    if (debugRootObject != null)
                    {
                        debugVisualsRoot = debugRootObject.transform;
                    }
                    else
                    {
                        GameObject createdRoot = new GameObject("DEBUG_VISUALS");
                        debugVisualsRoot = createdRoot.transform;
                    }
                }

                if (enableDebugLogs)
                {
                    Debug.Log($"[Control] Debug visuals root: '{debugVisualsRoot.name}'. boundaryPrefab={(boundaryPrefab != null)}, candidatePrefab={(candidatePositionPrefab != null)}, chosenPrefab={(chosenPositionPrefab != null)}", this);
                }

                EnsureVisualPool(m_BoundaryVisuals, Mathf.Max(3, boundaryRingPointCount), boundaryPrefab, "Boundary");
                EnsureVisualPool(m_CandidateVisuals, Mathf.Max(2, candidateCount), candidatePositionPrefab, "Candidate Position");
                EnsureChosenVisual();
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[Control] Started. interval={controlInterval:F2}, candidateCount={candidateCount}, arcWidth={arcWidthDegrees:F1}", this);
            }
        }

        private void Update()
        {
            m_Timer += Time.deltaTime;
            if (m_Timer < controlInterval)
            {
                return;
            }

            m_Timer = 0f;
            StepControl();
        }

        private void StepControl()
        {
            if (dogController == null)
            {
                if (enableDebugLogs && !m_LoggedMissingDogController)
                {
                    m_LoggedMissingDogController = true;
                    Debug.LogWarning("[Control] Step skipped: dogController is null.", this);
                }
                return;
            }

            List<Vector3> sheepPositions = GatherSheepPositions();
            if (sheepPositions.Count == 0)
            {
                if (enableDebugLogs && Time.time >= m_NextDebugLogTime)
                {
                    Debug.LogWarning($"[Control] Step skipped: no sheep found with tag '{sheepTag}'.", this);
                    m_NextDebugLogTime = Time.time + Mathf.Max(0.1f, debugLogInterval);
                }
                return;
            }

            if (!m_HasFieldBounds)
            {
                RefreshFieldBounds();
            }

            Vector3 dogPosition = dogController.transform.position;
            Vector3 goalPosition = GetGoalPosition();
            List<List<Vector3>> clusters = BuildClusters(sheepPositions);
            int clusterCount = clusters.Count;
            List<Vector3> prioritizedCluster = SelectPriorityCluster(clusters, sheepPositions, goalPosition);
            Vector3 newClusterCentroid = ComputeCentroid(prioritizedCluster);

            // Detect cluster switch and begin transition if needed
            bool clusterSwitched = m_TransitionPhase == TransitionPhase.Idle &&
                !float.IsInfinity(m_LastClusterCentroid.x) &&
                Vector3.Distance(newClusterCentroid, m_LastClusterCentroid) > clusterSwitchDetectionDistance;

            Vector3 target;
            Vector3 centroid;
            float flockRadius;
            float desiredDogDistance;
            List<Vector3> candidatePositions;

            if (clusterSwitched)
            {
                BeginClusterTransition(dogPosition, m_LastClusterCentroid, newClusterCentroid, goalPosition);
            }

            if (m_TransitionPhase != TransitionPhase.Idle)
            {
                target = TickTransition(dogPosition, newClusterCentroid, goalPosition, out centroid, out flockRadius, out desiredDogDistance, out candidatePositions);
            }
            else
            {
                m_LastClusterCentroid = newClusterCentroid;
                target = FindBestDogPosition(prioritizedCluster, dogPosition, goalPosition, out centroid, out flockRadius, out desiredDogDistance, out candidatePositions);
            }

            if (enableDebugLogs && Time.time >= m_NextDebugLogTime)
            {
                Debug.Log($"[Control] Step: sheep={sheepPositions.Count}, clusters={clusterCount}, selectedClusterSize={prioritizedCluster.Count}, centroid={centroid}, flockRadius={flockRadius:F2}, phase={m_TransitionPhase}, target={target}", this);
                m_NextDebugLogTime = Time.time + Mathf.Max(0.1f, debugLogInterval);
            }

            if (enableDebugVisuals)
            {
                RenderBoundaryRings(clusters);
                RenderCandidateVisuals(candidatePositions);
                RenderChosenVisual(target);
            }

            dogController.SetTarget(target);
        }

        private void BeginClusterTransition(Vector3 dogPos, Vector3 oldCentroid, Vector3 newCentroid, Vector3 goalPosition)
        {
            m_ArcWaypoints.Clear();

            // Retreat point: behind the old cluster away from the new cluster
            Vector3 awayFromNew = (oldCentroid - newCentroid);
            awayFromNew.y = 0f;
            if (awayFromNew.sqrMagnitude < 0.0001f) awayFromNew = Vector3.forward;
            awayFromNew.Normalize();

            Vector3 retreatPoint = ClampToField(oldCentroid + awayFromNew * retreatDistance);
            retreatPoint.y = 0f;
            m_TransitionTarget = retreatPoint;
            m_TransitionPhase = TransitionPhase.Retreating;

            // Build arc waypoints from retreat point around to behind the new cluster
            Vector3 toNewFromDog = (newCentroid - retreatPoint);
            toNewFromDog.y = 0f;
            if (toNewFromDog.sqrMagnitude < 0.0001f) toNewFromDog = Vector3.forward;
            toNewFromDog.Normalize();

            Vector3 goalDir = (goalPosition - newCentroid);
            goalDir.y = 0f;
            if (goalDir.sqrMagnitude < 0.0001f) goalDir = -toNewFromDog;
            goalDir.Normalize();

            float startAngle = Mathf.Atan2((retreatPoint - newCentroid).z, (retreatPoint - newCentroid).x);
            float endAngle = Mathf.Atan2(-goalDir.z, -goalDir.x);

            // Choose shortest arc direction
            float delta = Mathf.DeltaAngle(startAngle * Mathf.Rad2Deg, endAngle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
            int steps = Mathf.Max(2, arcSteps);

            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float angle = startAngle + delta * t;
                Vector3 arcPoint = newCentroid + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * arcRadius;
                arcPoint.y = 0f;
                m_ArcWaypoints.Enqueue(ClampToField(arcPoint));
            }

            if (enableDebugLogs)
            {
                Debug.Log($"[Control] Cluster switch detected. Retreating to {retreatPoint}, then arcing through {m_ArcWaypoints.Count} waypoints to new cluster at {newCentroid}.", this);
            }
        }

        private Vector3 TickTransition(Vector3 dogPos, Vector3 newCentroid, Vector3 goalPosition, out Vector3 centroid, out float flockRadius, out float desiredDogDistance, out List<Vector3> candidatePositions)
        {
            centroid = newCentroid;
            flockRadius = arcRadius;
            desiredDogDistance = dogRadius;
            candidatePositions = new List<Vector3>();

            float reached = Mathf.Max(0.1f, transitionWaypointReachedRadius);

            if (m_TransitionPhase == TransitionPhase.Retreating)
            {
                if (Vector3.Distance(dogPos, m_TransitionTarget) <= reached)
                {
                    if (m_ArcWaypoints.Count > 0)
                    {
                        m_TransitionTarget = m_ArcWaypoints.Dequeue();
                        m_TransitionPhase = TransitionPhase.Arcing;
                    }
                    else
                    {
                        m_TransitionPhase = TransitionPhase.Idle;
                        m_LastClusterCentroid = newCentroid;
                    }
                }
            }
            else if (m_TransitionPhase == TransitionPhase.Arcing)
            {
                if (Vector3.Distance(dogPos, m_TransitionTarget) <= reached)
                {
                    if (m_ArcWaypoints.Count > 0)
                    {
                        m_TransitionTarget = m_ArcWaypoints.Dequeue();
                    }
                    else
                    {
                        m_TransitionPhase = TransitionPhase.Idle;
                        m_LastClusterCentroid = newCentroid;
                    }
                }
            }

            return m_TransitionTarget;
        }

        private List<Vector3> SelectPriorityCluster(List<List<Vector3>> clusters, List<Vector3> fallbackSheepPositions, Vector3 goalPosition)
        {
            if (clusters.Count == 0)
            {
                return fallbackSheepPositions;
            }

            List<Vector3> bestCluster = clusters[0];
            float bestScore = float.NegativeInfinity;

            for (int i = 0; i < clusters.Count; i++)
            {
                List<Vector3> cluster = clusters[i];
                if (cluster == null || cluster.Count < Mathf.Max(1, minClusterSize))
                {
                    continue;
                }

                Vector3 centroid = ComputeCentroid(cluster);
                float distToGoal = Vector3.Distance(centroid, goalPosition);

                // Furthest from goal is highest priority; slight size bonus to avoid chasing tiny outliers forever.
                float score = distToGoal + (cluster.Count * 0.05f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestCluster = cluster;
                }
            }

            return bestCluster;
        }

        private List<List<Vector3>> BuildClusters(List<Vector3> sheepPositions)
        {
            List<List<Vector3>> clusters = new List<List<Vector3>>();
            int count = sheepPositions.Count;
            if (count == 0)
            {
                return clusters;
            }

            bool[] visited = new bool[count];
            float joinDistance = Mathf.Max(0.1f, clusterJoinDistance);
            float joinDistanceSqr = joinDistance * joinDistance;

            Queue<int> queue = new Queue<int>();

            for (int i = 0; i < count; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                visited[i] = true;
                queue.Enqueue(i);

                List<Vector3> cluster = new List<Vector3>();

                while (queue.Count > 0)
                {
                    int index = queue.Dequeue();
                    Vector3 current = sheepPositions[index];
                    cluster.Add(current);

                    for (int j = 0; j < count; j++)
                    {
                        if (visited[j])
                        {
                            continue;
                        }

                        Vector3 delta = sheepPositions[j] - current;
                        delta.y = 0f;
                        if (delta.sqrMagnitude <= joinDistanceSqr)
                        {
                            visited[j] = true;
                            queue.Enqueue(j);
                        }
                    }
                }

                if (cluster.Count > 0)
                {
                    clusters.Add(cluster);
                }
            }

            return clusters;
        }

        private Vector3 ComputeCentroid(List<Vector3> points)
        {
            if (points == null || points.Count == 0)
            {
                return Vector3.zero;
            }

            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
            {
                centroid += points[i];
            }

            return centroid / points.Count;
        }

        private List<Vector3> GatherSheepPositions()
        {
            GameObject[] sheepObjects = GameObject.FindGameObjectsWithTag(sheepTag);
            List<Vector3> positions = new List<Vector3>(sheepObjects.Length);

            for (int i = 0; i < sheepObjects.Length; i++)
            {
                positions.Add(sheepObjects[i].transform.position);
            }

            return positions;
        }

        private Vector3 GetGoalPosition()
        {
            if (goal != null)
            {
                return goal.position;
            }

            return Vector3.zero;
        }

        private void RefreshFieldBounds()
        {
            GameObject[] fences = GameObject.FindGameObjectsWithTag("Fence");
            if (fences == null || fences.Length == 0)
            {
                m_HasFieldBounds = false;

                if (enableDebugLogs)
                {
                    Debug.LogWarning("[Control] No fence objects found with tag 'Fence'. Field bounds disabled.", this);
                }
                return;
            }

            Vector3 first = fences[0].transform.position;
            Vector3 min = first;
            Vector3 max = first;

            for (int i = 1; i < fences.Length; i++)
            {
                Vector3 p = fences[i].transform.position;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            min.x -= boundaryPadding;
            min.z -= boundaryPadding;
            max.x += boundaryPadding;
            max.z += boundaryPadding;

            m_FieldBounds = new Bounds((min + max) * 0.5f, new Vector3(max.x - min.x, 1000f, max.z - min.z));
            m_HasFieldBounds = true;

            if (enableDebugLogs)
            {
                Debug.Log($"[Control] Field bounds refreshed. min={m_FieldBounds.min}, max={m_FieldBounds.max}", this);
            }
        }

        private Vector3 FindBestDogPosition(List<Vector3> sheepPositions, Vector3 dogPosition, Vector3 goalPosition, out Vector3 centroid, out float flockRadius, out float desiredDogDistance, out List<Vector3> candidatePositions)
        {
            centroid = Vector3.zero;
            for (int i = 0; i < sheepPositions.Count; i++)
            {
                centroid += sheepPositions[i];
            }
            centroid /= sheepPositions.Count;

            flockRadius = GetFlockRadius(sheepPositions, centroid);
            float distanceToFlock = Vector3.Distance(dogPosition, centroid);
            desiredDogDistance = GetDesiredDogDistance(Vector3.Distance(centroid, goalPosition));
            candidatePositions = new List<Vector3>();

            Vector3 flockToGoal = goalPosition - centroid;
            flockToGoal.y = 0f;
            if (flockToGoal.sqrMagnitude < 0.0001f)
            {
                flockToGoal = Vector3.forward;
            }
            flockToGoal.Normalize();

            if (distanceToFlock > flockRadius + desiredDogDistance)
            {
                Vector3 approach = (centroid - dogPosition).normalized;
                return ClampToField(dogPosition + approach * desiredDogDistance);
            }

            if (distanceToFlock < Mathf.Abs(desiredDogDistance - flockRadius))
            {
                Vector3 fallback = centroid - flockToGoal * Mathf.Max(5f, flockRadius + desiredDogDistance * 2f);
                return ClampToField(fallback);
            }

            return ChooseHerderPosition(centroid, flockRadius, desiredDogDistance, flockToGoal, dogPosition, goalPosition, candidatePositions);
        }

        private float GetDesiredDogDistance(float distanceToGoal)
        {
            float arrivalFactor = 1f - Mathf.Clamp01(distanceToGoal / Mathf.Max(0.01f, goalArrivalDistance));
            return Mathf.Lerp(dogRadius, dogRadius * goalApproachDogRadiusMultiplier, arrivalFactor);
        }

        private float GetFlockRadius(List<Vector3> sheepPositions, Vector3 centroid)
        {
            float radius = 1f;
            for (int i = 0; i < sheepPositions.Count; i++)
            {
                float distance = Vector3.Distance(sheepPositions[i], centroid);
                if (distance > radius)
                {
                    radius = distance;
                }
            }

            return radius;
        }

        private Vector3 ChooseHerderPosition(Vector3 centroid, float flockRadius, float desiredDogDistance, Vector3 flockToGoal, Vector3 dogPosition, Vector3 goalPosition, List<Vector3> candidatePositions)
        {
            float startAngle = Mathf.Atan2(-flockToGoal.z, -flockToGoal.x);
            float arcWidth = Mathf.Deg2Rad * arcWidthDegrees;
            float halfArc = arcWidth * 0.5f;

            Vector3 bestPoint = dogPosition;
            float bestCost = float.PositiveInfinity;

            int samples = Mathf.Max(2, candidateCount);
            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / (samples - 1);
                float angle = startAngle - halfArc + t * arcWidth;

                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 candidate = centroid + direction * (flockRadius + desiredDogDistance);

                if (!IsWithinField(candidate))
                {
                    continue;
                }

                candidatePositions.Add(candidate);

                Vector3 candidateDir = candidate - centroid;
                candidateDir.y = 0f;
                if (candidateDir.sqrMagnitude < 0.0001f)
                {
                    continue;
                }
                candidateDir.Normalize();

                float angleCost = Vector3.Angle(candidateDir, -flockToGoal);
                float dogDistancePenalty = Vector3.Distance(candidate, dogPosition) * 0.02f;
                float goalDistancePenalty = Vector3.Distance(candidate, goalPosition) * 0.02f;
                float cost = angleCost + dogDistancePenalty + goalDistancePenalty;

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestPoint = candidate;
                }
            }

            return ClampToField(bestPoint);
        }

        private bool IsWithinField(Vector3 point)
        {
            if (!m_HasFieldBounds)
            {
                return true;
            }

            return m_FieldBounds.Contains(point);
        }

        private Vector3 ClampToField(Vector3 point)
        {
            if (!m_HasFieldBounds)
            {
                return point;
            }

            point.x = Mathf.Clamp(point.x, m_FieldBounds.min.x, m_FieldBounds.max.x);
            point.z = Mathf.Clamp(point.z, m_FieldBounds.min.z, m_FieldBounds.max.z);
            return point;
        }

        private void EnsureVisualPool(List<Transform> pool, int count, GameObject prefab, string baseName)
        {
            if (prefab == null || debugVisualsRoot == null)
            {
                if (enableDebugLogs && Time.time >= m_NextDebugLogTime)
                {
                    Debug.LogWarning($"[Control] Visual pool '{baseName}' not created. prefab={(prefab != null)}, root={(debugVisualsRoot != null)}", this);
                    m_NextDebugLogTime = Time.time + Mathf.Max(0.1f, debugLogInterval);
                }
                return;
            }

            while (pool.Count < count)
            {
                GameObject instance = Instantiate(prefab, debugVisualsRoot);
                instance.name = $"{baseName}_{pool.Count:D2}";
                pool.Add(instance.transform);

                if (enableDebugLogs)
                {
                    Debug.Log($"[Control] Spawned debug visual '{instance.name}'.", this);
                }
            }

            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] == null)
                {
                    GameObject instance = Instantiate(prefab, debugVisualsRoot);
                    instance.name = $"{baseName}_{i:D2}";
                    pool[i] = instance.transform;

                    if (enableDebugLogs)
                    {
                        Debug.Log($"[Control] Recreated missing debug visual '{instance.name}'.", this);
                    }
                }
            }
        }

        private void EnsureChosenVisual()
        {
            if (chosenPositionPrefab == null || debugVisualsRoot == null)
            {
                if (enableDebugLogs && Time.time >= m_NextDebugLogTime)
                {
                    Debug.LogWarning($"[Control] Chosen visual not created. prefab={(chosenPositionPrefab != null)}, root={(debugVisualsRoot != null)}", this);
                    m_NextDebugLogTime = Time.time + Mathf.Max(0.1f, debugLogInterval);
                }
                return;
            }

            if (m_ChosenVisual == null)
            {
                GameObject chosen = Instantiate(chosenPositionPrefab, debugVisualsRoot);
                chosen.name = "ChosenPosition";
                m_ChosenVisual = chosen.transform;

                if (enableDebugLogs)
                {
                    Debug.Log("[Control] Spawned debug visual 'ChosenPosition'.", this);
                }
            }
        }

        private void RenderBoundaryRings(List<List<Vector3>> clusters)
        {
            int pointCount = Mathf.Max(3, boundaryRingPointCount);
            int ringCount = clusters != null ? clusters.Count : 0;
            int neededMarkers = Mathf.Max(pointCount, ringCount * pointCount);
            EnsureVisualPool(m_BoundaryVisuals, neededMarkers, boundaryPrefab, "Boundary");

            int markerIndex = 0;
            for (int clusterIndex = 0; clusterIndex < ringCount; clusterIndex++)
            {
                List<Vector3> cluster = clusters[clusterIndex];
                if (cluster == null || cluster.Count == 0)
                {
                    continue;
                }

                Vector3 centroid = ComputeCentroid(cluster);
                float radius = GetFlockRadius(cluster, centroid);

                for (int i = 0; i < pointCount && markerIndex < m_BoundaryVisuals.Count; i++, markerIndex++)
                {
                    Transform marker = m_BoundaryVisuals[markerIndex];
                    if (marker == null)
                    {
                        continue;
                    }

                    float t = (float)i / pointCount;
                    float angle = t * Mathf.PI * 2f;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    Vector3 markerPosition = centroid + offset;
                    markerPosition.y = 0f;
                    marker.position = markerPosition;
                    marker.gameObject.SetActive(true);
                }
            }

            for (int i = markerIndex; i < m_BoundaryVisuals.Count; i++)
            {
                Transform marker = m_BoundaryVisuals[i];
                if (marker == null)
                {
                    continue;
                }
                marker.gameObject.SetActive(false);
            }
        }

        private void RenderCandidateVisuals(List<Vector3> candidatePositions)
        {
            int needed = candidatePositions != null ? candidatePositions.Count : 0;
            EnsureVisualPool(m_CandidateVisuals, Mathf.Max(2, candidateCount), candidatePositionPrefab, "Candidate Position");

            for (int i = 0; i < m_CandidateVisuals.Count; i++)
            {
                Transform marker = m_CandidateVisuals[i];
                if (marker == null)
                {
                    continue;
                }

                if (i < needed)
                {
                    Vector3 markerPosition = candidatePositions[i];
                    markerPosition.y = 0f;
                    marker.position = markerPosition;
                    marker.gameObject.SetActive(true);
                }
                else
                {
                    marker.gameObject.SetActive(false);
                }
            }
        }

        private void RenderChosenVisual(Vector3 chosenPosition)
        {
            EnsureChosenVisual();
            if (m_ChosenVisual == null)
            {
                return;
            }

            Vector3 markerPosition = chosenPosition;
            markerPosition.y = 0f;
            m_ChosenVisual.position = markerPosition;
            m_ChosenVisual.gameObject.SetActive(true);
        }
    }
}