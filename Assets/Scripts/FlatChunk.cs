using UnityEngine;
using System.Threading.Tasks;
using System.Collections;
using System.Collections.Generic;
using Terrain.Core;
using Terrain.Generators;

public class FlatChunk : MonoBehaviour
{
    private Mesh mesh;
    private MeshFilter meshFilter;
    private MeshRenderer meshRenderer;
    private MeshCollider meshCollider;
    private Bounds chunkBounds;

    private int currentGenerationId = 0;
    private LODLevel currentLOD;
    private Vector2Int chunkCoord;

    private bool pendingColliderBake = false;
    private Coroutine colliderCoroutine;
    
    private MeshData pendingMeshData;
    private LODLevel pendingLOD;
    private bool hasPendingMesh = false;
    
    private TreeSettings treeSettings;
    private float chunkWorldSize;
    private float heightMultiplier;
    private List<Matrix4x4> treeMatrices = new List<Matrix4x4>();
    private Matrix4x4[] treeBatchArray;
    private MaterialPropertyBlock treePropertyBlock;
    private bool hasTreeData = false;

    public LODLevel GetCurrentLOD() => currentLOD;
    public Vector2Int GetCoord() => chunkCoord;
    public bool HasPendingMesh => hasPendingMesh;
    public bool HasTrees => hasTreeData;

    public void Initialize(Vector2Int coord, int size, float scale, Material material, LODLevel lodLevel,
        TreeSettings trees = null, float heightMult = 1f)
    {
        this.chunkCoord = coord;
        this.currentLOD = lodLevel;
        this.treeSettings = trees;
        this.heightMultiplier = heightMult;

        meshFilter = gameObject.AddComponent<MeshFilter>();
        meshRenderer = gameObject.AddComponent<MeshRenderer>();
        
        if (material != null) meshRenderer.sharedMaterial = material;

        chunkWorldSize = (size - 1) * scale;
        transform.position = new Vector3(coord.x * chunkWorldSize, 0, coord.y * chunkWorldSize);

        chunkBounds = new Bounds(Vector3.zero, Vector3.one * chunkWorldSize);
        treePropertyBlock = new MaterialPropertyBlock();
    }

    public void ClearForPooling()
    {
        if (colliderCoroutine != null)
        {
            StopCoroutine(colliderCoroutine);
            colliderCoroutine = null;
        }
        
        if (mesh != null) mesh.Clear();
        
        if (meshCollider != null)
        {
            meshCollider.sharedMesh = null;
            Destroy(meshCollider);
            meshCollider = null;
        }
        
        ClearTrees();
        pendingColliderBake = false;
        hasPendingMesh = false;
        pendingMeshData = null;
        currentGenerationId++;
    }

    public async Task UpdateChunkAsync(int size, float scale, float heightMultiplier, float noiseScale, 
        int octaves, float persistence, float lacunarity, float offsetX, float offsetY, 
        TerrainColorSettings colorSettings, LODLevel lodLevel, float variationStrength)
    {
        int generationId = ++currentGenerationId;
        Vector3 chunkOffset = transform.position;
        Color[] colorMap = BakeColorSettings(colorSettings);

        MeshData result = await Task.Run(() => 
        {
            return MeshGenerator.Generate(size, scale, heightMultiplier, noiseScale, octaves, 
                persistence, lacunarity, offsetX, offsetY, chunkOffset, lodLevel, variationStrength, colorMap);
        });

        if (generationId == currentGenerationId && this != null && gameObject.activeInHierarchy)
        {
            pendingMeshData = result;
            pendingLOD = lodLevel;
            hasPendingMesh = true;
        }
    }

    public void ApplyPendingMesh()
    {
        if (!hasPendingMesh || pendingMeshData == null) return;
        
        hasPendingMesh = false;
        currentLOD = pendingLOD;
        ApplyMesh(pendingMeshData, pendingLOD);
        
        if (pendingLOD == LODLevel.High && treeSettings != null && treeSettings.treePrefab != null)
            GenerateTreeData(pendingMeshData);
        else
            ClearTrees();
        
        pendingMeshData = null;
    }
    
