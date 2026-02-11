using UnityEngine;
using UnityEngine.Rendering;

public class FlatWaterPlane : MonoBehaviour
{
    [Header("Water Settings")]
    [SerializeField] private float waterHeight = 4f;
    [SerializeField] private float planeSize = 5000f;
    [SerializeField] private int gridResolution = 60;
    
    [Header("Water Appearance")]
    [SerializeField] private Color shallowWaterColor = new Color(0.0f, 0.5f, 0.7f, 0.8f);
    [SerializeField] private float triangleColorVariation = 0.04f;
    [Range(0, 1)] [SerializeField] private float waterSmoothness = 0.9f;
    [Range(0, 1)] [SerializeField] private float waterMetallic = 0.05f;

    // [Header("Waves")]
    // [SerializeField] private bool enableWaves = true;
    // [SerializeField] private float waveSpeed = 0.5f;
    // [SerializeField] private float waveHeight = 0.2f;
    // [SerializeField] private float waveFrequency = 0.1f;
    
    [Header("Camera Following")]
    [SerializeField] private bool followCamera = true;
    [SerializeField] private float updateDistance = 200f;
    
    private GameObject waterPlane;
    private Mesh waterMesh;
    private Vector3[] baseVertices;
    private Vector3[] vertices;
    private Color[] colors;
    private Vector3 lastCameraPosition;
    private MeshRenderer meshRenderer;

    void Start() {
        CreateLowPolyWater();
        if (Camera.main != null) {
            lastCameraPosition = Camera.main.transform.position;
            // Position water plane at camera start position
            Vector3 startPos = Camera.main.transform.position;
            waterPlane.transform.position = new Vector3(startPos.x, waterHeight, startPos.z);
        }
    }

    void CreateLowPolyWater() {
        waterPlane = new GameObject("HDRP_LowPolyWater");
        waterPlane.transform.parent = transform;
        waterPlane.transform.position = new Vector3(0, waterHeight, 0);
        
        MeshFilter mf = waterPlane.AddComponent<MeshFilter>();
        meshRenderer = waterPlane.AddComponent<MeshRenderer>();
        
        waterMesh = new Mesh();
        waterMesh.name = "LowPolyWaterMesh";
        waterMesh.indexFormat = IndexFormat.UInt32;

        // Flat shading: 2 triangles per quad, 3 vertices per triangle
        int triangleCount = gridResolution * gridResolution * 2;
        vertices = new Vector3[triangleCount * 3];
        colors = new Color[triangleCount * 3];
        int[] triangles = new int[triangleCount * 3];

        float step = planeSize / gridResolution;
        float offset = -planeSize / 2f;
        int vIdx = 0;

        for (int z = 0; z < gridResolution; z++) {
            for (int x = 0; x < gridResolution; x++) {
                Vector3 bl = new Vector3(offset + x * step, 0, offset + z * step);
                Vector3 br = new Vector3(offset + (x + 1) * step, 0, offset + z * step);
                Vector3 tl = new Vector3(offset + x * step, 0, offset + (z + 1) * step);
                Vector3 tr = new Vector3(offset + (x + 1) * step, 0, offset + (z + 1) * step);

                // Alternate triangle pattern
                bool alt = (x + z) % 2 == 0;
                if (alt) {
                    AddTriangleToMesh(bl, tl, tr, ref vIdx, vertices, triangles, colors);
                    AddTriangleToMesh(bl, tr, br, ref vIdx, vertices, triangles, colors);
                } else {
                    AddTriangleToMesh(bl, tl, br, ref vIdx, vertices, triangles, colors);
                    AddTriangleToMesh(tl, tr, br, ref vIdx, vertices, triangles, colors);
                }
            }
        }

        waterMesh.vertices = vertices;
        waterMesh.triangles = triangles;
        waterMesh.colors = colors;
        waterMesh.RecalculateNormals();
        waterMesh.RecalculateBounds();
        
        baseVertices = (Vector3[])vertices.Clone();
        mf.mesh = waterMesh;

        SetupHDRPMaterial(meshRenderer);
        
        Debug.Log($"Water plane created with {triangleCount} triangles at height {waterHeight}");
    }

    void AddTriangleToMesh(Vector3 v1, Vector3 v2, Vector3 v3, ref int idx, Vector3[] verts, int[] tris, Color[] cols) {
        verts[idx] = v1; 
        verts[idx + 1] = v2; 
        verts[idx + 2] = v3;
        
        // Enhanced per-triangle color variation (matching terrain style)
        Vector3 triCenter = (v1 + v2 + v3) / 3f;
        float hash = Mathf.Sin(triCenter.x * 12.9898f + triCenter.z * 78.233f) * 43758.5453f;
        hash = hash - Mathf.Floor(hash);
        
        // Convert to -1 to 1 range
        float variation = (hash - 0.5f) * 2f;
        
        // Apply variation to HSV (like terrain)
        Color.RGBToHSV(shallowWaterColor, out float h, out float s, out float v);
        
        // Apply stronger variation for better contrast
        float vVariation = variation * triangleColorVariation * 2.5f;
        float sVariation = variation * triangleColorVariation * 0.8f;
        
        v = Mathf.Clamp01(v + vVariation);
        s = Mathf.Clamp01(s + sVariation);
        
        Color finalCol = Color.HSVToRGB(h, s, v);
        finalCol.a = shallowWaterColor.a;

        cols[idx] = cols[idx + 1] = cols[idx + 2] = finalCol;
        tris[idx] = idx; 
        tris[idx + 1] = idx + 1; 
        tris[idx + 2] = idx + 2;
        idx += 3;
    }

