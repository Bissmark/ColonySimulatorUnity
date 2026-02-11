using UnityEngine;
using Terrain.Core;
using System.Collections.Generic;

namespace Terrain.Generators
{
    /// <summary>
    /// Thread-safe region data snapshot for mesh generation
    /// </summary>
    public class RegionDataSnapshot
    {
        public List<RegionSeedData> regionSeeds = new List<RegionSeedData>();
        public float blendDistance;
        public float boundaryNoiseScale;
        public float boundaryNoiseStrength;
        public float worldSize;
        public bool isValid = false;
        
        public struct RegionSeedData
        {
            public int id;
            public Vector2 seedPoint;
            public RegionType regionType;
            public float baseHeightScale;
            public float mountainFrequency;
            public float waterLevel;
            public List<SubBiomeData> subBiomes;
        }
        
        public struct SubBiomeData
        {
            public string name;
            public float weight;
            public Color primaryColor;
            public Color secondaryColor;
            public float heightMultiplier;
            public float treeSpawnChance;
            public bool allowWater;
        }
    }

    public static class MeshGenerator
    {
        private static RegionDataSnapshot cachedRegionData;
        private static readonly object regionDataLock = new object();
        
        public static void UpdateRegionDataCache(WorldRegionManager regionManager)
        {
            if (regionManager == null || !regionManager.IsInitialized)
            {
                lock (regionDataLock) { cachedRegionData = null; }
                return;
            }
            
            var snapshot = new RegionDataSnapshot
            {
                blendDistance = 200f,
                boundaryNoiseScale = 0.001f,
                boundaryNoiseStrength = 300f,
                worldSize = regionManager.WorldSize,
                isValid = true
            };
            
            foreach (var region in regionManager.Regions)
            {
                var seedData = new RegionDataSnapshot.RegionSeedData
                {
                    id = region.id,
                    seedPoint = region.seedPoint,
                    regionType = region.regionType,
                    baseHeightScale = region.config.baseHeightScale,
                    mountainFrequency = region.config.mountainFrequency,
                    waterLevel = region.config.waterLevel,
                    subBiomes = new List<RegionDataSnapshot.SubBiomeData>()
                };
                
                foreach (var subBiome in region.config.subBiomes)
                {
                    seedData.subBiomes.Add(new RegionDataSnapshot.SubBiomeData
                    {
                        name = subBiome.name,
                        weight = subBiome.weight,
                        primaryColor = subBiome.primaryColor,
                        secondaryColor = subBiome.secondaryColor,
                        heightMultiplier = subBiome.heightMultiplier,
                        treeSpawnChance = subBiome.treeSpawnChance,
                        allowWater = subBiome.allowWater
                    });
                }
                
                snapshot.regionSeeds.Add(seedData);
            }
            
            lock (regionDataLock) { cachedRegionData = snapshot; }
        }
        
        public static MeshData Generate(
            int size, float scale, float heightMultiplier, float noiseScale, int octaves,
            float persistence, float lacunarity, float offsetX, float offsetY, 
            Vector3 chunkOffset, LODLevel lodLevel, float variationStrength, Color[] colorMap)
        {
            RegionDataSnapshot regionData;
            lock (regionDataLock) { regionData = cachedRegionData; }
            
            if (lodLevel == LODLevel.High)
                return GenerateFlatShaded(size, scale, heightMultiplier, noiseScale, octaves, persistence, lacunarity, offsetX, offsetY, chunkOffset, variationStrength, colorMap, regionData);
            else
                return GenerateIndexed(size, scale, heightMultiplier, noiseScale, octaves, persistence, lacunarity, offsetX, offsetY, chunkOffset, variationStrength, colorMap, regionData);
        }

        static MeshData GenerateFlatShaded(int size, float scale, float heightMultiplier, float noiseScale, int octaves, float persistence, float lacunarity, float offsetX, float offsetY, Vector3 chunkOffset, float variationStrength, Color[] colorMap, RegionDataSnapshot regionData)
        {
            // 1. Calculate Height Map First - ORIGINAL SMOOTH METHOD
            float[,] heightMap = new float[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float worldX = chunkOffset.x + x * scale;
                    float worldZ = chunkOffset.z + y * scale;
                    heightMap[x, y] = SampleNoise(worldX, worldZ, noiseScale, octaves, persistence, lacunarity, offsetX, offsetY);
                }
            }