    private void GenerateTreeData(MeshData meshData)
    {
        treeMatrices.Clear();
        hasTreeData = false;
        
        if (treeSettings == null || treeSettings.treePrefab == null) return;
        
        System.Random rng = new System.Random(chunkCoord.x * 10000 + chunkCoord.y + treeSettings.seed);
        int attempts = Mathf.RoundToInt(chunkWorldSize * chunkWorldSize * treeSettings.density * 0.001f);
        
        WorldRegionManager regionManager = WorldRegionManager.Instance;
        
        for (int i = 0; i < attempts && treeMatrices.Count < treeSettings.maxTreesPerChunk; i++)
        {
            float localX = (float)rng.NextDouble() * chunkWorldSize;
            float localZ = (float)rng.NextDouble() * chunkWorldSize;
            
            float worldX = transform.position.x + localX;
            float worldZ = transform.position.z + localZ;
            
            if (!IsValidBiomeForTrees(worldX, worldZ, regionManager))
                continue;
            
            float height = SampleHeightFromMesh(meshData, localX, localZ);
            float normalizedHeight = height / heightMultiplier;
            
            if (normalizedHeight >= treeSettings.minHeight && normalizedHeight <= treeSettings.maxHeight)
            {
                float noiseVal = Mathf.PerlinNoise(worldX * 0.02f + treeSettings.seed, worldZ * 0.02f);
                
                if (noiseVal > treeSettings.spawnThreshold)
                {
                    Vector3 worldPos = new Vector3(worldX, height, worldZ);
                    float randomRotation = (float)rng.NextDouble() * 360f;
                    Quaternion rotation = Quaternion.Euler(0, randomRotation, 0);
                    float scaleVar = 1f + ((float)rng.NextDouble() - 0.5f) * treeSettings.scaleVariation;
                    Vector3 scale = Vector3.one * treeSettings.baseScale * scaleVar;
                    
                    treeMatrices.Add(Matrix4x4.TRS(worldPos, rotation, scale));
                }
            }
        }
        
        hasTreeData = treeMatrices.Count > 0;
        if (hasTreeData)
            treeBatchArray = new Matrix4x4[Mathf.Min(1023, treeMatrices.Count)];
    }
    
    private bool IsValidBiomeForTrees(float worldX, float worldZ, WorldRegionManager regionManager)
    {
        if (regionManager != null && regionManager.IsInitialized)
            return regionManager.ShouldSpawnTree(worldX, worldZ);
        
        // Fallback
        float noiseScale = 0.04f;
        float temperatureNoise = Mathf.PerlinNoise(worldX * noiseScale * 0.008f + 21000f, worldZ * noiseScale * 0.008f + 21000f);
        float moistureNoise = Mathf.PerlinNoise(worldX * noiseScale * 0.01f + 22000f, worldZ * noiseScale * 0.01f + 22000f);
        
        float desertWeight = Mathf.Clamp01((temperatureNoise - 0.55f) * 3f) * Mathf.Clamp01((0.45f - moistureNoise) * 3f);
        if (desertWeight > 0.3f) return false;
        
        float tundraWeight = Mathf.Clamp01((0.45f - temperatureNoise) * 3f);
        if (tundraWeight > 0.3f) return false;
        
        return true;
    }
    
    private float SampleHeightFromMesh(MeshData meshData, float localX, float localZ)
    {
        if (meshData.vertices == null || meshData.vertices.Length == 0) return 0f;
        
        float closestDist = float.MaxValue;
        float height = 0f;
        int step = Mathf.Max(1, meshData.vertices.Length / 500);
        
        for (int i = 0; i < meshData.vertices.Length; i += step)
        {
            Vector3 v = meshData.vertices[i];
            float dx = v.x - localX;
            float dz = v.z - localZ;
            float dist = dx * dx + dz * dz;
            
            if (dist < closestDist)
            {
                closestDist = dist;
                height = v.y;
            }
        }
        return height;
    }
    
    public void RenderTrees()
    {
        if (!hasTreeData || treeSettings == null || treeMatrices.Count == 0) return;
        
        Mesh treeMesh = treeSettings.GetTreeMesh();
        Material treeMaterial = treeSettings.GetTreeMaterial();
        if (treeMesh == null || treeMaterial == null) return;
        
        int batchSize = 1023;
        for (int i = 0; i < treeMatrices.Count; i += batchSize)
        {
            int count = Mathf.Min(batchSize, treeMatrices.Count - i);
            for (int j = 0; j < count; j++)
                treeBatchArray[j] = treeMatrices[i + j];
            
            Graphics.DrawMeshInstanced(treeMesh, 0, treeMaterial, treeBatchArray, count, treePropertyBlock,
                UnityEngine.Rendering.ShadowCastingMode.On, true);
        }
    }
    
    private void ClearTrees()
    {
        treeMatrices.Clear();
        hasTreeData = false;
        treeBatchArray = null;
    }

    public List<Matrix4x4> GetTreeMatrices() => treeMatrices;
    