    void SetupHDRPMaterial(MeshRenderer mr) {
        // Try HDRP/Lit first
        Shader hdrpLit = Shader.Find("HDRP/Lit");
        
        if (hdrpLit == null) {
            Debug.LogWarning("HDRP/Lit shader not found! Trying Unlit shader.");
            // Fallback to Unlit/Color for guaranteed visibility
            Shader unlit = Shader.Find("Unlit/Color");
            if (unlit != null) {
                Material mat = new Material(unlit);
                mat.color = shallowWaterColor;
                mr.material = mat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                return;
            }
        }
        
        Material hdrpMat = new Material(hdrpLit);
        hdrpMat.name = "WaterMaterial_HDRP";

        // Base color - white to use vertex colors
        hdrpMat.SetColor("_BaseColor", Color.white);
        hdrpMat.SetColor("_UnlitColor", Color.white);
        
        // Surface properties
        hdrpMat.SetFloat("_Smoothness", waterSmoothness);
        hdrpMat.SetFloat("_Metallic", waterMetallic);
        
        // CRITICAL: Enable transparency properly for HDRP
        hdrpMat.SetFloat("_SurfaceType", 1); // 1 = Transparent
        hdrpMat.SetFloat("_BlendMode", 0); // 0 = Alpha
        hdrpMat.SetFloat("_AlphaCutoffEnable", 0);
        
        // Set blend modes
        hdrpMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        hdrpMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        hdrpMat.SetInt("_AlphaSrcBlend", (int)BlendMode.One);
        hdrpMat.SetInt("_AlphaDstBlend", (int)BlendMode.OneMinusSrcAlpha);
        
        // Depth settings
        hdrpMat.SetInt("_ZWrite", 0); // No depth write for transparency
        hdrpMat.SetInt("_ZTestDepthEqualForOpaque", (int)CompareFunction.LessEqual);
        hdrpMat.SetInt("_CullMode", (int)CullMode.Off); // Render both sides
        
        // Enable double-sided rendering
        hdrpMat.SetFloat("_DoubleSidedEnable", 1);
        hdrpMat.SetFloat("_DoubleSidedNormalMode", 1);
        
        // Enable necessary shader keywords
        hdrpMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        hdrpMat.EnableKeyword("_BLENDMODE_ALPHA");
        hdrpMat.EnableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
        hdrpMat.DisableKeyword("_BLENDMODE_ADD");
        hdrpMat.DisableKeyword("_BLENDMODE_PRE_MULTIPLY");
        
        // Set render queue
        hdrpMat.renderQueue = (int)RenderQueue.Transparent;
        
        mr.material = hdrpMat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = true;
        
        Debug.Log($"HDRP water material created. Transparent mode: {hdrpMat.GetFloat("_SurfaceType")}");
    }

    void Update() {
        if (waterPlane == null) return;
        
        // if (enableWaves) {
        //     UpdateWaves();
        // }
        
        if (followCamera && Camera.main != null) {
            Vector3 camPos = Camera.main.transform.position;
            
            // Update position when camera moves
            if (Vector3.Distance(new Vector3(camPos.x, 0, camPos.z), 
                new Vector3(lastCameraPosition.x, 0, lastCameraPosition.z)) > updateDistance) {
                waterPlane.transform.position = new Vector3(camPos.x, waterHeight, camPos.z);
                lastCameraPosition = camPos;
            }
        }
    }

    // void UpdateWaves() {
    //     if (baseVertices == null || vertices == null) return;
        
    //     float time = Time.time;
        
    //     for (int i = 0; i < vertices.Length; i++) {
    //         Vector3 v = baseVertices[i];
    //         Vector3 worldPos = waterPlane.transform.position + v;
            
    //         // Multiple wave frequencies
    //         float wave1 = Mathf.Sin(time * waveSpeed + worldPos.x * waveFrequency) * waveHeight;
    //         float wave2 = Mathf.Cos(time * waveSpeed * 0.8f + worldPos.z * waveFrequency * 0.7f) * waveHeight;
    //         float wave3 = Mathf.Sin(time * waveSpeed * 1.2f + (worldPos.x + worldPos.z) * waveFrequency * 0.5f) * waveHeight * 0.5f;
            
    //         vertices[i] = new Vector3(v.x, wave1 + wave2 + wave3, v.z);
    //     }
        
    //     waterMesh.vertices = vertices;
    //     waterMesh.RecalculateNormals();
    // }
    
    public void SetWaterHeight(float height) {
        waterHeight = height;
        if (waterPlane != null) {
            Vector3 pos = waterPlane.transform.position;
            waterPlane.transform.position = new Vector3(pos.x, waterHeight, pos.z);
        }
    }
    
    // Debug helper - call from inspector or code
    [ContextMenu("Force Recreate Water")]
    public void RecreateWater() {
        if (waterPlane != null) {
            DestroyImmediate(waterPlane);
        }
        CreateLowPolyWater();
        if (Camera.main != null) {
            Vector3 camPos = Camera.main.transform.position;
            waterPlane.transform.position = new Vector3(camPos.x, waterHeight, camPos.z);
        }
    }
}