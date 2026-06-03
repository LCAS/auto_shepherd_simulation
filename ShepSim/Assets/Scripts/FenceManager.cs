using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

public class FenceManager : MonoBehaviour
{
    private const string FenceTag = "Fence";
    private readonly Dictionary<int, float> _prefabSpacingCache = new Dictionary<int, float>();
    private readonly List<Vector3> _cachedLocalBoundary = new List<Vector3>();

    public enum VerticalAxis
    {
        Y,
        Z
    }

    [System.Serializable]
    public class FencePose
    {
        public Vector3 position;
        public Quaternion rotation;
        public string fenceType;
    }

    private struct GeoPoint
    {
        public double latitude;
        public double longitude;
    }

    public List<FencePose> fencePoses = new List<FencePose>();

    [Header("Fence Prefabs")]
    public GameObject fenceType1Prefab;
    public GameObject fenceType2Prefab;

    [Header("Fence Boundary Input")]
    public TextAsset fenceMapYaml;
    [Tooltip("Optional file path. If not absolute, it is resolved relative to Assets/ then Assets/Environments/.")]
    public string yamlFilePath;

    [Header("Spawn Settings")]
    public Transform fenceParent;
    public float fenceSpacingMeters = 2f;
    [Tooltip("Multiplier applied to measured prefab spacing. Values below 1 reduce visible gaps.")]
    [Range(0.5f, 1.25f)]
    public float spacingMultiplier = 0.95f;
    [Tooltip("How far each segment may overshoot corners as a fraction of typical spacing.")]
    [Range(0f, 1.5f)]
    public float cornerOverlapFactor = 0.5f;
    public float groundY = 0f;
    public bool spawnOnStart;
    public bool clearExistingChildren = true;

    [Header("Performance Safety")]
    [Tooltip("Hard cap on total fence instances spawned in one build. Prevents lock-ups from huge boundaries or tiny spacing.")]
    [Min(100)] public int maxFencesToSpawn = 8000;
    [Tooltip("When playing, fences are spawned in batches to keep the game responsive.")]
    [Min(1)] public int fencesPerFrame = 250;

    [Header("Spawn Timing")]
    [Tooltip("If true, fence spawn waits until end-of-frame so surface deformation scripts can update meshes/colliders first.")]
    public bool waitForEndOfFrameBeforeSpawn = true;
    [Tooltip("Additional frame delay before spawning fences. Useful when terrain colliders update a frame later.")]
    [Min(0)] public int additionalSpawnDelayFrames = 1;
    [Tooltip("Optional PlaneUndulation reference to force-apply before spawning fences.")]
    public PlaneUndulation planeUndulation;
    [Tooltip("If true, calls ApplyUndulation() on the referenced PlaneUndulation before fence projection.")]
    public bool forceApplyUndulationBeforeSpawn = true;

    [Header("Surface Projection")]
    public bool placeOnSurface = true;
    [Tooltip("Optional collider to project onto (for example the undulated plane MeshCollider).")]
    public Collider surfaceCollider;
    [Tooltip("If true, raycast the assigned surfaceCollider first, then fall back to Physics raycasts if it misses.")]
    public bool fallbackToPhysicsIfSurfaceMisses = true;
    public LayerMask surfaceLayerMask = ~0;
    [Tooltip("Select which world axis is treated as vertical for projection and baseline placement.")]
    public VerticalAxis verticalAxis = VerticalAxis.Y;
    [Tooltip("When enabled, surface projection raycasts ignore colliders tagged Fence to avoid snapping onto already spawned fence pieces.")]
    public bool ignoreFenceCollidersInProjection = true;
    public float raycastStartHeight = 200f;
    public float raycastDistance = 500f;
    public float fenceYOffset = 0f;

    [Header("Rotation")]
    [Tooltip("Yaw correction applied to all spawned fence prefabs to account for mesh forward-axis mismatch.")]
    public float prefabYawOffsetDegrees = 90f;

    private Coroutine _spawnRoutine;

    private void Start()
    {
        if (spawnOnStart)
        {
            StartCoroutine(SpawnOnStartRoutine());
        }
        else
        {
            InitializeFencePoses();
        }
    }

    private IEnumerator SpawnOnStartRoutine()
    {
        PlaneUndulation undulation = planeUndulation;
        if (undulation == null && surfaceCollider != null)
        {
            undulation = surfaceCollider.GetComponent<PlaneUndulation>();
        }

        if (forceApplyUndulationBeforeSpawn && undulation != null)
        {
            undulation.ApplyUndulation();
        }

        if (waitForEndOfFrameBeforeSpawn)
        {
            yield return new WaitForEndOfFrame();
        }

        for (int i = 0; i < additionalSpawnDelayFrames; i++)
        {
            yield return null;
        }

        Physics.SyncTransforms();
        BuildFromYamlAndSpawn();
    }

