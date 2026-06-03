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
        [SerializeField] private bool projectDebugVisualsToGround = true;
        [SerializeField] private LayerMask debugGroundLayerMask = ~0;
        [SerializeField] private float debugGroundProbeStartHeight = 200f;
        [SerializeField] private float debugGroundProbeDistance = 500f;

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
        [SerializeField] private float clusterJoinDistance = 4f;
        [SerializeField] private int clusterMinCorePoints = 3;
        [SerializeField] private int minClusterSize = 1;
        [SerializeField] private float boundaryPadding = 1f;
        [SerializeField] private float dogBoundaryInset = 1.2f;
        [SerializeField] private float dogFenceProbeHeight = 0.6f;
        [SerializeField] private float fencePolygonErosion = 0.5f;

        [Header("Robot Stop Diagnostics")]
        [SerializeField] private float stoppedMoveDistanceThreshold = 0.03f;
        [SerializeField] private float stoppedTargetDistanceThreshold = 1.5f;
        [SerializeField] private float stoppedDurationBeforeLog = 1f;

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

        private struct TargetSafetyInfo
        {
            public bool hadFieldBounds;
            public bool hadFencePolygon;
            public bool rawInsideErodedPolygon;
            public bool clampedInsideErodedPolygon;
            public bool adjustedForPolygon;
            public bool fenceBlockedPath;
            public float distanceToFenceEdge;
            public string correctionReason;
        }

        private float m_Timer;
        private Bounds m_FieldBounds;
        private bool m_HasFieldBounds;
        private readonly List<Vector2> m_FieldPolygon = new List<Vector2>();
        private bool m_HasFieldPolygon;
        private readonly List<Transform> m_BoundaryVisuals = new List<Transform>();
        private readonly List<Transform> m_CandidateVisuals = new List<Transform>();
        private Transform m_ChosenVisual;
        private float m_NextDebugLogTime;
        private bool m_LoggedMissingDogController;
        private bool m_LoggedMissingFences;
        private Vector3 m_LastDogPosition;
        private bool m_HasLastDogPosition;
        private float m_RobotStoppedTimer;
        private float m_LastDiagnosticTime;

        // Cluster switch transition
        private TransitionPhase m_TransitionPhase = TransitionPhase.Idle;
        private Vector3 m_LastClusterCentroid = Vector3.positiveInfinity;
        private Vector3 m_TransitionTarget;
        private readonly Queue<Vector3> m_ArcWaypoints = new Queue<Vector3>();

        private bool TryGetGroundProjectedPoint(Vector3 worldPoint, out Vector3 projected)
        {
            projected = worldPoint;

            if (!projectDebugVisualsToGround)
            {
                projected.y = worldPoint.y + debugVerticalOffset;
                return true;
            }

            float startHeight = Mathf.Max(1f, debugGroundProbeStartHeight);
            float maxDistance = Mathf.Max(startHeight + 1f, debugGroundProbeDistance);
            Vector3 rayOrigin = worldPoint + Vector3.up * startHeight;
            Ray ray = new Ray(rayOrigin, Vector3.down);

            if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, debugGroundLayerMask, QueryTriggerInteraction.Ignore))
            {
                projected = hit.point + Vector3.up * debugVerticalOffset;
                return true;
            }

            projected.y = worldPoint.y + debugVerticalOffset;
            return false;
        }

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

            if (!m_HasFieldBounds || !m_HasFieldPolygon)
            {
                RefreshFieldBounds();
            }

            Vector3 dogPosition = dogController.transform.position;
            Vector3 goalPosition = GetGoalPosition();
            List<List<Vector3>> clusters = BuildClusters(sheepPositions);
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

            TargetSafetyInfo safetyInfo;
            Vector3 safeTarget = MakeReachableDogTarget(dogPosition, target, out safetyInfo);

            if (enableDebugLogs && Time.time >= m_NextDebugLogTime)
            {
                LogRobotTargetDiagnostic(dogPosition, target, safeTarget, safetyInfo);
                m_NextDebugLogTime = Time.time + Mathf.Max(0.1f, debugLogInterval);
            }

            if (enableDebugVisuals)
            {
                RenderBoundaryRings(clusters);
                RenderCandidateVisuals(candidatePositions);
                RenderChosenVisual(safeTarget);
            }

            dogController.SetTarget(safeTarget);
        }

        private void BeginClusterTransition(Vector3 dogPos, Vector3 oldCentroid, Vector3 newCentroid, Vector3 goalPosition)
        {
            m_ArcWaypoints.Clear();

            // Retreat point: behind the old cluster away from the new cluster
            Vector3 awayFromNew = (oldCentroid - newCentroid);
            awayFromNew.y = 0f;
            if (awayFromNew.sqrMagnitude < 0.0001f) awayFromNew = Vector3.forward;
            awayFromNew.Normalize();

            Vector3 retreatPoint = MakeReachableDogTarget(dogPos, oldCentroid + awayFromNew * retreatDistance);
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
            Vector3 waypointOrigin = retreatPoint;

            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                float angle = startAngle + delta * t;
                Vector3 arcPoint = newCentroid + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * arcRadius;
                arcPoint = MakeReachableDogTarget(waypointOrigin, arcPoint);
                arcPoint.y = 0f;
                m_ArcWaypoints.Enqueue(arcPoint);
                waypointOrigin = arcPoint;
            }

            LogRobotEvent($"Cluster switch transition started. phase={m_TransitionPhase}, target={m_TransitionTarget}");
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

        private void LogRobotTargetDiagnostic(Vector3 dogPosition, Vector3 rawTarget, Vector3 safeTarget, TargetSafetyInfo safetyInfo)
        {
            if (!enableDebugLogs)
            {
                return;
            }

            Vector3 dogPlanar = dogPosition;
            Vector3 targetPlanar = safeTarget;
            dogPlanar.y = 0f;
            targetPlanar.y = 0f;

            float targetDistance = Vector3.Distance(dogPlanar, targetPlanar);
            float movedDistance = 0f;
            float elapsed = m_LastDiagnosticTime > 0f ? Mathf.Max(0.0001f, Time.time - m_LastDiagnosticTime) : 0f;

            if (m_HasLastDogPosition)
            {
                Vector3 lastPlanar = m_LastDogPosition;
                lastPlanar.y = 0f;
                movedDistance = Vector3.Distance(lastPlanar, dogPlanar);
            }

            bool targetChanged = Vector3.Distance(rawTarget, safeTarget) > 0.05f;
            bool nearTarget = targetDistance <= Mathf.Max(0.1f, stoppedTargetDistanceThreshold);
            bool barelyMoved = m_HasLastDogPosition && movedDistance <= Mathf.Max(0.001f, stoppedMoveDistanceThreshold);

            if (!nearTarget && barelyMoved)
            {
                m_RobotStoppedTimer += elapsed > 0f ? elapsed : Mathf.Max(0.1f, debugLogInterval);
            }
            else
            {
                m_RobotStoppedTimer = 0f;
            }

            bool likelyStopped = m_RobotStoppedTimer >= Mathf.Max(0.1f, stoppedDurationBeforeLog);
            string state = likelyStopped ? "STOPPED_SUSPECTED" : nearTarget ? "NEAR_TARGET" : "COMMANDING";
            string boundsState = safetyInfo.hadFieldBounds ? "bounds=ok" : "bounds=unavailable";
            string polygonState = !safetyInfo.hadFencePolygon
                ? "polygon=unavailable"
                : $"polygonRaw={(safetyInfo.rawInsideErodedPolygon ? "ok" : "reject")}, polygonSafe={(safetyInfo.clampedInsideErodedPolygon ? "ok" : "reject")}, edgeDist={safetyInfo.distanceToFenceEdge:F2}m, erosion={fencePolygonErosion:F2}m";
            string correction = targetChanged || safetyInfo.fenceBlockedPath || safetyInfo.adjustedForPolygon
                ? $"correction={safetyInfo.correctionReason}, raw={rawTarget}, safe={safeTarget}"
                : "correction=none";

            Debug.Log($"[Control][RobotTarget] state={state}, phase={m_TransitionPhase}, targetDistance={targetDistance:F2}m, movedSinceLastLog={movedDistance:F3}m, stoppedFor={m_RobotStoppedTimer:F2}s, {boundsState}, {polygonState}, pathFenceHit={safetyInfo.fenceBlockedPath}, {correction}", this);

            m_LastDogPosition = dogPosition;
            m_HasLastDogPosition = true;
            m_LastDiagnosticTime = Time.time;
        }

        private void LogRobotEvent(string message)
        {
            if (enableDebugLogs)
            {
                Debug.Log($"[Control][RobotTarget] {message}", this);
            }
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
            int[] clusterIds = new int[count];
            for (int i = 0; i < count; i++)
            {
                clusterIds[i] = -1;
            }

            float joinDistance = Mathf.Max(0.1f, clusterJoinDistance);
            float joinDistanceSqr = joinDistance * joinDistance;
            int minCorePoints = Mathf.Max(1, clusterMinCorePoints);

            List<int>[] neighbourIndices = new List<int>[count];
            for (int i = 0; i < count; i++)
            {
                neighbourIndices[i] = new List<int>();
            }

            // Precompute neighbourhood graph in XZ plane.
            for (int i = 0; i < count; i++)
            {
                neighbourIndices[i].Add(i);
                for (int j = i + 1; j < count; j++)
                {
                    Vector3 delta = sheepPositions[j] - sheepPositions[i];
                    delta.y = 0f;
                    if (delta.sqrMagnitude <= joinDistanceSqr)
                    {
                        neighbourIndices[i].Add(j);
                        neighbourIndices[j].Add(i);
                    }
                }
            }

            int clusterId = 0;
            for (int i = 0; i < count; i++)
            {
                if (visited[i])
                {
                    continue;
                }

                visited[i] = true;

                // Not a dense-enough seed; leave as noise/border candidate for now.
                if (neighbourIndices[i].Count < minCorePoints)
                {
                    continue;
                }

                List<Vector3> cluster = new List<Vector3>();
                Queue<int> seeds = new Queue<int>();
                seeds.Enqueue(i);

                while (seeds.Count > 0)
                {
                    int idx = seeds.Dequeue();
                    if (clusterIds[idx] == clusterId)
                    {
                        continue;
                    }

                    if (clusterIds[idx] == -1)
                    {
                        clusterIds[idx] = clusterId;
                        cluster.Add(sheepPositions[idx]);
                    }

                    bool isCore = neighbourIndices[idx].Count >= minCorePoints;
                    if (!isCore)
                    {
                        continue;
                    }

                    List<int> neighbours = neighbourIndices[idx];
                    for (int n = 0; n < neighbours.Count; n++)
                    {
                        int neighbourIndex = neighbours[n];

                        if (!visited[neighbourIndex])
                        {
                            visited[neighbourIndex] = true;
                            if (neighbourIndices[neighbourIndex].Count >= minCorePoints)
                            {
                                seeds.Enqueue(neighbourIndex);
                            }
                        }

                        if (clusterIds[neighbourIndex] == -1)
                        {
                            clusterIds[neighbourIndex] = clusterId;
                            cluster.Add(sheepPositions[neighbourIndex]);
                        }
                    }
                }

                if (cluster.Count > 0)
                {
                    clusters.Add(cluster);
                    clusterId++;
                }
            }

            // If no dense clusters were found, keep sheep separated into tight singleton clusters.
            if (clusters.Count == 0)
            {
                for (int i = 0; i < count; i++)
                {
                    List<Vector3> singleton = new List<Vector3>(1) { sheepPositions[i] };
                    clusters.Add(singleton);
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
            RefreshFencePolygon();

            GameObject[] fences = GameObject.FindGameObjectsWithTag("Fence");
            if (fences == null || fences.Length == 0)
            {
                m_HasFieldBounds = false;

                if (enableDebugLogs && !m_LoggedMissingFences)
                {
                    m_LoggedMissingFences = true;
                    Debug.LogWarning("[Control] No fence objects found with tag 'Fence'. Field bounds disabled.", this);
                }
                return;
            }

            m_LoggedMissingFences = false;

            bool hasBounds = false;
            Vector3 min = Vector3.zero;
            Vector3 max = Vector3.zero;

            for (int i = 0; i < fences.Length; i++)
            {
                GameObject fence = fences[i];
                if (fence == null)
                {
                    continue;
                }

                Collider[] cols = fence.GetComponentsInChildren<Collider>();
                if (cols != null && cols.Length > 0)
                {
                    for (int c = 0; c < cols.Length; c++)
                    {
                        Collider col = cols[c];
                        if (col == null)
                        {
                            continue;
                        }

                        Bounds b = col.bounds;
                        if (!hasBounds)
                        {
                            min = b.min;
                            max = b.max;
                            hasBounds = true;
                        }
                        else
                        {
                            min = Vector3.Min(min, b.min);
                            max = Vector3.Max(max, b.max);
                        }
                    }
                }
                else
                {
                    Vector3 p = fence.transform.position;
                    if (!hasBounds)
                    {
                        min = p;
                        max = p;
                        hasBounds = true;
                    }
                    else
                    {
                        min = Vector3.Min(min, p);
                        max = Vector3.Max(max, p);
                    }
                }
            }

            if (!hasBounds)
            {
                m_HasFieldBounds = false;
                return;
            }

            min.x -= boundaryPadding;
            min.z -= boundaryPadding;
            max.x += boundaryPadding;
            max.z += boundaryPadding;

            m_FieldBounds = new Bounds((min + max) * 0.5f, new Vector3(max.x - min.x, 1000f, max.z - min.z));
            m_HasFieldBounds = true;

        }

        private void RefreshFencePolygon()
        {
            m_FieldPolygon.Clear();
            m_HasFieldPolygon = false;

            FenceManager fenceManager = FindFirstObjectByType<FenceManager>();
            if (fenceManager == null || fenceManager.fencePoses == null || fenceManager.fencePoses.Count < 3)
            {
                RefreshFencePolygonFromSceneObjects();
                return;
            }

            for (int i = 0; i < fenceManager.fencePoses.Count; i++)
            {
                Vector3 point = fenceManager.fencePoses[i].position;
                Vector2 xz = new Vector2(point.x, point.z);

                if (m_FieldPolygon.Count > 0 && (m_FieldPolygon[m_FieldPolygon.Count - 1] - xz).sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                m_FieldPolygon.Add(xz);
            }

            if (m_FieldPolygon.Count > 2 && (m_FieldPolygon[0] - m_FieldPolygon[m_FieldPolygon.Count - 1]).sqrMagnitude < 0.0001f)
            {
                m_FieldPolygon.RemoveAt(m_FieldPolygon.Count - 1);
            }

            m_HasFieldPolygon = m_FieldPolygon.Count >= 3;
        }

        private void RefreshFencePolygonFromSceneObjects()
        {
            GameObject[] fences = GameObject.FindGameObjectsWithTag("Fence");
            if (fences == null || fences.Length < 3)
            {
                return;
            }

            List<Vector2> unordered = new List<Vector2>(fences.Length);
            for (int i = 0; i < fences.Length; i++)
            {
                if (fences[i] == null)
                {
                    continue;
                }

                Vector3 p = fences[i].transform.position;
                Vector2 xz = new Vector2(p.x, p.z);
                bool duplicate = false;
                for (int j = 0; j < unordered.Count; j++)
                {
                    if ((unordered[j] - xz).sqrMagnitude < 0.0001f)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    unordered.Add(xz);
                }
            }

            if (unordered.Count < 3)
            {
                return;
            }

            int startIndex = 0;
            for (int i = 1; i < unordered.Count; i++)
            {
                if (unordered[i].x < unordered[startIndex].x ||
                    (Mathf.Approximately(unordered[i].x, unordered[startIndex].x) && unordered[i].y < unordered[startIndex].y))
                {
                    startIndex = i;
                }
            }

            Vector2 current = unordered[startIndex];
            m_FieldPolygon.Add(current);
            unordered.RemoveAt(startIndex);

            while (unordered.Count > 0)
            {
                int nearestIndex = 0;
                float nearestDistance = (unordered[0] - current).sqrMagnitude;
                for (int i = 1; i < unordered.Count; i++)
                {
                    float distance = (unordered[i] - current).sqrMagnitude;
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearestIndex = i;
                    }
                }

                current = unordered[nearestIndex];
                m_FieldPolygon.Add(current);
                unordered.RemoveAt(nearestIndex);
            }

            m_HasFieldPolygon = m_FieldPolygon.Count >= 3;
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

                candidate = MakeReachableDogTarget(dogPosition, candidate);
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

            float inset = Mathf.Max(0f, dogBoundaryInset);
            float minX = m_FieldBounds.min.x + inset;
            float maxX = m_FieldBounds.max.x - inset;
            float minZ = m_FieldBounds.min.z + inset;
            float maxZ = m_FieldBounds.max.z - inset;

            if (minX > maxX)
            {
                float mid = (m_FieldBounds.min.x + m_FieldBounds.max.x) * 0.5f;
                minX = mid;
                maxX = mid;
            }

            if (minZ > maxZ)
            {
                float mid = (m_FieldBounds.min.z + m_FieldBounds.max.z) * 0.5f;
                minZ = mid;
                maxZ = mid;
            }

            return point.x >= minX && point.x <= maxX && point.z >= minZ && point.z <= maxZ;
        }

        private Vector3 ClampToField(Vector3 point)
        {
            if (!m_HasFieldBounds)
            {
                return point;
            }

            float inset = Mathf.Max(0f, dogBoundaryInset);
            float minX = m_FieldBounds.min.x + inset;
            float maxX = m_FieldBounds.max.x - inset;
            float minZ = m_FieldBounds.min.z + inset;
            float maxZ = m_FieldBounds.max.z - inset;

            if (minX > maxX)
            {
                float mid = (m_FieldBounds.min.x + m_FieldBounds.max.x) * 0.5f;
                minX = mid;
                maxX = mid;
            }

            if (minZ > maxZ)
            {
                float mid = (m_FieldBounds.min.z + m_FieldBounds.max.z) * 0.5f;
                minZ = mid;
                maxZ = mid;
            }

            point.x = Mathf.Clamp(point.x, minX, maxX);
            point.z = Mathf.Clamp(point.z, minZ, maxZ);
            return point;
        }

        private Vector3 MakeReachableDogTarget(Vector3 dogPosition, Vector3 desiredTarget)
        {
            TargetSafetyInfo safetyInfo;
            return MakeReachableDogTarget(dogPosition, desiredTarget, out safetyInfo);
        }

        private Vector3 MakeReachableDogTarget(Vector3 dogPosition, Vector3 desiredTarget, out TargetSafetyInfo safetyInfo)
        {
            Vector3 clamped = ClampToField(desiredTarget);
            safetyInfo = new TargetSafetyInfo
            {
                hadFieldBounds = m_HasFieldBounds,
                hadFencePolygon = m_HasFieldPolygon,
                rawInsideErodedPolygon = IsInsideErodedFencePolygon(desiredTarget),
                clampedInsideErodedPolygon = IsInsideErodedFencePolygon(clamped),
                distanceToFenceEdge = GetDistanceToFencePolygonEdge(clamped),
                correctionReason = "none"
            };

            if (m_HasFieldPolygon && !safetyInfo.clampedInsideErodedPolygon)
            {
                Vector3 polygonSafe = MoveTargetInsideErodedFencePolygon(dogPosition, clamped);
                safetyInfo.adjustedForPolygon = true;
                safetyInfo.correctionReason = safetyInfo.rawInsideErodedPolygon ? "rect_clamp_near_fence" : "outside_or_too_close_to_fence_polygon";
                clamped = polygonSafe;
                safetyInfo.clampedInsideErodedPolygon = IsInsideErodedFencePolygon(clamped);
                safetyInfo.distanceToFenceEdge = GetDistanceToFencePolygonEdge(clamped);
            }

            float probeHeight = Mathf.Max(0.05f, dogFenceProbeHeight);
            Vector3 from = dogPosition + Vector3.up * probeHeight;
            Vector3 to = clamped + Vector3.up * probeHeight;
            Vector3 ray = to - from;
            float dist = ray.magnitude;

            if (dist < 0.0001f)
            {
                clamped.y = dogPosition.y;
                return clamped;
            }

            Vector3 dir = ray / dist;
            if (TryGetNearestFenceHit(from, dir, dist, out RaycastHit hit))
            {
                Vector3 fallback = hit.point - dir * Mathf.Max(0.4f, dogBoundaryInset * 0.5f);
                fallback = MoveTargetInsideErodedFencePolygon(dogPosition, ClampToField(fallback));
                fallback.y = dogPosition.y;
                safetyInfo.fenceBlockedPath = true;
                safetyInfo.correctionReason = "path_crosses_fence";
                safetyInfo.clampedInsideErodedPolygon = IsInsideErodedFencePolygon(fallback);
                safetyInfo.distanceToFenceEdge = GetDistanceToFencePolygonEdge(fallback);
                return fallback;
            }

            clamped.y = dogPosition.y;
            return clamped;
        }

        private bool IsInsideErodedFencePolygon(Vector3 point)
        {
            if (!m_HasFieldPolygon)
            {
                return true;
            }

            Vector2 xz = new Vector2(point.x, point.z);
            if (!IsPointInPolygon(xz, m_FieldPolygon))
            {
                return false;
            }

            float erosion = Mathf.Max(0f, fencePolygonErosion);
            if (erosion <= 0f)
            {
                return true;
            }

            return GetDistanceToPolygonEdges(xz, m_FieldPolygon) >= erosion;
        }

        private Vector3 MoveTargetInsideErodedFencePolygon(Vector3 origin, Vector3 target)
        {
            if (!m_HasFieldPolygon || IsInsideErodedFencePolygon(target))
            {
                return target;
            }

            Vector3 safeOrigin = origin;
            if (!IsInsideErodedFencePolygon(safeOrigin))
            {
                safeOrigin = GetFencePolygonCenter();
            }

            if (!IsInsideErodedFencePolygon(safeOrigin))
            {
                Vector2 closest = GetClosestPointOnPolygonEdges(new Vector2(target.x, target.z), m_FieldPolygon);
                Vector3 pulled = Vector3.Lerp(new Vector3(closest.x, target.y, closest.y), GetFencePolygonCenter(), 0.2f);
                return ClampToField(pulled);
            }

            Vector3 low = safeOrigin;
            Vector3 high = target;
            for (int i = 0; i < 18; i++)
            {
                Vector3 mid = Vector3.Lerp(low, high, 0.5f);
                if (IsInsideErodedFencePolygon(mid))
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            low.y = target.y;
            return ClampToField(low);
        }

        private Vector3 GetFencePolygonCenter()
        {
            if (!m_HasFieldPolygon)
            {
                return m_FieldBounds.center;
            }

            Vector2 center = Vector2.zero;
            for (int i = 0; i < m_FieldPolygon.Count; i++)
            {
                center += m_FieldPolygon[i];
            }

            center /= m_FieldPolygon.Count;
            return new Vector3(center.x, m_FieldBounds.center.y, center.y);
        }

        private float GetDistanceToFencePolygonEdge(Vector3 point)
        {
            if (!m_HasFieldPolygon)
            {
                return -1f;
            }

            return GetDistanceToPolygonEdges(new Vector2(point.x, point.z), m_FieldPolygon);
        }

        private static bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                float denom = b.y - a.y;
                if (Mathf.Abs(denom) < 0.000001f)
                {
                    denom = denom < 0f ? -0.000001f : 0.000001f;
                }

                bool crosses = ((a.y > point.y) != (b.y > point.y)) &&
                    (point.x < (b.x - a.x) * (point.y - a.y) / denom + a.x);

                if (crosses)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static float GetDistanceToPolygonEdges(Vector2 point, List<Vector2> polygon)
        {
            float best = float.PositiveInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                float distance = DistancePointToSegment(point, a, b);
                if (distance < best)
                {
                    best = distance;
                }
            }

            return best;
        }

        private static Vector2 GetClosestPointOnPolygonEdges(Vector2 point, List<Vector2> polygon)
        {
            Vector2 closest = polygon[0];
            float best = float.PositiveInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                Vector2 candidate = ClosestPointOnSegment(point, a, b);
                float distance = (candidate - point).sqrMagnitude;
                if (distance < best)
                {
                    best = distance;
                    closest = candidate;
                }
            }

            return closest;
        }

        private static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            return Vector2.Distance(point, ClosestPointOnSegment(point, a, b));
        }

        private static Vector2 ClosestPointOnSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denom = ab.sqrMagnitude;
            if (denom < 0.000001f)
            {
                return a;
            }

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denom);
            return a + ab * t;
        }

        private bool TryGetNearestFenceHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearestFenceHit)
        {
            nearestFenceHit = new RaycastHit();

            RaycastHit[] hits = Physics.RaycastAll(origin, direction, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            bool foundFence = false;
            float nearestDistance = float.PositiveInfinity;

            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null || !IsFenceCollider(hit.collider))
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearestFenceHit = hit;
                    foundFence = true;
                }
            }

            return foundFence;
        }

        private static bool IsFenceCollider(Collider collider)
        {
            if (collider == null)
            {
                return false;
            }

            if (collider.CompareTag("Fence"))
            {
                return true;
            }

            Transform parent = collider.transform.parent;
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
            }

            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] == null)
                {
                    GameObject instance = Instantiate(prefab, debugVisualsRoot);
                    instance.name = $"{baseName}_{i:D2}";
                    pool[i] = instance.transform;
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
            }
        }

        private void RenderBoundaryRings(List<List<Vector3>> clusters)
        {
            int pointCount = Mathf.Max(3, boundaryRingPointCount);
            int ringCount = clusters != null ? clusters.Count : 0;

            int neededMarkers = 0;
            if (clusters != null)
            {
                for (int i = 0; i < clusters.Count; i++)
                {
                    List<Vector3> cluster = clusters[i];
                    if (cluster == null || cluster.Count == 0)
                    {
                        continue;
                    }

                    List<Vector3> hull = ComputeClusterHull(cluster);
                    neededMarkers += Mathf.Max(hull.Count, pointCount);
                }
            }

            neededMarkers = Mathf.Max(pointCount, neededMarkers);
            EnsureVisualPool(m_BoundaryVisuals, neededMarkers, boundaryPrefab, "Boundary");

            int markerIndex = 0;
            for (int clusterIndex = 0; clusterIndex < ringCount; clusterIndex++)
            {
                List<Vector3> cluster = clusters[clusterIndex];
                if (cluster == null || cluster.Count == 0)
                {
                    continue;
                }

                List<Vector3> hull = ComputeClusterHull(cluster);
                if (hull.Count < 2)
                {
                    continue;
                }

                int clusterMarkerCount = Mathf.Max(pointCount, hull.Count);

                for (int i = 0; i < clusterMarkerCount && markerIndex < m_BoundaryVisuals.Count; i++, markerIndex++)
                {
                    Transform marker = m_BoundaryVisuals[markerIndex];
                    if (marker == null)
                    {
                        continue;
                    }

                    float t = (float)i / clusterMarkerCount;
                    Vector3 markerPosition = SampleHullPerimeter(hull, t);
                    TryGetGroundProjectedPoint(markerPosition, out markerPosition);
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

        private List<Vector3> ComputeClusterHull(List<Vector3> points)
        {
            List<Vector2> uniquePoints = new List<Vector2>();
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 p = new Vector2(points[i].x, points[i].z);
                bool exists = false;
                for (int j = 0; j < uniquePoints.Count; j++)
                {
                    if ((uniquePoints[j] - p).sqrMagnitude < 0.0001f)
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    uniquePoints.Add(p);
                }
            }

            if (uniquePoints.Count == 0)
            {
                return new List<Vector3>();
            }

            if (uniquePoints.Count <= 2)
            {
                List<Vector3> simple = new List<Vector3>(uniquePoints.Count);
                for (int i = 0; i < uniquePoints.Count; i++)
                {
                    simple.Add(new Vector3(uniquePoints[i].x, 0f, uniquePoints[i].y));
                }
                return simple;
            }

            // Monotonic chain convex hull in XZ plane
            uniquePoints.Sort((a, b) =>
            {
                int xComp = a.x.CompareTo(b.x);
                return xComp != 0 ? xComp : a.y.CompareTo(b.y);
            });

            List<Vector2> lower = new List<Vector2>();
            for (int i = 0; i < uniquePoints.Count; i++)
            {
                Vector2 p = uniquePoints[i];
                while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0f)
                {
                    lower.RemoveAt(lower.Count - 1);
                }
                lower.Add(p);
            }

            List<Vector2> upper = new List<Vector2>();
            for (int i = uniquePoints.Count - 1; i >= 0; i--)
            {
                Vector2 p = uniquePoints[i];
                while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0f)
                {
                    upper.RemoveAt(upper.Count - 1);
                }
                upper.Add(p);
            }

            if (lower.Count > 0) lower.RemoveAt(lower.Count - 1);
            if (upper.Count > 0) upper.RemoveAt(upper.Count - 1);

            List<Vector3> hull = new List<Vector3>(lower.Count + upper.Count);
            for (int i = 0; i < lower.Count; i++)
            {
                hull.Add(new Vector3(lower[i].x, 0f, lower[i].y));
            }
            for (int i = 0; i < upper.Count; i++)
            {
                hull.Add(new Vector3(upper[i].x, 0f, upper[i].y));
            }

            return hull;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c)
        {
            Vector2 ab = b - a;
            Vector2 ac = c - a;
            return ab.x * ac.y - ab.y * ac.x;
        }

        private Vector3 SampleHullPerimeter(List<Vector3> hull, float t)
        {
            if (hull == null || hull.Count == 0)
            {
                return Vector3.zero;
            }

            if (hull.Count == 1)
            {
                return hull[0];
            }

            float perimeter = 0f;
            for (int i = 0; i < hull.Count; i++)
            {
                Vector3 a = hull[i];
                Vector3 b = hull[(i + 1) % hull.Count];
                perimeter += Vector3.Distance(a, b);
            }

            if (perimeter < 0.0001f)
            {
                return hull[0];
            }

            float distanceAlong = Mathf.Clamp01(t) * perimeter;
            float traveled = 0f;

            for (int i = 0; i < hull.Count; i++)
            {
                Vector3 a = hull[i];
                Vector3 b = hull[(i + 1) % hull.Count];
                float edgeLen = Vector3.Distance(a, b);
                if (traveled + edgeLen >= distanceAlong)
                {
                    float edgeT = (distanceAlong - traveled) / Mathf.Max(0.0001f, edgeLen);
                    return Vector3.Lerp(a, b, edgeT);
                }

                traveled += edgeLen;
            }

            return hull[hull.Count - 1];
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
                    TryGetGroundProjectedPoint(markerPosition, out markerPosition);
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
            TryGetGroundProjectedPoint(markerPosition, out markerPosition);
            m_ChosenVisual.position = markerPosition;
            m_ChosenVisual.gameObject.SetActive(true);
        }
    }
}
