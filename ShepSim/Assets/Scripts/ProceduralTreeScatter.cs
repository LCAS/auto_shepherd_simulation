using UnityEngine;
using System.Collections.Generic;

public class ProceduralTreeScatter : MonoBehaviour
{
    [System.Serializable]
    public struct TreeBreed
    {
        public string breedName;
        public GameObject[] variants;
    }

    [Header("Asset Configuration")]
    [SerializeField] private List<TreeBreed> treeBreeds = new List<TreeBreed>();
    [SerializeField] private Transform treeContainer;

    [Header("Scatter Area Parameters")]
    [SerializeField] private Vector2 scatterAreaSize = new Vector2(80f, 80f);
    [SerializeField] private int maxAttempts = 3000;
    [SerializeField] private float raycastHeight = 150f;
    [SerializeField] private LayerMask groundLayer;

    private readonly List<GameObject> _spawnedTrees = new List<GameObject>();

    [ContextMenu("Clear Trees")]
    public void ClearTrees()
    {
        Transform container = treeContainer != null ? treeContainer : transform;
        for (int i = container.childCount - 1; i >= 0; i--)
        {
            DestroyImmediate(container.GetChild(i).gameObject);
        }
        _spawnedTrees.Clear();
    }

    /// <summary>
    /// Executes rejection-sampled tree placement, strictly picking one random breed variation pool per invocation.
    /// </summary>
    public void GenerateTrees(int targetCount, float minSpacing)
    {
        ClearTrees();

        if (treeBreeds == null || treeBreeds.Count == 0)
        {
            Debug.LogError("ProceduralTreeScatter: No tree breeds assigned in the Inspector.");
            return;
        }

        Transform container = treeContainer != null ? treeContainer : transform;
        float halfX = scatterAreaSize.x * 0.5f;
        float halfZ = scatterAreaSize.y * 0.5f;

        int sampleAttempts = 0;
        int itemsPlaced = 0;

        // Pick exactly one uniform breed variant list for this generation session
        int selectedBreedIndex = Random.Range(0, treeBreeds.Count);
        GameObject[] activeVariantPool = treeBreeds[selectedBreedIndex].variants;

        if (activeVariantPool == null || activeVariantPool.Length == 0)
        {
            Debug.LogError($"ProceduralTreeScatter: Selected breed '{treeBreeds[selectedBreedIndex].breedName}' has no variant prefabs.");
            return;
        }

        while (itemsPlaced < targetCount && sampleAttempts < maxAttempts)
        {
            sampleAttempts++;

            float localX = Random.Range(-halfX, halfX);
            float localZ = Random.Range(-halfZ, halfZ);
            Vector3 rayOrigin = transform.TransformPoint(new Vector3(localX, raycastHeight, localZ));

            if (IsTooCloseToNeighbours(rayOrigin, minSpacing))
            {
                continue;
            }

            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, raycastHeight * 2f, groundLayer))
            {
                // CRITICAL SIMPLIFICATION: Trees ALWAYS face perfectly up towards the sky
                Quaternion randomYaw = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                GameObject randomPrefabVariant = activeVariantPool[Random.Range(0, activeVariantPool.Length)];
                if (randomPrefabVariant != null)
                {
                    GameObject treeInstance = Instantiate(randomPrefabVariant, hit.point, randomYaw, container);
                    _spawnedTrees.Add(treeInstance);
                    itemsPlaced++;
                }
            }
        }
    }

    private bool IsTooCloseToNeighbours(Vector3 samplePos, float minSpacing)
    {
        float sqrMinSpacing = minSpacing * minSpacing;
        foreach (GameObject tree in _spawnedTrees)
        {
            if (tree == null) continue;

            float deltaX = tree.transform.position.x - samplePos.x;
            float deltaZ = tree.transform.position.z - samplePos.z;
            float sqrDistance = (deltaX * deltaX) + (deltaZ * deltaZ);

            if (sqrDistance < sqrMinSpacing)
            {
                return true;
            }
        }
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.0f, 0.7f, 0.3f, 1.0f);
        Vector3 sizeVector = new Vector3(scatterAreaSize.x, 2f, scatterAreaSize.y);
        Gizmos.DrawWireCube(transform.position, sizeVector);
    }
}