    private void InitializeFencePoses()
    {
        fencePoses.Clear();

        GameObject[] fences = GameObject.FindGameObjectsWithTag(FenceTag);
        
        foreach (GameObject fence in fences)
        {
            FencePose pose = new FencePose
            {
                position = fence.transform.position,
                rotation = fence.transform.rotation,
                fenceType = fence.name.Contains("Type1") ? "Type1" : "Type2"
            };
            fencePoses.Add(pose);
        }
    }

    [ContextMenu("Build From YAML And Spawn")]
    public void BuildFromYamlAndSpawn()
    {
        _prefabSpacingCache.Clear();

        string yamlContent = LoadYamlContent();
        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            Debug.LogError("FenceManager: No YAML content provided. Assign fenceMapYaml or yamlFilePath.");
            return;
        }

        List<GeoPoint> geoBoundary = ParseBoundaryPoints(yamlContent);
        if (geoBoundary.Count < 3)
        {
            Debug.LogError("FenceManager: fence_map YAML must include at least 3 latitude/longitude points.");
            return;
        }

        if (fenceSpacingMeters <= 0f)
        {
            Debug.LogError("FenceManager: fenceSpacingMeters must be greater than 0.");
            return;
        }

        List<Vector3> localBoundary = ConvertBoundaryToLocalMeters(geoBoundary, groundY);
        CacheLocalBoundary(localBoundary);

        if (clearExistingChildren)
        {
            ClearAllFences();
        }

