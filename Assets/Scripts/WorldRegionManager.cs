using UnityEngine;
using System.Collections.Generic;

namespace Terrain.Core
{
    /// <summary>
    /// Defines the overall biome type for a Voronoi region
    /// </summary>
    public enum RegionType
    {
        Temperate,  // Grasslands, forests, lakes
        Arid,       // Desert, savanna, oasis
        Cold,       // Tundra, snowy mountains, frozen lakes
        Tropical,   // Jungle, swamp, beaches
        Volcanic    // Lava fields, ash plains, obsidian formations
    }

    /// <summary>
    /// Sub-biome within a region
    /// </summary>
    [System.Serializable]
    public class SubBiome
    {
        public string name;
        [Range(0f, 1f)]
        public float weight = 1f;  // Relative weight for this sub-biome
        public Color primaryColor;
        public Color secondaryColor;
        [Range(0f, 2f)]
        public float heightMultiplier = 1f;  // How terrain height is modified
        [Range(0f, 1f)]
        public float treeSpawnChance = 0.5f;
        public bool allowWater = true;
    }

    /// <summary>
    /// Configuration for a region type
    /// </summary>
    [System.Serializable]
    public class RegionConfig
    {
        public RegionType regionType;
        public List<SubBiome> subBiomes = new List<SubBiome>();
        
        [Header("Terrain Modifiers")]
        [Range(0f, 2f)]
        public float baseHeightScale = 1f;
        [Range(0f, 1f)]
        public float mountainFrequency = 0.5f;
        [Range(0f, 1f)]
        public float waterLevel = 0.4f;
        
        public float TotalWeight
        {
            get
            {
                float total = 0f;
                foreach (var sb in subBiomes) total += sb.weight;
                return total;
            }
        }
    }

    /// <summary>
    /// A single Voronoi region/cell in the world
    /// </summary>
    public class WorldRegion
    {
        public int id;
        public Vector2 seedPoint;       // Center of this Voronoi cell
        public RegionType regionType;
        public RegionConfig config;
        public Color debugColor;        // For visualization
        
        public WorldRegion(int id, Vector2 seed, RegionType type)
        {
            this.id = id;
            this.seedPoint = seed;
            this.regionType = type;
            
            // Generate a debug color based on region type
            switch (type)
            {
                case RegionType.Temperate: debugColor = new Color(0.2f, 0.7f, 0.3f); break;
                case RegionType.Arid: debugColor = new Color(0.9f, 0.8f, 0.4f); break;
                case RegionType.Cold: debugColor = new Color(0.8f, 0.9f, 1f); break;
                case RegionType.Tropical: debugColor = new Color(0.1f, 0.5f, 0.2f); break;
                case RegionType.Volcanic: debugColor = new Color(0.6f, 0.2f, 0.1f); break;
            }
        }
    }

    /// <summary>
    /// Result of querying a world position
    /// </summary>
    public struct RegionQuery
    {
        public WorldRegion primaryRegion;      // The region this point is in
        public WorldRegion secondaryRegion;    // Nearest neighboring region (for blending)
        public float blendFactor;              // 0 = fully primary, 1 = fully secondary
        public SubBiome subBiome;              // The specific sub-biome at this point
        public float distanceToEdge;           // Distance to nearest region edge
    }

    /// <summary>
    /// Manages the world's Voronoi-based region system
    /// </summary>
    public class WorldRegionManager : MonoBehaviour
    {
        [Header("World Settings")]
        [Tooltip("Total world size (will be centered at origin)")]
        [SerializeField] private float worldSize = 10000f;
        
        [Tooltip("Number of regions to generate")]
        [SerializeField] private int regionCount = 7;
        
        [Tooltip("Seed for deterministic generation")]
        [SerializeField] private int worldSeed = 12345;
        
        [Header("Blending")]
        [Tooltip("Width of the blend zone between regions")]
        [SerializeField] private float blendDistance = 200f;
        
