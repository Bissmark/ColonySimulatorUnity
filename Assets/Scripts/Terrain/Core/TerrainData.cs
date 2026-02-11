using UnityEngine;

namespace Terrain.Core
{
    public enum LODLevel
    {
        High = 0,      // Flat shaded with collider
        Medium = 1,    // Indexed mesh
        Low = 2        // Far distance
    }

    public struct BiomeWeights
    {
        public float grassland;
        public float desert;
        public float tundra;
    }

    public struct BiomeSettings
    {
        public float baseFrequency;
        public float baseAmplitude;
        public float mountainThreshold;
        public float mountainMultiplier;
        public float floorHeight;
        public bool isDesert;
    }

    // Data Transfer Object
    public class MeshData
    {
        public Vector3[] vertices;
        public int[] triangles;
        public Vector3[] normals;
        public Color[] colors;
    }
    
    // Helper to keep the Chunk dictionary clean
    public class ChunkData
    {
        public MonoBehaviour chunkScript; // Generic reference to FlatChunk
        public LODLevel currentLOD;
        public float distanceFromViewer;
    }
}