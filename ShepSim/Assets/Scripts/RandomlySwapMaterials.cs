using UnityEngine;

public class RandomlySwapMaterials : MonoBehaviour
{
    [SerializeField] private Material[] materials;
    [SerializeField] private bool randomiseOnStart = true;
    [SerializeField] private int materialIndex = 0;
    public int materialOverrideIndex = -1;
    [SerializeField] private Vector2 textureTiling = new Vector2(50f, 50f);

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

        if (materialOverrideIndex >= 0)
        {
            selected = materials[Mathf.Clamp(materialOverrideIndex, 0, materials.Length - 1)];
        }
        else if (randomiseOnStart)
        {
            selected = materials[Random.Range(0, materials.Length)];
        }
        else
        {
            materialIndex = Mathf.Clamp(materialIndex, 0, materials.Length - 1);
            selected = materials[materialIndex];
        }

        Material materialInstance = _renderer.material = selected;
        ApplyTextureTiling(materialInstance);
    }

    private void ApplyTextureTiling(Material material)
    {
        if (material.HasProperty("_BaseMap"))
            material.SetTextureScale("_BaseMap", textureTiling);

        if (material.HasProperty("_MainTex"))
            material.SetTextureScale("_MainTex", textureTiling);
    }
}

/*
I like this switching of material, but the materials look to be tiling with clear end points, is there a way to make it so that the texture file is more blended over the plane it is applied to?
*/