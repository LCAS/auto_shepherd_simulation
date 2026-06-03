using UnityEngine;

public class RandomlySwapMaterials : MonoBehaviour
{
    [SerializeField] private Material[] materials;
    [SerializeField] private bool randomiseOnStart = true;
    [SerializeField] private int materialIndex = 0;

    private Renderer _renderer;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
    }

    private void Start()
    {
        if (materials == null || materials.Length == 0 || _renderer == null)
            return;

        Material selected;

        if (randomiseOnStart)
        {
            selected = materials[Random.Range(0, materials.Length)];
        }
        else
        {
            materialIndex = Mathf.Clamp(materialIndex, 0, materials.Length - 1);
            selected = materials[materialIndex];
        }

        _renderer.material = selected;
    }
}

/*
I like this switching of material, but the materials look to be tiling with clear end points, is there a way to make it so that the texture file is more blended over the plane it is applied to?
*/