using UnityEngine;

public class DayNightCycle : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Light directionalLight; // Your sun
    [SerializeField] Transform planet; // Your planet center
    [SerializeField] GameObject visibleSun; // Optional: visible sun sphere in sky
    
    [Header("Day/Night Settings")]
    [SerializeField] float dayDurationInSeconds = 120f; // 2 minutes = 1 full day
    [SerializeField] float startTimeOfDay = 0.25f; // 0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset
    
    [Header("Sun Settings")]
    [SerializeField] float sunIntensity = 100000f; // Intensity at noon
    [SerializeField] Color dayColor = new Color(1f, 0.95f, 0.9f); // Slightly warm white
    [SerializeField] Color sunsetColor = new Color(1f, 0.6f, 0.3f); // Orange
    [SerializeField] Color nightColor = new Color(0.3f, 0.4f, 0.6f); // Blue tint
    
    [Header("Visible Sun Settings")]
    [SerializeField] float sunDistance = 100000f; // VERY far away - allows spherical world day/night zones
    [SerializeField] float sunSize = 4000f; // Large size to remain visible at extreme distance
    [Tooltip("If true, sun renders at infinite distance (spherical world). If false, follows camera (flat world)")]
    [SerializeField] bool sphericalWorldMode = true;
    
    [Header("Ambient Light")]
    [SerializeField] bool changeAmbientLight = true;
    [SerializeField] Color ambientDay = new Color(0.4f, 0.4f, 0.4f);
    [SerializeField] Color ambientNight = new Color(0.02f, 0.02f, 0.05f);
    
    private float currentTimeOfDay;
    
    void Start()
    {
        currentTimeOfDay = startTimeOfDay;
        
        if (directionalLight == null)
        {
            directionalLight = GetComponent<Light>();
        }
        
        if (planet == null)
        {
            planet = GameObject.Find("Planet")?.transform;
        }
        
        // Create visible sun if not assigned
        if (visibleSun == null)
        {
            CreateVisibleSun();
        }
    }
    
    void Update()
    {
        // Progress time
        currentTimeOfDay += Time.deltaTime / dayDurationInSeconds;
        if (currentTimeOfDay >= 1f)
        {
            currentTimeOfDay = 0f;
        }
        
        UpdateSun();
    }
    
    void UpdateSun()
    {
        // Rotate sun around planet
        // 0 = midnight (bottom), 0.25 = sunrise, 0.5 = noon (top), 0.75 = sunset
        float sunAngle = currentTimeOfDay * 360f;
        
        if (planet != null)
        {
            // Position light to rotate around planet
            transform.position = planet.position;
        }
        
        // Rotate the light (sun moving across sky from east to west)
        // At sunrise (0.25): sun at eastern horizon
        // At noon (0.5): sun directly overhead
        // At sunset (0.75): sun at western horizon
        // Rotate on X axis to create the arc
        transform.rotation = Quaternion.Euler(sunAngle, 170f, 0f);
        
        // Position visible sun if it exists
        if (visibleSun != null)
        {
            Vector3 sunDirection = -transform.forward; // Opposite of light direction
            
            if (sphericalWorldMode)
            {
                // SPHERICAL WORLD MODE: Sun at fixed world position
                // This allows different parts of planet to be in day/night simultaneously
                Vector3 centerPoint = planet != null ? planet.position : Vector3.zero;
                visibleSun.transform.position = centerPoint + sunDirection * sunDistance;
            }
            else
            {
                // FLAT WORLD MODE: Sun follows camera
                // Sun always appears at same distance from player
                if (Camera.main != null)
                {
                    visibleSun.transform.position = Camera.main.transform.position + sunDirection * sunDistance;
                }
            }
            
            // Make sun face the camera (billboard effect)
            if (Camera.main != null)
            {
                visibleSun.transform.LookAt(Camera.main.transform);
                visibleSun.transform.Rotate(0, 180, 0); // Flip to face camera
            }
        }
        
        // Change sun intensity and color based on time of day
        float sunHeight = Mathf.Sin(currentTimeOfDay * Mathf.PI * 2f); // -1 to 1
        
        if (sunHeight > 0) // Daytime
        {
            // Full intensity at noon
            directionalLight.intensity = Mathf.Lerp(0f, sunIntensity, sunHeight);
            
            // Blend between sunrise/sunset and day color
            float sunsetBlend = 1f - Mathf.Abs(sunHeight); // 0 at noon, 1 at horizon
            Color currentSunColor = Color.Lerp(dayColor, sunsetColor, sunsetBlend);
            directionalLight.color = currentSunColor;
            
            // Update visible sun color
            if (visibleSun != null)
            {
                Renderer sunRenderer = visibleSun.GetComponent<Renderer>();
                if (sunRenderer != null)
                {
                    sunRenderer.material.SetColor("_EmissionColor", currentSunColor * 2f);
                }
            }
            
            // Show sun during day
            if (visibleSun != null)
            {
                visibleSun.SetActive(true);
            }
        }
        else // Nighttime
        {
            // Very dim moonlight
            directionalLight.intensity = Mathf.Lerp(0f, sunIntensity * 0.1f, -sunHeight);
            directionalLight.color = nightColor;
            
            // Hide sun at night
            if (visibleSun != null)
            {
                visibleSun.SetActive(false);
            }
        }
        
        // Update ambient lighting
        if (changeAmbientLight)
        {
            float ambientBlend = Mathf.InverseLerp(-0.2f, 0.2f, sunHeight);
            RenderSettings.ambientLight = Color.Lerp(ambientNight, ambientDay, ambientBlend);
        }
    }
    
    void CreateVisibleSun()
    {
        // Create a sphere to represent the sun
        visibleSun = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visibleSun.name = "Visible Sun";
        visibleSun.transform.localScale = Vector3.one * sunSize;
        
        // Remove collider (don't need physics on sun)
        Collider collider = visibleSun.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
        
        // Create emissive material for sun
        Renderer renderer = visibleSun.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material sunMaterial = new Material(Shader.Find("HDRP/Lit"));
            
            // Enable emission
            sunMaterial.EnableKeyword("_EMISSION");
            sunMaterial.SetFloat("_UseEmissiveIntensity", 1);
            
            // Set sun color
            sunMaterial.SetColor("_BaseColor", Color.white);
            sunMaterial.SetColor("_EmissionColor", dayColor * 2f);
            
            // Make it bright and emissive
            sunMaterial.SetFloat("_EmissiveIntensity", 10f);
            
            renderer.material = sunMaterial;
        }
    }
    
    // Helper method to set time manually
    public void SetTimeOfDay(float time)
    {
        currentTimeOfDay = Mathf.Clamp01(time);
    }
}