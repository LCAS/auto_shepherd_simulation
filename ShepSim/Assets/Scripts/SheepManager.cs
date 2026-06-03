using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SheepManager : MonoBehaviour
{
    [Header("References")]
    public GameObject SheepParentGameobject;
    public GameObject WhiteSheepPrefab;
    public GameObject BlackSheepPrefab;
    public FenceManager FenceManager;

    [Header("Spawn Settings")]
    [Tooltip("Percentage of spawned sheep that should use BlackSheepPrefab.")]
    [Range(0f, 100f)] public float diversity_percentage = 20f;
    [Tooltip("How strongly spawned sheep scale varies. At 1, sheep spawn between 80% and 120% of prefab size.")]
    [Range(0f, 1f)] public float size_variation = 1f;
    [Min(0)] public int total_sheep = 50;
    [Min(0f)] public float spawn_radius = 20f;
    [Tooltip("Small offset above the sampled ground/fence plane.")]
    [Min(0f)] public float groundOffset = 0.05f;
    [Tooltip("Maximum random placement attempts per sheep before giving up.")]
    [Min(1)] public int maxPlacementAttemptsPerSheep = 200;
    public bool spawnOnStart = true;
    public bool clearExistingChildren = true;

    private void Start()
    {
        if (spawnOnStart)
        {
            StartCoroutine(SpawnAfterFencesRoutine());
        }
    }

    [ContextMenu("Spawn Sheep")]
    public void SpawnSheep()
    {
        if (!ValidateSpawnInputs())
        {
            return;
        }

        if (!FenceManager.TryGetBoundaryWorldPoints(out List<Vector3> boundary) || boundary.Count < 3)
        {
            Debug.LogWarning("SheepManager: Could not get a valid fence boundary from FenceManager.", this);
            return;
        }

        if (clearExistingChildren)
        {
            ClearSheepParent();
        }

        List<GameObject> spawnOrder = BuildSpawnOrder();
        int spawned = 0;
        int maxAttempts = Mathf.Max(1, maxPlacementAttemptsPerSheep);

        for (int i = 0; i < spawnOrder.Count; i++)
        {
            if (TryFindSpawnPosition(boundary, maxAttempts, out Vector3 position))
            {
                Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                GameObject sheep = Instantiate(spawnOrder[i], position, rotation, SheepParentGameobject.transform);
                float scaleMultiplier = Random.Range(1f - 0.5f * size_variation, 1f + 0.5f * size_variation);
                sheep.transform.localScale *= scaleMultiplier;
                spawned++;
            }
            else
            {
                Debug.LogWarning($"SheepManager: Failed to place sheep {i + 1}/{spawnOrder.Count}. Increase spawn_radius or check the fence boundary.", this);
            }
        }

        if (spawned < total_sheep)
        {
            Debug.LogWarning($"SheepManager: Spawned {spawned}/{total_sheep} sheep.", this);
        }
    }

    private IEnumerator SpawnAfterFencesRoutine()
    {
        yield return null;

        while (FenceManager != null && FenceManager.IsGeneratingFences)
        {
            yield return null;
        }

        SpawnSheep();
    }

    private bool ValidateSpawnInputs()
    {
        if (SheepParentGameobject == null)
        {
            Debug.LogError("SheepManager: SheepParentGameobject is not assigned.", this);
            return false;
        }

        if (WhiteSheepPrefab == null)
        {
            Debug.LogError("SheepManager: WhiteSheepPrefab is not assigned.", this);
            return false;
        }

        if (BlackSheepPrefab == null)
        {
            Debug.LogError("SheepManager: BlackSheepPrefab is not assigned.", this);
            return false;
        }

        if (FenceManager == null)
        {
            Debug.LogError("SheepManager: FenceManager is not assigned.", this);
            return false;
        }

        return total_sheep > 0;
    }

    private List<GameObject> BuildSpawnOrder()
    {
        int blackSheepCount = Mathf.RoundToInt(total_sheep * Mathf.Clamp01(diversity_percentage / 100f));
        int whiteSheepCount = Mathf.Max(0, total_sheep - blackSheepCount);

        List<GameObject> prefabs = new List<GameObject>(total_sheep);
        for (int i = 0; i < blackSheepCount; i++)
        {
            prefabs.Add(BlackSheepPrefab);
        }

        for (int i = 0; i < whiteSheepCount; i++)
        {
            prefabs.Add(WhiteSheepPrefab);
        }

        for (int i = prefabs.Count - 1; i > 0; i--)
        {
            int swapIndex = Random.Range(0, i + 1);
            GameObject temp = prefabs[i];
            prefabs[i] = prefabs[swapIndex];
            prefabs[swapIndex] = temp;
        }

        return prefabs;
    }

    private bool TryFindSpawnPosition(List<Vector3> boundary, int maxAttempts, out Vector3 position)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            Vector2 planar = Random.insideUnitCircle * spawn_radius;
            Vector3 candidate = BuildWorldCandidate(planar);

            if (!IsInsideBoundary(candidate, boundary))
            {
                continue;
            }

            if (FenceManager.TryProjectWorldPointToSurface(candidate, out Vector3 projected))
            {
                candidate = projected;
            }

            position = candidate + GetVerticalDirection() * groundOffset;
            return true;
        }

        position = Vector3.zero;
        return false;
    }

    private Vector3 BuildWorldCandidate(Vector2 planar)
    {
        if (FenceManager.verticalAxis == global::FenceManager.VerticalAxis.Z)
        {
            return new Vector3(planar.x, planar.y, FenceManager.groundY);
        }

        return new Vector3(planar.x, FenceManager.groundY, planar.y);
    }

    private bool IsInsideBoundary(Vector3 point, List<Vector3> boundary)
    {
        Vector2 p = ToPlanar(point);
        bool inside = false;

        for (int i = 0, j = boundary.Count - 1; i < boundary.Count; j = i++)
        {
            Vector2 a = ToPlanar(boundary[i]);
            Vector2 b = ToPlanar(boundary[j]);

            if (((a.y > p.y) != (b.y > p.y)) &&
                (p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x))
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private Vector2 ToPlanar(Vector3 point)
    {
        if (FenceManager.verticalAxis == global::FenceManager.VerticalAxis.Z)
        {
            return new Vector2(point.x, point.y);
        }

        return new Vector2(point.x, point.z);
    }

    private Vector3 GetVerticalDirection()
    {
        return FenceManager.verticalAxis == global::FenceManager.VerticalAxis.Z ? Vector3.forward : Vector3.up;
    }

    private void ClearSheepParent()
    {
        Transform parent = SheepParentGameobject.transform;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Destroy(parent.GetChild(i).gameObject);
        }
    }
}