            // 2. Build Triangles
            int quadCount = (size - 1) * (size - 1);
            int triangleCount = quadCount * 2;
            
            Vector3[] vertices = new Vector3[triangleCount * 3];
            Vector3[] normals = new Vector3[triangleCount * 3];
            Color[] colors = new Color[triangleCount * 3];
            int[] triangles = new int[triangleCount * 3];

            int vertIndex = 0;

            for (int y = 0; y < size - 1; y++)
            {
                for (int x = 0; x < size - 1; x++)
                {
                    Vector3 bl = new Vector3(x * scale, heightMap[x, y] * heightMultiplier, y * scale);
                    Vector3 br = new Vector3((x + 1) * scale, heightMap[x + 1, y] * heightMultiplier, y * scale);
                    Vector3 tl = new Vector3(x * scale, heightMap[x, y + 1] * heightMultiplier, (y + 1) * scale);
                    Vector3 tr = new Vector3((x + 1) * scale, heightMap[x + 1, y + 1] * heightMultiplier, (y + 1) * scale);

                    bool alt = (x + y) % 2 == 0;

                    if (alt) {
                        AddTriangle(bl, tl, tr, ref vertIndex, vertices, normals, colors, triangles, chunkOffset, noiseScale, offsetX, offsetY, heightMultiplier, variationStrength, colorMap, regionData);
                        AddTriangle(bl, tr, br, ref vertIndex, vertices, normals, colors, triangles, chunkOffset, noiseScale, offsetX, offsetY, heightMultiplier, variationStrength, colorMap, regionData);
                    } else {
                        AddTriangle(bl, tl, br, ref vertIndex, vertices, normals, colors, triangles, chunkOffset, noiseScale, offsetX, offsetY, heightMultiplier, variationStrength, colorMap, regionData);
                        AddTriangle(tl, tr, br, ref vertIndex, vertices, normals, colors, triangles, chunkOffset, noiseScale, offsetX, offsetY, heightMultiplier, variationStrength, colorMap, regionData);
                    }
                }
            }

            return new MeshData { vertices = vertices, triangles = triangles, normals = normals, colors = colors };
        }