    public void RemoveTree(int index)
    {
        if (index >= 0 && index < treeMatrices.Count)
        {
            treeMatrices.RemoveAt(index);
            hasTreeData = treeMatrices.Count > 0;
            treeBatchArray = hasTreeData ? new Matrix4x4[Mathf.Min(1023, treeMatrices.Count)] : null;
        }
    }
    
    public bool RemoveClosestTree(Vector3 worldPosition, float maxDistance = 5f)
    {
        if (!hasTreeData || treeMatrices.Count == 0) return false;
        
        int closestIndex = -1;
        float closestDist = float.MaxValue;
        
        for (int i = 0; i < treeMatrices.Count; i++)
        {
            Vector3 treePos = treeMatrices[i].GetColumn(3);
            float dist = Vector3.Distance(worldPosition, treePos);
            if (dist < closestDist && dist < maxDistance)
            {
                closestDist = dist;
                closestIndex = i;
            }
        }
        
        if (closestIndex >= 0)
        {
            RemoveTree(closestIndex);
            return true;
        }
        return false;
    }

    private void ApplyMesh(MeshData data, LODLevel lodLevel)
    {
        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }
        else mesh.Clear();

        mesh.vertices = data.vertices;
        mesh.triangles = data.triangles;
        mesh.normals = data.normals;
        mesh.colors = data.colors;
        
        mesh.RecalculateBounds();
        Bounds b = mesh.bounds;
        mesh.bounds = new Bounds(b.center, b.size * 1.2f);
        
        meshFilter.sharedMesh = mesh;
        chunkBounds = mesh.bounds;

        if (lodLevel == LODLevel.High)
        {
            if (meshCollider == null) 
                meshCollider = gameObject.AddComponent<MeshCollider>();
            
            if (colliderCoroutine != null) StopCoroutine(colliderCoroutine);
            pendingColliderBake = true;
            colliderCoroutine = StartCoroutine(DeferredColliderBake());
        }
        else if (meshCollider != null)
        {
            if (colliderCoroutine != null)
            {
                StopCoroutine(colliderCoroutine);
                colliderCoroutine = null;
            }
            pendingColliderBake = false;
            meshCollider.sharedMesh = null;
            Destroy(meshCollider);
            meshCollider = null;
        }
    }

    private IEnumerator DeferredColliderBake()
    {
        yield return null;
        yield return null;
        yield return null;
        yield return new WaitForSeconds(0.1f);
        
        if (pendingColliderBake && meshCollider != null && mesh != null && gameObject.activeInHierarchy)
        {
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = mesh;
            pendingColliderBake = false;
        }
        colliderCoroutine = null;
    }

    private Color[] BakeColorSettings(TerrainColorSettings settings)
    {
        if (settings == null) return new Color[] { Color.white };
        int resolution = 100;
        Color[] map = new Color[resolution];
        for (int i = 0; i < resolution; i++)
            map[i] = settings.GetColorForHeight(i / (float)(resolution - 1)); 
        return map;
    }

    public void SetVisible(bool visible) => gameObject.SetActive(visible);
    
    private void OnDestroy()
    {
        ClearTrees();
        if (mesh != null)
        {
            Destroy(mesh);
            mesh = null;
        }
    }
}

[System.Serializable]
public class TreeSettings
{
    [Header("Prefab & Materials")]
    public GameObject treePrefab;
    public Material treeMaterial;
    
    [Header("Spawn Settings")]
    [Range(0.1f, 10f)] public float density = 2f;
    [Range(10, 500)] public int maxTreesPerChunk = 100;
    [Range(0f, 1f)] public float minHeight = 0.52f;
    [Range(0f, 1f)] public float maxHeight = 0.8f;
    [Range(0f, 1f)] public float spawnThreshold = 0.4f;
    
    [Header("Variation")]
    public float baseScale = 5f;
    [Range(0f, 1f)] public float scaleVariation = 0.3f;
    public int seed = 42;
    
    private Mesh cachedMesh;
    private Material cachedMaterial;
    
    public Mesh GetTreeMesh()
    {
        if (cachedMesh == null && treePrefab != null)
        {
            MeshFilter mf = treePrefab.GetComponent<MeshFilter>();
            if (mf != null) cachedMesh = mf.sharedMesh;
        }
        return cachedMesh;
    }
    
    public Material GetTreeMaterial()
    {
        if (treeMaterial != null) return treeMaterial;
        if (cachedMaterial == null && treePrefab != null)
        {
            MeshRenderer mr = treePrefab.GetComponent<MeshRenderer>();
            if (mr != null) cachedMaterial = mr.sharedMaterial;
        }
        return cachedMaterial;
    }
}