        if (Application.isPlaying)
        {
            if (_spawnRoutine != null)
            {
                StopCoroutine(_spawnRoutine);
                _spawnRoutine = null;
            }

            _spawnRoutine = StartCoroutine(SpawnAlongBoundaryRoutine(localBoundary));
        }
        else
        {
            SpawnAlongBoundary(localBoundary, Mathf.Max(100, maxFencesToSpawn));
        }
    }

    private IEnumerator SpawnAlongBoundaryRoutine(List<Vector3> localBoundary)
    {
        int totalLimit = Mathf.Max(100, maxFencesToSpawn);
        int perFrameLimit = Mathf.Max(1, fencesPerFrame);

        int spawnedTotal = 0;
        int spawnedThisFrame = 0;

        Transform parent = fenceParent != null ? fenceParent : transform;
        fencePoses.Clear();
        float cornerOverlapDistance = GetTypicalSpacing() * Mathf.Max(0f, cornerOverlapFactor);
        Vector3 upDir = GetVerticalDirection();

        for (int i = 0; i < localBoundary.Count; i++)
        {
            Vector3 start = localBoundary[i];
            Vector3 end = localBoundary[(i + 1) % localBoundary.Count];

            Vector3 segment = end - start;
            float length = segment.magnitude;
            if (length <= 0.001f)
            {
                continue;
            }

            Vector3 direction = segment / length;
            Vector3 planarDirection = Vector3.ProjectOnPlane(direction, upDir);
            if (planarDirection.sqrMagnitude < 0.000001f)
            {
                continue;
            }
            Quaternion rotation = Quaternion.LookRotation(planarDirection.normalized, upDir);

            for (float distance = -cornerOverlapDistance; distance <= length + cornerOverlapDistance;)
            {
                if (spawnedTotal >= totalLimit)
                {
                    Debug.LogWarning($"FenceManager: Spawn halted at {spawnedTotal} instances (maxFencesToSpawn={totalLimit}). Increase limit or spacing if needed.", this);
                    _spawnRoutine = null;
                    yield break;
                }

                GameObject prefab = GetRandomFencePrefab();
                if (prefab == null)
                {
                    Debug.LogWarning("FenceManager: No fence prefabs assigned.");
                    _spawnRoutine = null;
                    yield break;
                }

                float spacing = Mathf.Max(0.05f, GetSpacingForPrefab(prefab));
                Vector3 localPos = start + direction * distance;
                SpawnFenceAtLocal(parent, localPos, rotation, prefab);

                spawnedTotal++;
                spawnedThisFrame++;
                distance += spacing;

                if (spawnedThisFrame >= perFrameLimit)
                {
                    spawnedThisFrame = 0;
                    yield return null;
                }
            }
        }

        _spawnRoutine = null;
    }

    private string LoadYamlContent()
    {
        if (fenceMapYaml != null)
        {
            return fenceMapYaml.text;
        }

        if (string.IsNullOrWhiteSpace(yamlFilePath))
        {
            return null;
        }

        if (File.Exists(yamlFilePath))
        {
            return File.ReadAllText(yamlFilePath);
        }

        string assetsPath = Application.dataPath;
        string candidate = Path.Combine(assetsPath, yamlFilePath);
        if (File.Exists(candidate))
        {
            return File.ReadAllText(candidate);
        }

        candidate = Path.Combine(assetsPath, "Environments", yamlFilePath);
        if (File.Exists(candidate))
        {
            return File.ReadAllText(candidate);
        }

        Debug.LogError("FenceManager: Could not find YAML file at yamlFilePath.");
        return null;
    }

    private List<GeoPoint> ParseBoundaryPoints(string yamlContent)
    {
        List<GeoPoint> points = new List<GeoPoint>();

        Regex latRegex = new Regex(@"latitude:\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)");
        Regex lonRegex = new Regex(@"longitude:\s*([-+]?\d*\.?\d+(?:[eE][-+]?\d+)?)");

        double? pendingLatitude = null;
        string[] lines = yamlContent.Split('\n');

        foreach (string raw in lines)
        {
            string line = raw.Trim();

            Match latMatch = latRegex.Match(line);
            if (latMatch.Success)
            {
                if (double.TryParse(latMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat))
                {
                    pendingLatitude = lat;
                }
                continue;
            }

            Match lonMatch = lonRegex.Match(line);
            if (lonMatch.Success && pendingLatitude.HasValue)
            {
                if (double.TryParse(lonMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                {
                    points.Add(new GeoPoint
                    {
                        latitude = pendingLatitude.Value,
                        longitude = lon
                    });
                }
                pendingLatitude = null;
            }
        }

        return points;
    }

    private List<Vector3> ConvertBoundaryToLocalMeters(List<GeoPoint> geoBoundary, float y)
    {
        double centerLat = 0.0;
        double centerLon = 0.0;

        for (int i = 0; i < geoBoundary.Count; i++)
        {
            centerLat += geoBoundary[i].latitude;
            centerLon += geoBoundary[i].longitude;
        }

        centerLat /= geoBoundary.Count;
        centerLon /= geoBoundary.Count;

        double latRad = centerLat * Mathf.Deg2Rad;
        double metersPerDegLat = 111132.92 - 559.82 * System.Math.Cos(2.0 * latRad) + 1.175 * System.Math.Cos(4.0 * latRad);
        double metersPerDegLon = 111412.84 * System.Math.Cos(latRad) - 93.5 * System.Math.Cos(3.0 * latRad);

        List<Vector3> localBoundary = new List<Vector3>(geoBoundary.Count);
        Vector3 upDir = GetVerticalDirection();
        for (int i = 0; i < geoBoundary.Count; i++)
        {
            double east = (geoBoundary[i].longitude - centerLon) * metersPerDegLon;
            double north = (geoBoundary[i].latitude - centerLat) * metersPerDegLat;

            if (upDir == Vector3.up)
            {
                localBoundary.Add(new Vector3((float)east, y, (float)north));
            }
            else
            {
                localBoundary.Add(new Vector3((float)east, (float)north, y));
            }
        }

        return localBoundary;
    }

    private void SpawnAlongBoundary(List<Vector3> localBoundary, int maxSpawnCount)
    {
        Transform parent = fenceParent != null ? fenceParent : transform;
        fencePoses.Clear();
        float cornerOverlapDistance = GetTypicalSpacing() * Mathf.Max(0f, cornerOverlapFactor);
        Vector3 upDir = GetVerticalDirection();
        int spawnedTotal = 0;

        for (int i = 0; i < localBoundary.Count; i++)
        {
            Vector3 start = localBoundary[i];
            Vector3 end = localBoundary[(i + 1) % localBoundary.Count];

            Vector3 segment = end - start;
            float length = segment.magnitude;
            if (length <= 0.001f)
            {
                continue;
            }

            Vector3 direction = segment / length;
            Vector3 planarDirection = Vector3.ProjectOnPlane(direction, upDir);
            if (planarDirection.sqrMagnitude < 0.000001f)
            {
                continue;
            }
            Quaternion rotation = Quaternion.LookRotation(planarDirection.normalized, upDir);

            for (float distance = -cornerOverlapDistance; distance <= length + cornerOverlapDistance;)
            {
                if (spawnedTotal >= maxSpawnCount)
                {
                    Debug.LogWarning($"FenceManager: Spawn halted at {spawnedTotal} instances (maxFencesToSpawn={maxSpawnCount}).", this);
                    return;
                }

                GameObject prefab = GetRandomFencePrefab();
                if (prefab == null)
                {
                    Debug.LogWarning("FenceManager: No fence prefabs assigned.");
                    return;
                }

                float spacing = Mathf.Max(0.05f, GetSpacingForPrefab(prefab));
                Vector3 localPos = start + direction * distance;
                SpawnFenceAtLocal(parent, localPos, rotation, prefab);
                spawnedTotal++;
                distance += spacing;
            }
        }
    }

    public bool TryGetBoundaryWorldPoints(out List<Vector3> worldBoundary)
    {
        worldBoundary = new List<Vector3>();

        if (_cachedLocalBoundary.Count < 3)
        {
            if (!TryRebuildCachedBoundaryFromYaml())
            {
                return false;
            }
        }

        Transform parent = fenceParent != null ? fenceParent : transform;
        for (int i = 0; i < _cachedLocalBoundary.Count; i++)
        {
            worldBoundary.Add(parent.TransformPoint(_cachedLocalBoundary[i]));
        }

        return worldBoundary.Count >= 3;
    }

    private void CacheLocalBoundary(List<Vector3> localBoundary)
    {
        _cachedLocalBoundary.Clear();
        if (localBoundary == null)
        {
            return;
        }

        for (int i = 0; i < localBoundary.Count; i++)
        {
            _cachedLocalBoundary.Add(localBoundary[i]);
        }
    }

    private bool TryRebuildCachedBoundaryFromYaml()
    {
        string yamlContent = LoadYamlContent();
        if (string.IsNullOrWhiteSpace(yamlContent))
        {
            return false;
        }

        List<GeoPoint> geoBoundary = ParseBoundaryPoints(yamlContent);
        if (geoBoundary == null || geoBoundary.Count < 3)
        {
            return false;
        }

        List<Vector3> localBoundary = ConvertBoundaryToLocalMeters(geoBoundary, groundY);
        CacheLocalBoundary(localBoundary);
        return _cachedLocalBoundary.Count >= 3;
    }

    private float GetTypicalSpacing()
    {
        float spacing1 = fenceType1Prefab != null ? GetSpacingForPrefab(fenceType1Prefab) : -1f;
        float spacing2 = fenceType2Prefab != null ? GetSpacingForPrefab(fenceType2Prefab) : -1f;

        if (spacing1 > 0f && spacing2 > 0f)
        {
            return (spacing1 + spacing2) * 0.5f;
        }

        if (spacing1 > 0f)
        {
            return spacing1;
        }

        if (spacing2 > 0f)
        {
            return spacing2;
        }

        return Mathf.Max(0.1f, fenceSpacingMeters);
    }

    private void SpawnFenceAtLocal(Transform parent, Vector3 localPos, Quaternion localRot, GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogWarning("FenceManager: No fence prefabs assigned.");
            return;
        }

        Vector3 spawnLocalPos = localPos;
        if (placeOnSurface)
        {
            if (TryProjectToSurface(parent, localPos, out Vector3 projectedWorldPos))
            {
                spawnLocalPos = parent.InverseTransformPoint(projectedWorldPos);
            }
        }

        GameObject fence = Instantiate(prefab, parent);
        fence.transform.localPosition = spawnLocalPos;
        fence.transform.localRotation = localRot * Quaternion.Euler(0f, prefabYawOffsetDegrees, 0f);
        fence.tag = FenceTag;

        fencePoses.Add(new FencePose
        {
            position = fence.transform.position,
            rotation = fence.transform.rotation,
            fenceType = prefab == fenceType1Prefab ? "Type1" : "Type2"
        });
    }

    private GameObject GetRandomFencePrefab()
    {
        if (fenceType1Prefab != null && fenceType2Prefab != null)
        {
            return Random.value < 0.5f ? fenceType1Prefab : fenceType2Prefab;
        }

        return fenceType1Prefab != null ? fenceType1Prefab : fenceType2Prefab;
    }

    private float GetSpacingForPrefab(GameObject prefab)
    {
        float fallback = Mathf.Max(0.1f, fenceSpacingMeters);
        if (prefab == null)
        {
            return fallback;
        }

        int key = prefab.GetInstanceID();
        if (_prefabSpacingCache.TryGetValue(key, out float cached))
        {
            return cached;
        }

        float measured = MeasurePrefabSpacing(prefab);
        if (measured <= 0.001f)
        {
            measured = fallback;
        }

        float adjusted = Mathf.Max(0.05f, measured * spacingMultiplier);
        _prefabSpacingCache[key] = adjusted;
        return adjusted;
    }

    private float MeasurePrefabSpacing(GameObject prefab)
    {
        GameObject probe = Instantiate(prefab, Vector3.zero, Quaternion.identity);
        probe.hideFlags = HideFlags.HideAndDontSave;

        bool hasBounds = false;
        Bounds combined = new Bounds(probe.transform.position, Vector3.zero);

        Renderer[] renderers = probe.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!hasBounds)
            {
                combined = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(renderers[i].bounds);
            }
        }

        Collider[] colliders = probe.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (!hasBounds)
            {
                combined = colliders[i].bounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(colliders[i].bounds);
            }
        }

        float spacing = hasBounds ? Mathf.Max(combined.size.x, combined.size.z) : 0f;

        if (Application.isPlaying)
        {
            Destroy(probe);
        }
        else
        {
            DestroyImmediate(probe);
        }

        return spacing;
    }

    private bool TryProjectToSurface(Transform parent, Vector3 localPos, out Vector3 projectedWorldPos)
    {
        projectedWorldPos = parent.TransformPoint(localPos);

        float startHeight = Mathf.Max(1f, raycastStartHeight);
        float maxDistance = Mathf.Max(startHeight + 1f, raycastDistance);
        Vector3 upDir = GetVerticalDirection();
        Vector3 worldPos = parent.TransformPoint(localPos);
        Vector3 rayOrigin = worldPos + upDir * startHeight;
        Ray ray = new Ray(rayOrigin, -upDir);

        if (surfaceCollider != null)
        {
            if (surfaceCollider.Raycast(ray, out RaycastHit hit, maxDistance))
            {
                projectedWorldPos = hit.point + upDir * fenceYOffset;
                return true;
            }

            if (!fallbackToPhysicsIfSurfaceMisses)
            {
                return false;
            }
        }

        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, surfaceLayerMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
        {
            return false;
        }

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null)
            {
                continue;
            }

            if (ignoreFenceCollidersInProjection && IsFenceCollider(col))
            {
                continue;
            }

            projectedWorldPos = hits[i].point + upDir * fenceYOffset;
            return true;
        }

        return false;
    }

    private Vector3 GetVerticalDirection()
    {
        return verticalAxis == VerticalAxis.Z ? Vector3.forward : Vector3.up;
    }

    private static bool IsFenceCollider(Collider col)
    {
        if (col == null)
        {
            return false;
        }

        if (col.CompareTag(FenceTag))
        {
            return true;
        }

        Transform parent = col.transform.parent;
        while (parent != null)
        {
            if (parent.CompareTag(FenceTag))
            {
                return true;
            }

            parent = parent.parent;
        }

        return false;
    }

    public void SpawnFence(FencePose pose)
    {
        GameObject prefab = pose.fenceType == "Type1" ? fenceType1Prefab : fenceType2Prefab;
        if (prefab != null)
        {
            Transform parent = fenceParent != null ? fenceParent : null;
            GameObject fence = Instantiate(prefab, pose.position, pose.rotation, parent);
            fence.tag = FenceTag;
        }
    }

    public void SpawnAllFences()
    {
        foreach (FencePose pose in fencePoses)
        {
            SpawnFence(pose);
        }
    }

    [ContextMenu("Clear All Fences")]
    public void ClearAllFences()
    {
        if (fenceParent != null)
        {
            List<Transform> toDestroy = new List<Transform>();
            for (int i = 0; i < fenceParent.childCount; i++)
            {
                Transform child = fenceParent.GetChild(i);
                if (child.CompareTag(FenceTag))
                {
                    toDestroy.Add(child);
                }
            }

            for (int i = 0; i < toDestroy.Count; i++)
            {
                Destroy(toDestroy[i].gameObject);
            }
            return;
        }

        GameObject[] fences = GameObject.FindGameObjectsWithTag(FenceTag);
        foreach (GameObject fence in fences)
        {
            Destroy(fence);
        }
    }

    public void SaveFencePoses()
    {
        // Save fence poses to a JSON file
        string json = JsonUtility.ToJson(new FencePoseList { poses = fencePoses }, true);
        System.IO.File.WriteAllText(Application.persistentDataPath + "/fence_poses.json", json);
    }

    public void LoadFencePoses()
    {
        // Load fence poses from JSON file
        string path = Application.persistentDataPath + "/fence_poses.json";
        if (System.IO.File.Exists(path))
        {
            string json = System.IO.File.ReadAllText(path);
            FencePoseList poseList = JsonUtility.FromJson<FencePoseList>(json);
            fencePoses = poseList != null && poseList.poses != null ? poseList.poses : new List<FencePose>();
        }
    }

    [System.Serializable]
    private class FencePoseList
    {
        public List<FencePose> poses;
    }
} 