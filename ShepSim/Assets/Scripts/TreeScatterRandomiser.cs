using UnityEngine;

public class TreeScatterRandomiser : MonoBehaviour
{
    [Header("Core Script Handler")]
    [SerializeField] private ProceduralTreeScatter treeScatterer;

    [Header("Tree Count (Sliders)")]
    [Range(0, 50)][SerializeField] private int minTreeCount = 15;
    [Range(0, 50)][SerializeField] private int maxTreeCount = 60;

    [Header("Tree Minimum Spacing Context (Sliders)")]
    [Tooltip("The minimum allowed distance dynamically rolled for a session.")]
    [Range(1f, 4f)][SerializeField] private float minAllowedSpacing = 1.5f;
    [Range(4f, 10f)][SerializeField] private float maxAllowedSpacing = 5.0f;

    [Header("Execution Mode")]
    [SerializeField] private bool randomiseOnStart = true;

    private void Reset()
    {
        if (treeScatterer == null) treeScatterer = GetComponent<ProceduralTreeScatter>();
    }

    private void Start()
    {
        if (randomiseOnStart)
        {
            Randomise();
        }
    }

    /// <summary>
    /// Decoupled orchestration loop. Evaluates runtime random selection targets based on slider positions.
    /// </summary>
    [ContextMenu("Randomise Scene")]
    public void Randomise()
    {
        if (treeScatterer == null)
        {
            Debug.LogError("TreeScatterRandomiser: ProceduralTreeScatter script reference is unassigned.");
            return;
        }

        // Randomly choose how many trees to spawn for this iteration
        int determinedTreeCount = Random.Range(minTreeCount, maxTreeCount + 1);

        // Fix: Roll a single minimum distance constraint boundary for this session loop
        float determinedMinSpacing = Random.Range(minAllowedSpacing, maxAllowedSpacing);

        // Populate vegetation fields instantly
        treeScatterer.GenerateTrees(determinedTreeCount, determinedMinSpacing);
    }
}