using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
public class PlaneUndulation : MonoBehaviour
{
    [Header("Execution")]
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private bool applyInAwake = true;
    [SerializeField] private bool randomizeOffsetOnApply = false;

    [Header("Mesh")]
    [SerializeField] private bool generateDenseGrid = true;
    [SerializeField, Min(8)] private int gridResolution = 128;
    [SerializeField] private Vector2 planeSize = new Vector2(80f, 80f);

    [Header("Global Shape")]
    [SerializeField] private float globalAmplitude = 1.5f;
    [SerializeField] private float globalFrequency = 0.03f;

    [Header("Local Detail")]
    [SerializeField] private float localAmplitude = 0.35f;
    [SerializeField] private float localFrequency = 0.22f;

    [Header("Noise")]
    [SerializeField] private Vector2 noiseOffset = new Vector2(100f, 100f);
    [SerializeField] private Vector2 randomOffsetRange = new Vector2(10000f, 10000f);

    private MeshFilter _meshFilter;
    private MeshCollider _meshCollider;
    private Vector3[] _originalVertices;
    private bool _hasApplied;

    private void Awake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshCollider = GetComponent<MeshCollider>();

        if (generateDenseGrid)
        {
            Mesh generated = BuildGridMesh(Mathf.Max(8, gridResolution), planeSize);
            generated.name = "ProceduralPlane";
            _meshFilter.sharedMesh = generated;
            if (_meshCollider != null)
            {
                _meshCollider.sharedMesh = generated;
            }
        }

        if (_meshFilter != null && _meshFilter.sharedMesh != null)
        {
            _originalVertices = _meshFilter.sharedMesh.vertices;
        }

        if (applyOnStart && applyInAwake)
        {
            ApplyUndulation();
        }
    }

    private void Start()
    {
        if (applyOnStart && (!applyInAwake || !_hasApplied))
        {
            ApplyUndulation();
        }
    }

    [ContextMenu("Apply Undulation")]
    public void ApplyUndulation()
    {
        if (_meshFilter == null || _meshFilter.sharedMesh == null || _originalVertices == null || _originalVertices.Length == 0)
        {
            return;
        }

        Mesh deformedMesh = _hasApplied ? _meshFilter.mesh : Instantiate(_meshFilter.sharedMesh);
        _hasApplied = true;
        deformedMesh.name = _meshFilter.sharedMesh.name + "_Undulated";

        if (randomizeOffsetOnApply)
        {
            noiseOffset = new Vector2(
                Random.Range(-randomOffsetRange.x, randomOffsetRange.x),
                Random.Range(-randomOffsetRange.y, randomOffsetRange.y)
            );
        }

        Vector3[] deformed = new Vector3[_originalVertices.Length];

        for (int i = 0; i < _originalVertices.Length; i++)
        {
            Vector3 v = _originalVertices[i];
            Vector3 world = transform.TransformPoint(v);

            float globalNoise = SampleSignedNoise(world.x, world.z, globalFrequency);
            float localNoise = SampleSignedNoise(world.x + 217.3f, world.z - 103.7f, localFrequency);
            v.y = globalNoise * globalAmplitude + localNoise * localAmplitude;
            deformed[i] = v;
        }

        deformedMesh.vertices = deformed;
        deformedMesh.RecalculateNormals();
        deformedMesh.RecalculateBounds();

        _meshFilter.mesh = deformedMesh;

        if (_meshCollider != null)
        {
            _meshCollider.sharedMesh = null;
            _meshCollider.sharedMesh = deformedMesh;
        }
    }

    [ContextMenu("Reset Mesh")]
    public void ResetMesh()
    {
        if (_meshFilter == null || _meshFilter.sharedMesh == null || _originalVertices == null || _originalVertices.Length == 0)
        {
            return;
        }

        Mesh resetMesh = _hasApplied ? _meshFilter.mesh : Instantiate(_meshFilter.sharedMesh);
        resetMesh.name = _meshFilter.sharedMesh.name + "_Reset";
        resetMesh.vertices = (Vector3[])_originalVertices.Clone();
        resetMesh.RecalculateNormals();
        resetMesh.RecalculateBounds();

        _meshFilter.mesh = resetMesh;

        if (_meshCollider != null)
        {
            _meshCollider.sharedMesh = null;
            _meshCollider.sharedMesh = resetMesh;
        }

        _hasApplied = false;
    }

    [ContextMenu("Randomize Offset And Apply")]
    public void RandomizeOffsetAndApply()
    {
        noiseOffset = new Vector2(
            Random.Range(-randomOffsetRange.x, randomOffsetRange.x),
            Random.Range(-randomOffsetRange.y, randomOffsetRange.y)
        );
        ApplyUndulation();
    }

    private float SampleSignedNoise(float x, float z, float frequency)
    {
        float sampleX = (x + noiseOffset.x) * Mathf.Max(0.0001f, frequency);
        float sampleZ = (z + noiseOffset.y) * Mathf.Max(0.0001f, frequency);
        return Mathf.PerlinNoise(sampleX, sampleZ) * 2f - 1f;
    }

    private static Mesh BuildGridMesh(int resolution, Vector2 size)
    {
        int vertsPerLine = resolution + 1;
        Vector3[] vertices = new Vector3[vertsPerLine * vertsPerLine];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[resolution * resolution * 6];

        float halfX = size.x * 0.5f;
        float halfZ = size.y * 0.5f;

        int vertIndex = 0;
        for (int z = 0; z <= resolution; z++)
        {
            float z01 = z / (float)resolution;
            float localZ = Mathf.Lerp(-halfZ, halfZ, z01);

            for (int x = 0; x <= resolution; x++)
            {
                float x01 = x / (float)resolution;
                float localX = Mathf.Lerp(-halfX, halfX, x01);
                vertices[vertIndex] = new Vector3(localX, 0f, localZ);
                uvs[vertIndex] = new Vector2(x01, z01);
                vertIndex++;
            }
        }

        int triIndex = 0;
        for (int z = 0; z < resolution; z++)
        {
            int rowStart = z * vertsPerLine;
            int nextRowStart = (z + 1) * vertsPerLine;

            for (int x = 0; x < resolution; x++)
            {
                int a = rowStart + x;
                int b = rowStart + x + 1;
                int c = nextRowStart + x;
                int d = nextRowStart + x + 1;

                triangles[triIndex++] = a;
                triangles[triIndex++] = c;
                triangles[triIndex++] = b;

                triangles[triIndex++] = b;
                triangles[triIndex++] = c;
                triangles[triIndex++] = d;
            }
        }

        Mesh mesh = new Mesh();
        mesh.indexFormat = vertices.Length > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}