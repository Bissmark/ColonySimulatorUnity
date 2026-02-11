using UnityEngine;

[CreateAssetMenu(fileName = "TerrainColorSettings", menuName = "Terrain/Color Settings")]
public class TerrainColorSettings : ScriptableObject
{
    [System.Serializable]
    public class TerrainLayer
    {
        public string name;
        public float height;
        public Color color;
    }
    
    [Header("Terrain Layers (from low to high)")]
    public TerrainLayer[] layers = new TerrainLayer[]
    {
        new TerrainLayer { name = "Deep Water", height = 0.0f, color = new Color(0.0f, 0.1f, 0.3f) },
        new TerrainLayer { name = "Shallow Water", height = 0.4f, color = new Color(0.0f, 0.3f, 0.5f) },
        new TerrainLayer { name = "Sand/Beach", height = 0.48f, color = new Color(0.76f, 0.70f, 0.50f) },
        new TerrainLayer { name = "Grass Low", height = 0.52f, color = new Color(0.2f, 0.5f, 0.2f) },
        new TerrainLayer { name = "Grass", height = 0.60f, color = new Color(0.3f, 0.6f, 0.3f) },
        new TerrainLayer { name = "Grass Hill", height = 0.70f, color = new Color(0.4f, 0.5f, 0.3f) },
        new TerrainLayer { name = "Rock Low", height = 0.80f, color = new Color(0.5f, 0.5f, 0.5f) },
        new TerrainLayer { name = "Rock", height = 0.90f, color = new Color(0.6f, 0.6f, 0.6f) },
        new TerrainLayer { name = "Snow", height = 0.95f, color = new Color(0.9f, 0.9f, 0.95f) },
        new TerrainLayer { name = "Peak", height = 1.0f, color = new Color(1.0f, 1.0f, 1.0f) }
    };
    
    public Color GetColorForHeight(float normalizedHeight)
    {
        if (layers == null || layers.Length == 0)
            return Color.magenta;
        
        // Find the two layers to interpolate between
        for (int i = 0; i < layers.Length - 1; i++)
        {
            if (normalizedHeight >= layers[i].height && normalizedHeight <= layers[i + 1].height)
            {
                float t = Mathf.InverseLerp(layers[i].height, layers[i + 1].height, normalizedHeight);
                return Color.Lerp(layers[i].color, layers[i + 1].color, t);
            }
        }
        
        // If below first layer
        if (normalizedHeight < layers[0].height)
            return layers[0].color;
        
        // If above last layer
        return layers[layers.Length - 1].color;
    }
}