using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using Terrain.Core;
using Terrain.Generators;

[System.Serializable]
public class LODSettings
{
    [Header("LOD Distances (in chunks)")]
    public int highLODDistance = 1;
    public int mediumLODDistance = 3;
    public int lowLODDistance = 4;
    
    [Header("LOD Chunk Sizes")]
    public int highLODSize = 121;
    public int mediumLODSize = 61;
    public int lowLODSize = 31;
}

public class FlatChunkManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform viewer;
    
    [Header("Settings")]
    [SerializeField] private LODSettings lodSettings;
    [SerializeField] private int maxRenderDistance = 3;
    [SerializeField] private float scale = 10f;
    
    [Header("Generation Params")]
    [SerializeField] private float heightMultiplier = 50f;
    [SerializeField] private float noiseScale = 0.01f;
    [SerializeField] private int octaves = 4;
    [SerializeField] private float persistence = 0.5f;
    [SerializeField] private float lacunarity = 2f;
    [SerializeField] private int seed = 0;
    
    [Header("Visuals")]
    [SerializeField] private Material chunkMaterial;
    [SerializeField] private TerrainColorSettings colorSettings;
    [SerializeField] private float colorVariationStrength = 0.15f;
    
    [Header("Trees")]
    [SerializeField] private TreeSettings treeSettings;

    [Header("Performance Tuning")]
    [Tooltip("Max chunk creations to START per frame")]
    [SerializeField] private int maxNewChunksPerFrame = 1;
    [Tooltip("Max mesh applications per frame (the expensive part)")]
    [SerializeField] private int maxMeshAppliesPerFrame = 1;
    [Tooltip("Max LOD updates to START per frame")]
    [SerializeField] private int maxLODUpdatesPerFrame = 1;
    
    // State
    private Dictionary<Vector2Int, FlatChunk> activeChunks = new Dictionary<Vector2Int, FlatChunk>();
    private Queue<Vector2Int> creationQueue = new Queue<Vector2Int>();
    private Queue<(FlatChunk chunk, LODLevel newLOD)> lodUpdateQueue = new Queue<(FlatChunk, LODLevel)>();
    private Vector2Int currentViewerChunk;
    private float offsetX, offsetY;
    private float chunkWorldSize;
    
    private List<FlatChunk> allChunks = new List<FlatChunk>();
    private WorldRegionManager regionManager;

    void Awake()
    {
        regionManager = FindFirstObjectByType<WorldRegionManager>();
    }

    void Start()
    {
        Random.InitState(seed);
        offsetX = Random.Range(0f, 10000f);
        offsetY = Random.Range(0f, 10000f);

        chunkWorldSize = (lodSettings.highLODSize - 1) * scale;

        if (viewer == null) viewer = Camera.main.transform;
        
        // Initialize region data cache for MeshGenerator (thread-safe)
        if (regionManager != null && regionManager.IsInitialized)
        {
            MeshGenerator.UpdateRegionDataCache(regionManager);
            Debug.Log("FlatChunkManager: Region data cache initialized");
        }
        else
        {
            Debug.Log("FlatChunkManager: No WorldRegionManager found, using fallback biome system");
        }
        
        currentViewerChunk = GetChunkCoordFromPosition(viewer.position);
        UpdateVisibleChunks();
        
        StartCoroutine(ProcessChunkQueue());
        StartCoroutine(ProcessLODQueue());
    }
    
    void LateUpdate()
    {
        foreach (var chunk in allChunks)
        {
            if (chunk != null && chunk.HasTrees)
                chunk.RenderTrees();
        }
    }

    void Update()
    {
        Vector2Int viewerChunk = GetChunkCoordFromPosition(viewer.position);
        
        if (viewerChunk != currentViewerChunk)
        {
            currentViewerChunk = viewerChunk;
            UpdateVisibleChunks();
        }
        
        ApplyPendingMeshes();
    }

    void ApplyPendingMeshes()
    {
        int applied = 0;
        
        allChunks.Sort((a, b) => {
            int distA = GetChebyshevDistance(a.GetCoord(), currentViewerChunk);
            int distB = GetChebyshevDistance(b.GetCoord(), currentViewerChunk);
            return distA.CompareTo(distB);
        });
        
        foreach (var chunk in allChunks)
        {
            if (applied >= maxMeshAppliesPerFrame) break;
            
            if (chunk != null && chunk.HasPendingMesh)
            {
                chunk.ApplyPendingMesh();
                applied++;
            }
        }
    }

    void UpdateVisibleChunks()
    {
        List<Vector2Int> toRemove = new List<Vector2Int>();
        foreach (var coord in activeChunks.Keys)
        {
            if (GetChebyshevDistance(coord, currentViewerChunk) > maxRenderDistance)
                toRemove.Add(coord);
        }

        foreach (var coord in toRemove)
        {
            FlatChunk chunk = activeChunks[coord];
            allChunks.Remove(chunk);
            Destroy(chunk.gameObject);
            activeChunks.Remove(coord);
        }

        creationQueue.Clear();
        lodUpdateQueue.Clear();
        
        for (int d = 0; d <= maxRenderDistance; d++)
        {
            for (int x = -d; x <= d; x++)
            {
                for (int y = -d; y <= d; y++)
                {
                    if (Mathf.Abs(x) != d && Mathf.Abs(y) != d) continue;

                    Vector2Int coord = new Vector2Int(currentViewerChunk.x + x, currentViewerChunk.y + y);
                    
                    if (!activeChunks.ContainsKey(coord))
                    {
                        creationQueue.Enqueue(coord);
                    }
                    else
                    {
                        FlatChunk chunk = activeChunks[coord];
                        int dist = GetChebyshevDistance(coord, currentViewerChunk);
                        LODLevel newLOD = GetLODLevel(dist);
                        
                        if (chunk.GetCurrentLOD() != newLOD)
                            lodUpdateQueue.Enqueue((chunk, newLOD));
                    }
                }
            }
        }
    }

    IEnumerator ProcessChunkQueue()
    {
        while (true)
        {
            int processed = 0;
            while (creationQueue.Count > 0 && processed < maxNewChunksPerFrame)
            {
                Vector2Int coord = creationQueue.Dequeue();
                if (!activeChunks.ContainsKey(coord))
                {
                    CreateChunk(coord);
                    processed++;
                }
            }
            yield return null;
        }
    }

    IEnumerator ProcessLODQueue()
    {
        while (true)
        {
            int processed = 0;
            while (lodUpdateQueue.Count > 0 && processed < maxLODUpdatesPerFrame)
            {
                var (chunk, newLOD) = lodUpdateQueue.Dequeue();
                
                if (chunk != null && chunk.gameObject.activeInHierarchy && chunk.GetCurrentLOD() != newLOD)
                {
                    UpdateChunkLOD(chunk, newLOD);
                    processed++;
                }
            }
            yield return null;
        }
    }

    void CreateChunk(Vector2Int coord)
    {
        GameObject chunkObject = new GameObject($"Chunk_{coord.x}_{coord.y}");
        chunkObject.transform.parent = transform;
        
        FlatChunk chunk = chunkObject.AddComponent<FlatChunk>();
        
        int dist = GetChebyshevDistance(coord, currentViewerChunk);
        LODLevel lodLevel = GetLODLevel(dist);
        int size = GetSize(lodLevel);
        float lodScale = chunkWorldSize / (size - 1);

        chunk.Initialize(coord, size, lodScale, chunkMaterial, lodLevel, treeSettings, heightMultiplier);
        activeChunks.Add(coord, chunk);
        allChunks.Add(chunk);

        _ = chunk.UpdateChunkAsync(
            size, lodScale, heightMultiplier, noiseScale, octaves, persistence, lacunarity, 
            offsetX, offsetY, colorSettings, lodLevel, colorVariationStrength
        );
    }

    void UpdateChunkLOD(FlatChunk chunk, LODLevel newLOD)
    {
        int size = GetSize(newLOD);
        float lodScale = chunkWorldSize / (size - 1);

        _ = chunk.UpdateChunkAsync(
            size, lodScale, heightMultiplier, noiseScale, octaves, persistence, lacunarity, 
            offsetX, offsetY, colorSettings, newLOD, colorVariationStrength
        );
    }

    int GetChebyshevDistance(Vector2Int a, Vector2Int b) =>
        Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));

    LODLevel GetLODLevel(int distance)
    {
        if (distance <= lodSettings.highLODDistance) return LODLevel.High;
        if (distance <= lodSettings.mediumLODDistance) return LODLevel.Medium;
        return LODLevel.Low;
    }

    int GetSize(LODLevel lod) => lod switch
    {
        LODLevel.High => lodSettings.highLODSize,
        LODLevel.Medium => lodSettings.mediumLODSize,
        _ => lodSettings.lowLODSize
    };

    Vector2Int GetChunkCoordFromPosition(Vector3 position)
    {
        int x = Mathf.FloorToInt(position.x / chunkWorldSize);
        int z = Mathf.FloorToInt(position.z / chunkWorldSize);
        return new Vector2Int(x, z);
    }
    
    public void RefreshRegionCache()
    {
        if (regionManager != null)
            MeshGenerator.UpdateRegionDataCache(regionManager);
    }
}