        [Tooltip("Noise scale for distorting region boundaries")]
        [SerializeField] private float boundaryNoiseScale = 0.001f;
        
        [Tooltip("How much noise distorts the boundaries")]
        [SerializeField] private float boundaryNoiseStrength = 300f;
        
        [Header("Region Configurations")]
        [SerializeField] private List<RegionConfig> regionConfigs = new List<RegionConfig>();
        
        // Runtime data
        private List<WorldRegion> regions = new List<WorldRegion>();
        private System.Random rng;
        private bool isInitialized = false;
        
        // Singleton for easy access
        public static WorldRegionManager Instance { get; private set; }
        
        public float WorldSize => worldSize;
        public List<WorldRegion> Regions => regions;
        public bool IsInitialized => isInitialized;

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                Initialize();
            }
            else
            {
                Destroy(gameObject);
            }
        }

        void Initialize()
        {
            if (isInitialized) return;
            
            rng = new System.Random(worldSeed);
            
            // Create default region configs if none defined
            if (regionConfigs.Count == 0)
            {
                CreateDefaultRegionConfigs();
            }
            
            // Generate Voronoi seed points
            GenerateRegions();
            
            isInitialized = true;
            Debug.Log($"WorldRegionManager initialized with {regions.Count} regions");
        }

        void CreateDefaultRegionConfigs()
        {
            // Temperate Region
            var temperate = new RegionConfig
            {
                regionType = RegionType.Temperate,
                baseHeightScale = 1f,
                mountainFrequency = 0.4f,
                waterLevel = 0.45f,
                subBiomes = new List<SubBiome>
                {
                    new SubBiome { name = "Grasslands", weight = 0.5f, primaryColor = new Color(0.4f, 0.65f, 0.2f), secondaryColor = new Color(0.35f, 0.55f, 0.15f), heightMultiplier = 0.8f, treeSpawnChance = 0.3f },
                    new SubBiome { name = "Forest", weight = 0.35f, primaryColor = new Color(0.2f, 0.45f, 0.15f), secondaryColor = new Color(0.15f, 0.35f, 0.1f), heightMultiplier = 1f, treeSpawnChance = 0.8f },
                    new SubBiome { name = "Lakes", weight = 0.15f, primaryColor = new Color(0.2f, 0.4f, 0.7f), secondaryColor = new Color(0.15f, 0.3f, 0.6f), heightMultiplier = 0.3f, treeSpawnChance = 0.1f, allowWater = true }
                }
            };
            
            // Arid Region
            var arid = new RegionConfig
            {
                regionType = RegionType.Arid,
                baseHeightScale = 0.7f,
                mountainFrequency = 0.2f,
                waterLevel = 0.2f,
                subBiomes = new List<SubBiome>
                {
                    new SubBiome { name = "Desert", weight = 0.6f, primaryColor = new Color(0.9f, 0.8f, 0.5f), secondaryColor = new Color(0.85f, 0.7f, 0.4f), heightMultiplier = 0.5f, treeSpawnChance = 0.02f, allowWater = false },
                    new SubBiome { name = "Savanna", weight = 0.3f, primaryColor = new Color(0.75f, 0.7f, 0.4f), secondaryColor = new Color(0.65f, 0.6f, 0.3f), heightMultiplier = 0.6f, treeSpawnChance = 0.15f },
                    new SubBiome { name = "Oasis", weight = 0.1f, primaryColor = new Color(0.3f, 0.5f, 0.3f), secondaryColor = new Color(0.2f, 0.5f, 0.6f), heightMultiplier = 0.4f, treeSpawnChance = 0.6f, allowWater = true }
                }
            };
            
            // Cold Region
            var cold = new RegionConfig
            {
                regionType = RegionType.Cold,
                baseHeightScale = 1.3f,
                mountainFrequency = 0.7f,
                waterLevel = 0.35f,
                subBiomes = new List<SubBiome>
                {
                    new SubBiome { name = "Tundra", weight = 0.45f, primaryColor = new Color(0.7f, 0.75f, 0.7f), secondaryColor = new Color(0.6f, 0.65f, 0.6f), heightMultiplier = 0.7f, treeSpawnChance = 0.05f },
                    new SubBiome { name = "SnowyMountains", weight = 0.4f, primaryColor = new Color(0.95f, 0.97f, 1f), secondaryColor = new Color(0.85f, 0.88f, 0.92f), heightMultiplier = 1.5f, treeSpawnChance = 0.1f },
                    new SubBiome { name = "FrozenLakes", weight = 0.15f, primaryColor = new Color(0.7f, 0.85f, 0.95f), secondaryColor = new Color(0.6f, 0.75f, 0.9f), heightMultiplier = 0.2f, treeSpawnChance = 0f, allowWater = true }
                }
            };
            
            // Tropical Region
            var tropical = new RegionConfig
            {
                regionType = RegionType.Tropical,
                baseHeightScale = 0.9f,
                mountainFrequency = 0.3f,
                waterLevel = 0.5f,
                subBiomes = new List<SubBiome>
                {
                    new SubBiome { name = "Jungle", weight = 0.5f, primaryColor = new Color(0.1f, 0.4f, 0.15f), secondaryColor = new Color(0.05f, 0.3f, 0.1f), heightMultiplier = 1f, treeSpawnChance = 0.9f },
                    new SubBiome { name = "Swamp", weight = 0.3f, primaryColor = new Color(0.25f, 0.35f, 0.2f), secondaryColor = new Color(0.3f, 0.35f, 0.25f), heightMultiplier = 0.3f, treeSpawnChance = 0.4f, allowWater = true },
                    new SubBiome { name = "Beach", weight = 0.2f, primaryColor = new Color(0.95f, 0.9f, 0.7f), secondaryColor = new Color(0.9f, 0.85f, 0.6f), heightMultiplier = 0.2f, treeSpawnChance = 0.2f }
                }
            };
            
            // Volcanic Region
            var volcanic = new RegionConfig
            {
                regionType = RegionType.Volcanic,
                baseHeightScale = 1.5f,
                mountainFrequency = 0.8f,
                waterLevel = 0.1f,
                subBiomes = new List<SubBiome>
                {
                    new SubBiome { name = "LavaFields", weight = 0.4f, primaryColor = new Color(0.3f, 0.1f, 0.05f), secondaryColor = new Color(0.8f, 0.3f, 0.1f), heightMultiplier = 0.6f, treeSpawnChance = 0f, allowWater = false },
                    new SubBiome { name = "AshPlains", weight = 0.4f, primaryColor = new Color(0.3f, 0.3f, 0.32f), secondaryColor = new Color(0.25f, 0.25f, 0.27f), heightMultiplier = 0.8f, treeSpawnChance = 0.02f },
                    new SubBiome { name = "ObsidianFormations", weight = 0.2f, primaryColor = new Color(0.1f, 0.1f, 0.12f), secondaryColor = new Color(0.15f, 0.1f, 0.2f), heightMultiplier = 1.8f, treeSpawnChance = 0f }
                }
            };
            
            regionConfigs.Add(temperate);
            regionConfigs.Add(arid);
            regionConfigs.Add(cold);
            regionConfigs.Add(tropical);
            regionConfigs.Add(volcanic);
        }

        void GenerateRegions()
        {
            regions.Clear();
            
            float halfWorld = worldSize / 2f;
            
            // Generate seed points using Poisson-like distribution for better spacing
            List<Vector2> seedPoints = GeneratePoissonPoints(regionCount, worldSize * 0.15f);
            
            // Assign region types - try to distribute evenly but with some randomness
            List<RegionType> availableTypes = new List<RegionType>();
            foreach (var config in regionConfigs)
            {
                availableTypes.Add(config.regionType);
            }
            
            for (int i = 0; i < seedPoints.Count; i++)
            {
                // Pick a region type
                RegionType type;
                if (availableTypes.Count > 0 && i < availableTypes.Count)
                {
                    // First pass: ensure each type is used at least once
                    int typeIndex = rng.Next(availableTypes.Count);
                    type = availableTypes[typeIndex];
                    availableTypes.RemoveAt(typeIndex);
                }
                else
                {
                    // Random type from configs
                    type = regionConfigs[rng.Next(regionConfigs.Count)].regionType;
                }
                
                var region = new WorldRegion(i, seedPoints[i], type);
                region.config = GetConfigForType(type);
                regions.Add(region);
                
                Debug.Log($"Region {i}: {type} at {seedPoints[i]}");
            }
        }

        List<Vector2> GeneratePoissonPoints(int count, float minDistance)
        {
            List<Vector2> points = new List<Vector2>();
            float halfWorld = worldSize / 2f;
            int maxAttempts = 1000;
            
            for (int i = 0; i < count && maxAttempts > 0; maxAttempts--)
            {
                Vector2 candidate = new Vector2(
                    (float)(rng.NextDouble() * worldSize - halfWorld),
                    (float)(rng.NextDouble() * worldSize - halfWorld)
                );
                
                bool valid = true;
                foreach (var existing in points)
                {
                    if (Vector2.Distance(candidate, existing) < minDistance)
                    {
                        valid = false;
                        break;
                    }
                }
                
                if (valid)
                {
                    points.Add(candidate);
                    i++;
                }
            }
            
            return points;
        }

        RegionConfig GetConfigForType(RegionType type)
        {
            foreach (var config in regionConfigs)
            {
                if (config.regionType == type)
                    return config;
            }
            return regionConfigs[0]; // Fallback
        }

        /// <summary>
        /// Query the region information at a world position
        /// </summary>
        public RegionQuery QueryPosition(float worldX, float worldZ)
        {
            if (!isInitialized) Initialize();
            
            RegionQuery result = new RegionQuery();
            
            // Apply boundary noise distortion
            float noiseX = Mathf.PerlinNoise(worldX * boundaryNoiseScale + 1000f, worldZ * boundaryNoiseScale + 1000f);
            float noiseZ = Mathf.PerlinNoise(worldX * boundaryNoiseScale + 2000f, worldZ * boundaryNoiseScale + 2000f);
            
            float distortedX = worldX + (noiseX - 0.5f) * 2f * boundaryNoiseStrength;
            float distortedZ = worldZ + (noiseZ - 0.5f) * 2f * boundaryNoiseStrength;
            
            Vector2 queryPoint = new Vector2(distortedX, distortedZ);
            
            // Find two closest regions (for blending)
            float closestDist = float.MaxValue;
            float secondClosestDist = float.MaxValue;
            WorldRegion closest = null;
            WorldRegion secondClosest = null;
            
            foreach (var region in regions)
            {
                float dist = Vector2.Distance(queryPoint, region.seedPoint);
                
                if (dist < closestDist)
                {
                    secondClosestDist = closestDist;
                    secondClosest = closest;
                    closestDist = dist;
                    closest = region;
                }
                else if (dist < secondClosestDist)
                {
                    secondClosestDist = dist;
                    secondClosest = region;
                }
            }
            
            result.primaryRegion = closest;
            result.secondaryRegion = secondClosest;
            
            // Calculate blend factor based on distance to edge
            // Edge is approximated as midpoint between two closest seeds
            if (secondClosest != null)
            {
                float edgeDist = (secondClosestDist - closestDist) / 2f;
                result.distanceToEdge = edgeDist;
                
                if (edgeDist < blendDistance)
                {
                    result.blendFactor = 1f - (edgeDist / blendDistance);
                    result.blendFactor = Mathf.SmoothStep(0f, 1f, result.blendFactor);
                }
                else
                {
                    result.blendFactor = 0f;
                }
            }
            
            // Determine sub-biome using noise
            result.subBiome = GetSubBiomeAtPosition(worldX, worldZ, closest.config);
            
            return result;
        }

        SubBiome GetSubBiomeAtPosition(float worldX, float worldZ, RegionConfig config)
        {
            if (config.subBiomes.Count == 0) return null;
            if (config.subBiomes.Count == 1) return config.subBiomes[0];
            
            // Use noise to determine sub-biome
            float noise = Mathf.PerlinNoise(worldX * 0.002f + 5000f, worldZ * 0.002f + 5000f);
            
            // Map noise to weighted sub-biome selection
            float totalWeight = config.TotalWeight;
            float threshold = noise * totalWeight;
            
            float cumulative = 0f;
            foreach (var subBiome in config.subBiomes)
            {
                cumulative += subBiome.weight;
                if (threshold <= cumulative)
                    return subBiome;
            }
            
            return config.subBiomes[config.subBiomes.Count - 1];
        }

        /// <summary>
        /// Get blended color at a position considering region blending
        /// </summary>
        public Color GetBlendedColorAtPosition(float worldX, float worldZ, float normalizedHeight)
        {
            RegionQuery query = QueryPosition(worldX, worldZ);
            
            Color primaryColor = GetColorForHeight(query.subBiome, normalizedHeight);
            
            if (query.blendFactor > 0.01f && query.secondaryRegion != null)
            {
                SubBiome secondarySubBiome = GetSubBiomeAtPosition(worldX, worldZ, query.secondaryRegion.config);
                Color secondaryColor = GetColorForHeight(secondarySubBiome, normalizedHeight);
                
                return Color.Lerp(primaryColor, secondaryColor, query.blendFactor);
            }
            
            return primaryColor;
        }

        Color GetColorForHeight(SubBiome subBiome, float normalizedHeight)
        {
            if (subBiome == null) return Color.magenta; // Error color
            
            // Blend between primary and secondary based on height
            return Color.Lerp(subBiome.primaryColor, subBiome.secondaryColor, normalizedHeight);
        }

        /// <summary>
        /// Check if trees should spawn at this position
        /// </summary>
        public bool ShouldSpawnTree(float worldX, float worldZ)
        {
            RegionQuery query = QueryPosition(worldX, worldZ);
            
            if (query.subBiome == null) return false;
            
            // Use noise for variation
            float noise = Mathf.PerlinNoise(worldX * 0.05f + 3000f, worldZ * 0.05f + 3000f);
            
            return noise < query.subBiome.treeSpawnChance;
        }

        /// <summary>
        /// Get height modifier at this position
        /// </summary>
        public float GetHeightModifier(float worldX, float worldZ)
        {
            RegionQuery query = QueryPosition(worldX, worldZ);
            
            float primaryMod = query.primaryRegion.config.baseHeightScale;
            if (query.subBiome != null)
                primaryMod *= query.subBiome.heightMultiplier;
            
            if (query.blendFactor > 0.01f && query.secondaryRegion != null)
            {
                float secondaryMod = query.secondaryRegion.config.baseHeightScale;
                SubBiome secondarySub = GetSubBiomeAtPosition(worldX, worldZ, query.secondaryRegion.config);
                if (secondarySub != null)
                    secondaryMod *= secondarySub.heightMultiplier;
                
                return Mathf.Lerp(primaryMod, secondaryMod, query.blendFactor);
            }
            
            return primaryMod;
        }

        // Debug visualization
        void OnDrawGizmosSelected()
        {
            if (regions == null || regions.Count == 0) return;
            
            foreach (var region in regions)
            {
                Gizmos.color = region.debugColor;
                Vector3 pos = new Vector3(region.seedPoint.x, 100f, region.seedPoint.y);
                Gizmos.DrawWireSphere(pos, 200f);
                
                // Draw label would need Handles, skip for now
            }
            
            // Draw world bounds
            Gizmos.color = Color.white;
            float half = worldSize / 2f;
            Gizmos.DrawWireCube(new Vector3(0, 0, 0), new Vector3(worldSize, 100f, worldSize));
        }
    }
}