        static MeshData GenerateIndexed(int size, float scale, float heightMultiplier, float noiseScale, int octaves, float persistence, float lacunarity, float offsetX, float offsetY, Vector3 chunkOffset, float variationStrength, Color[] colorMap, RegionDataSnapshot regionData)
        {
            int vertexCount = size * size;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector3[] normals = new Vector3[vertexCount];
            Color[] colors = new Color[vertexCount];

            // Generate heights using ORIGINAL smooth method
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    float worldX = chunkOffset.x + x * scale;
                    float worldZ = chunkOffset.z + y * scale;
                    float h = SampleNoise(worldX, worldZ, noiseScale, octaves, persistence, lacunarity, offsetX, offsetY);
                    vertices[i] = new Vector3(x * scale, h * heightMultiplier, y * scale);
                }
            }

            // Calculate normals and colors
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int index = y * size + x;
                    normals[index] = CalculateVertexNormal(vertices, x, y, size);
                    
                    Vector3 worldPos = chunkOffset + vertices[index];
                    float normalizedHeight = Mathf.InverseLerp(-1f * heightMultiplier, 1f * heightMultiplier, vertices[index].y);
                    
                    // Use region-based coloring if available, otherwise fallback
                    Color baseColor = GetColorForPosition(worldPos.x, worldPos.z, normalizedHeight, noiseScale, offsetX, offsetY, colorMap, regionData);
                    colors[index] = AddColorVariation(baseColor, worldPos, variationStrength);
                }
            }

            int quadCount = (size - 1) * (size - 1);
            int[] triangles = new int[quadCount * 6];
            int triIndex = 0;

            for (int y = 0; y < size - 1; y++)
            {
                for (int x = 0; x < size - 1; x++)
                {
                    int bl = y * size + x;
                    int br = y * size + x + 1;
                    int tl = (y + 1) * size + x;
                    int tr = (y + 1) * size + x + 1;

                    bool alt = (x + y) % 2 == 0;
                    if (alt) {
                        triangles[triIndex++] = bl; triangles[triIndex++] = tl; triangles[triIndex++] = tr;
                        triangles[triIndex++] = bl; triangles[triIndex++] = tr; triangles[triIndex++] = br;
                    } else {
                        triangles[triIndex++] = bl; triangles[triIndex++] = tl; triangles[triIndex++] = br;
                        triangles[triIndex++] = tl; triangles[triIndex++] = tr; triangles[triIndex++] = br;
                    }
                }
            }

            return new MeshData { vertices = vertices, triangles = triangles, normals = normals, colors = colors };
        }

        // --- ORIGINAL SMOOTH TERRAIN GENERATION ---
        
        static float SampleNoise(float x, float z, float noiseScale, int octaves, float persistence, float lacunarity, float offsetX, float offsetY)
        {   
            // ---------------------------------------------------------
            // 1. CONTINENT MASK
            // ---------------------------------------------------------
            float continentMask = NativePerlinNoise.Noise(x * noiseScale * 0.002f + offsetX + 2000f, z * noiseScale * 0.002f + offsetY + 2000f);
            float landInfluence = Mathf.SmoothStep(0.15f, 0.45f, continentMask);

            // ---------------------------------------------------------
            // 2. TERRAIN LAYERS
            // ---------------------------------------------------------
            
            // LAYER A: Rolling Hills
            float rollingHills = 0f;
            float hillAmp = 0.1f;
            for (int i = 0; i < 2; i++) {
                float sx = x * noiseScale * 0.06f * (i + 1) + offsetX;
                float sz = z * noiseScale * 0.06f * (i + 1) + offsetY;
                rollingHills += (NativePerlinNoise.Noise(sx, sz) - 0.5f) * 2f * hillAmp;
                hillAmp *= 0.5f;
            }

            // LAYER B: Medium Mountains
            float mediumMask = NativePerlinNoise.Noise(x * noiseScale * 0.015f + offsetX + 644f, z * noiseScale * 0.015f + offsetY + 644f);
            mediumMask = Mathf.SmoothStep(0.4f, 0.8f, mediumMask);
            
            float mediumMountains = 0f;
            float mAmp = 1.8f;
            float mFreq = 1.0f;
            for (int i = 0; i < octaves; i++) {
                float sx = x * noiseScale * 0.025f * mFreq + offsetX + 777f;
                float sz = z * noiseScale * 0.025f * mFreq + offsetY + 777f;
                mediumMountains += (NativePerlinNoise.Noise(sx, sz) - 0.5f) * 2f * mAmp;
                mAmp *= persistence;
                mFreq *= lacunarity;
            }

            // ---------------------------------------------------------
            // 3. MEGA PEAKS with smoothed ridges
            // ---------------------------------------------------------
            float megaMaskValue = NativePerlinNoise.Noise(x * noiseScale * 0.004f + offsetX + 999f, z * noiseScale * 0.004f + offsetY + 999f);
            float megaMask = Mathf.Pow(Mathf.Max(0, megaMaskValue - 0.45f) * 2.0f, 2.0f);
            
            // Smoothed ridge noise
            float ridgeNoise = NativePerlinNoise.Noise(x * noiseScale * 0.012f + offsetX, z * noiseScale * 0.012f + offsetY);
            float ridgeBase = 1f - Mathf.Abs(ridgeNoise * 2f - 1f);
            float v = Mathf.SmoothStep(0f, 1f, ridgeBase);
            float ridgeSharpness = 0.3f;
            v = Mathf.Lerp(v, ridgeBase, ridgeSharpness);
            
            float megaPeaks = Mathf.Pow(v, 2.0f) * 50.0f;

            // ---------------------------------------------------------
            // 4. FINAL COMPOSITION
            // ---------------------------------------------------------
            float landBase = rollingHills + (mediumMountains * mediumMask);
            float finalHeight = landBase + (megaPeaks * megaMask);

            float seaLevelOffset = -0.6f;
            finalHeight += (landInfluence * 3.0f) + seaLevelOffset;

            return Mathf.Max(finalHeight, -0.5f);
        }

        // --- COLORING (uses regions if available) ---
        
        static Color GetColorForPosition(float worldX, float worldZ, float normalizedHeight, float noiseScale, float offsetX, float offsetY, Color[] colorMap, RegionDataSnapshot regionData)
        {
            // Try region-based coloring first
            if (regionData != null && regionData.isValid && regionData.regionSeeds.Count > 0)
            {
                return GetRegionColor(worldX, worldZ, normalizedHeight, regionData);
            }
            
            // Fallback to original biome system
            BiomeWeights weights = GetBiomeWeights(worldX, worldZ, noiseScale, offsetX, offsetY);
            return GetBlendedColor(normalizedHeight, weights, colorMap);
        }
        
        static Color GetRegionColor(float worldX, float worldZ, float normalizedHeight, RegionDataSnapshot regionData)
        {
            // Apply boundary noise distortion
            float noiseX = NativePerlinNoise.Noise(worldX * regionData.boundaryNoiseScale + 1000f, worldZ * regionData.boundaryNoiseScale + 1000f);
            float noiseZ = NativePerlinNoise.Noise(worldX * regionData.boundaryNoiseScale + 2000f, worldZ * regionData.boundaryNoiseScale + 2000f);
            
            float distortedX = worldX + (noiseX - 0.5f) * 2f * regionData.boundaryNoiseStrength;
            float distortedZ = worldZ + (noiseZ - 0.5f) * 2f * regionData.boundaryNoiseStrength;
            
            Vector2 queryPoint = new Vector2(distortedX, distortedZ);
            
            // Find two closest regions
            float closestDist = float.MaxValue;
            float secondClosestDist = float.MaxValue;
            int closestIdx = -1;
            int secondClosestIdx = -1;
            
            for (int i = 0; i < regionData.regionSeeds.Count; i++)
            {
                float dist = Vector2.Distance(queryPoint, regionData.regionSeeds[i].seedPoint);
                
                if (dist < closestDist)
                {
                    secondClosestDist = closestDist;
                    secondClosestIdx = closestIdx;
                    closestDist = dist;
                    closestIdx = i;
                }
                else if (dist < secondClosestDist)
                {
                    secondClosestDist = dist;
                    secondClosestIdx = i;
                }
            }
            
            if (closestIdx < 0)
                return Color.Lerp(new Color(0.2f, 0.5f, 0.2f), new Color(0.5f, 0.5f, 0.5f), normalizedHeight);
            
            var primaryRegion = regionData.regionSeeds[closestIdx];
            var primarySubBiome = GetSubBiomeAtPosition(worldX, worldZ, primaryRegion);
            Color primaryColor = Color.Lerp(primarySubBiome.primaryColor, primarySubBiome.secondaryColor, normalizedHeight);
            
            // Calculate blend factor
            float blendFactor = 0f;
            if (secondClosestIdx >= 0)
            {
                float edgeDist = (secondClosestDist - closestDist) / 2f;
                if (edgeDist < regionData.blendDistance)
                {
                    blendFactor = 1f - (edgeDist / regionData.blendDistance);
                    blendFactor = blendFactor * blendFactor * (3f - 2f * blendFactor); // SmoothStep
                }
            }
            
            if (blendFactor > 0.01f && secondClosestIdx >= 0)
            {
                var secondaryRegion = regionData.regionSeeds[secondClosestIdx];
                var secondarySubBiome = GetSubBiomeAtPosition(worldX, worldZ, secondaryRegion);
                Color secondaryColor = Color.Lerp(secondarySubBiome.primaryColor, secondarySubBiome.secondaryColor, normalizedHeight);
                return Color.Lerp(primaryColor, secondaryColor, blendFactor);
            }
            
            return primaryColor;
        }
        
        static RegionDataSnapshot.SubBiomeData GetSubBiomeAtPosition(float worldX, float worldZ, RegionDataSnapshot.RegionSeedData region)
        {
            if (region.subBiomes == null || region.subBiomes.Count == 0)
            {
                return new RegionDataSnapshot.SubBiomeData 
                { 
                    primaryColor = Color.magenta, 
                    secondaryColor = Color.magenta,
                    heightMultiplier = 1f 
                };
            }
            
            if (region.subBiomes.Count == 1)
                return region.subBiomes[0];
            
            // Use noise to determine sub-biome
            float noise = NativePerlinNoise.Noise(worldX * 0.002f + 5000f, worldZ * 0.002f + 5000f);
            
            float totalWeight = 0f;
            foreach (var sb in region.subBiomes)
                totalWeight += sb.weight;
            
            float threshold = noise * totalWeight;
            float cumulative = 0f;
            
            foreach (var subBiome in region.subBiomes)
            {
                cumulative += subBiome.weight;
                if (threshold <= cumulative)
                    return subBiome;
            }
            
            return region.subBiomes[region.subBiomes.Count - 1];
        }

        // --- ORIGINAL BIOME SYSTEM (fallback) ---

        static BiomeWeights GetBiomeWeights(float x, float z, float noiseScale, float offsetX, float offsetY)
        {
            float temperatureNoise = NativePerlinNoise.Noise(x * noiseScale * 0.015f + offsetX + 21000f, z * noiseScale * 0.015f + offsetY + 21000f);
            float moistureNoise = NativePerlinNoise.Noise(x * noiseScale * 0.018f + offsetX + 22000f, z * noiseScale * 0.018f + offsetY + 22000f);
            
            float desertWeight = Mathf.Clamp01((temperatureNoise - 0.5f) * 4f) * Mathf.Clamp01((0.5f - moistureNoise) * 4f);
            float tundraWeight = Mathf.Clamp01((0.5f - temperatureNoise) * 4f);
            float grasslandWeight = Mathf.Clamp01(1f - (desertWeight + tundraWeight));
            
            float total = grasslandWeight + desertWeight + tundraWeight;
            if (total <= 0.0001f) return new BiomeWeights { grassland = 1f };

            return new BiomeWeights { grassland = grasslandWeight / total, desert = desertWeight / total, tundra = tundraWeight / total };
        }

        static Color GetBlendedColor(float normalizedHeight, BiomeWeights weights, Color[] colorMap)
        {
            int index = Mathf.Clamp(Mathf.FloorToInt(normalizedHeight * (colorMap.Length - 1)), 0, colorMap.Length - 1);
            Color grasslandCol = colorMap[index];

            Color desertCol;
            if (normalizedHeight < 0.45f) desertCol = new Color(0.85f, 0.75f, 0.50f);
            else if (normalizedHeight < 0.60f) desertCol = new Color(0.80f, 0.65f, 0.40f);
            else desertCol = new Color(0.75f, 0.55f, 0.35f);

            Color tundraCol;
            if (normalizedHeight < 0.52f) tundraCol = new Color(0.7f, 0.75f, 0.8f);
            else if (normalizedHeight < 0.75f) tundraCol = new Color(0.85f, 0.88f, 0.92f);
            else tundraCol = new Color(0.60f, 0.62f, 0.65f);

            return (grasslandCol * weights.grassland) + (desertCol * weights.desert) + (tundraCol * weights.tundra);
        }

        // --- HELPERS ---

        static void AddTriangle(Vector3 v1, Vector3 v2, Vector3 v3, ref int idx, Vector3[] verts, Vector3[] norms, Color[] cols, int[] tris, Vector3 offset, float ns, float ox, float oy, float hm, float varStr, Color[] colorMap, RegionDataSnapshot regionData)
        {
            verts[idx] = v1; verts[idx + 1] = v2; verts[idx + 2] = v3;
            Vector3 normal = Vector3.Cross(v2 - v1, v3 - v1).normalized;
            norms[idx] = norms[idx + 1] = norms[idx + 2] = normal;

            Vector3 center = (v1 + v2 + v3) / 3f;
            Vector3 worldCenter = offset + center;
            
            float avgHeight = (v1.y + v2.y + v3.y) / 3f;
            float normHeight = Mathf.InverseLerp(-1f * hm, 1f * hm, avgHeight);
            
            Color baseCol = GetColorForPosition(worldCenter.x, worldCenter.z, normHeight, ns, ox, oy, colorMap, regionData);
            Color finalCol = AddColorVariation(baseCol, worldCenter, varStr);

            cols[idx] = cols[idx + 1] = cols[idx + 2] = finalCol;
            tris[idx] = idx; tris[idx + 1] = idx + 1; tris[idx + 2] = idx + 2;
            idx += 3;
        }

        static Color AddColorVariation(Color baseColor, Vector3 position, float variationStrength)
        {
            float hash = Mathf.Sin(position.x * 12.9898f + position.z * 78.233f) * 43758.5453f;
            hash = hash - Mathf.Floor(hash);
            float variation = (hash - 0.5f) * 2f;
            
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            v = Mathf.Clamp01(v + variation * variationStrength);
            s = Mathf.Clamp01(s + variation * variationStrength * 0.3f);
            
            Color finalColor = Color.HSVToRGB(h, s, v);
            finalColor.a = baseColor.a;
            return finalColor;
        }

        static Vector3 CalculateVertexNormal(Vector3[] vertices, int x, int y, int size)
        {
            Vector3 normal = Vector3.up;
            int index = y * size + x;
            if (x > 0 && y > 0) {
                Vector3 v = Vector3.Cross(vertices[index - 1] - vertices[index], vertices[index - size] - vertices[index]);
                if (v.sqrMagnitude > 0.001f) normal += v;
            }
            if (x < size - 1 && y > 0) {
                Vector3 v = Vector3.Cross(vertices[index - size] - vertices[index], vertices[index + 1] - vertices[index]);
                if (v.sqrMagnitude > 0.001f) normal += v;
            }
            if (x < size - 1 && y < size - 1) {
                Vector3 v = Vector3.Cross(vertices[index + 1] - vertices[index], vertices[index + size] - vertices[index]);
                if (v.sqrMagnitude > 0.001f) normal += v;
            }
            return normal.normalized;
        }
    }

    public static class NativePerlinNoise
    {
        private static readonly int[] permutation = {
            151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,
            190, 6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,88,237,149,56,87,174,20,
            125,136,171,168, 68,175,74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,60,211,133,230,220,
            105,92,41,55,46,245,40,244,102,143,54, 65,25,63,161, 1,216,80,73,209,76,132,187,208, 89,18,169,200,
            196,135,130,116,188,159,86,164,100,109,198,173,186, 3,64,52,217,226,250,124,123,5,202,38,147,118,126,
            255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,119,248,152, 2,44,154,163, 70,
            221,153,101,155,167, 43,172,9,129,22,39,253, 19,98,108,110,79,113,224,232,178,185, 112,104,218,246,97,
            228,251,34,242,193,238,210,144,12,191,179,162,241, 81,51,145,235,249,14,239,107,49,192,214, 31,181,199,
            106,157,184, 84,204,176,115,121,50,45,127, 4,150,254,138,236,205,93,222,114,67,29,24,72,243,141,128,
            195,78,66,215,61,156,180
        };

        private static readonly int[] p;

        static NativePerlinNoise()
        {
            p = new int[512];
            for (int i = 0; i < 256; i++) p[256 + i] = p[i] = permutation[i];
        }

        public static float Noise(float x, float y)
        {
            int X = (int)System.Math.Floor(x) & 255;
            int Y = (int)System.Math.Floor(y) & 255;
            
            x -= (float)System.Math.Floor(x);
            y -= (float)System.Math.Floor(y);
            
            float u = Fade(x);
            float v = Fade(y);
            
            int A = p[X] + Y, AA = p[A], AB = p[A + 1];
            int B = p[X + 1] + Y, BA = p[B], BB = p[B + 1];

            return (Lerp(v, Lerp(u, Grad(p[AA], x, y), Grad(p[BA], x - 1, y)),
                                Lerp(u, Grad(p[AB], x, y - 1), Grad(p[BB], x - 1, y - 1))) + 1f) * 0.5f;
        }

        static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
        static float Lerp(float t, float a, float b) => a + t * (b - a);
        static float Grad(int hash, float x, float y)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : h == 12 || h == 14 ? x : 0;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
    }
}