using UnityEngine;
using System.Collections;

public class NPCSpawner : MonoBehaviour
{
    [Header("Player Settings")]
    [Tooltip("The player prefab to spawn")]
    [SerializeField] private GameObject playerPrefab;
    
    [Tooltip("Height offset above terrain (so player doesn't spawn inside ground)")]
    [SerializeField] private float spawnHeightOffset = 1f;
    
    [Tooltip("Spawn position in world XZ coordinates (Y will be calculated from terrain)")]
    [SerializeField] private Vector2 spawnPosition = Vector2.zero;
    
    [Header("Camera Settings")]
    [Tooltip("Should the camera follow the player?")]
    [SerializeField] private bool cameraFollowsPlayer = true;
    
    [Tooltip("Camera offset from player (for third person view)")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(0, 5, -10);
    
    [Header("References")]
    [Tooltip("Reference to chunk manager (auto-finds if not set)")]
    [SerializeField] private FlatChunkManager chunkManager;
    
    [Tooltip("Reference to camera (auto-finds if not set)")]
    [SerializeField] private Camera mainCamera;
    
    // Spawned player reference
    private GameObject spawnedPlayer;
    private bool hasSpawned = false;
    
    public GameObject SpawnedPlayer => spawnedPlayer;
    public bool HasSpawned => hasSpawned;

    void Start()
    {
        // Auto-find references if not set
        if (chunkManager == null)
            chunkManager = FindFirstObjectByType<FlatChunkManager>();
        
        if (mainCamera == null)
            mainCamera = Camera.main;
        
        // Start spawn coroutine
        StartCoroutine(WaitAndSpawnPlayer());
    }

    IEnumerator WaitAndSpawnPlayer()
    {
        // Wait a moment for chunk system to initialize
        yield return new WaitForSeconds(0.1f);
        
        // Wait until we can raycast to terrain (chunk is loaded and has collider)
        float timeout = 30f;
        float elapsed = 0f;
        bool terrainReady = false;
        
        while (!terrainReady && elapsed < timeout)
        {
            // Try to find terrain height via raycast
            Vector3 rayStart = new Vector3(spawnPosition.x, 1000f, spawnPosition.y);
            
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 2000f))
            {
                // Found terrain!
                terrainReady = true;
                SpawnPlayerAtPosition(hit.point);
            }
            else
            {
                // Terrain not ready yet, wait and retry
                yield return new WaitForSeconds(0.2f);
                elapsed += 0.2f;
            }
        }
        
        if (!terrainReady)
        {
            Debug.LogWarning("PlayerSpawner: Timeout waiting for terrain. Spawning at default height.");
            SpawnPlayerAtPosition(new Vector3(spawnPosition.x, 50f, spawnPosition.y));
        }
    }

    void SpawnPlayerAtPosition(Vector3 terrainPoint)
    {
        if (playerPrefab == null)
        {
            Debug.LogError("PlayerSpawner: No player prefab assigned!");
            return;
        }
        
        // Calculate spawn position
        Vector3 spawnPos = terrainPoint + Vector3.up * spawnHeightOffset;
        
        // Spawn the player
        spawnedPlayer = Instantiate(playerPrefab, spawnPos, Quaternion.identity);
        spawnedPlayer.name = "Player";
        
        hasSpawned = true;
        
        Debug.Log($"Player spawned at {spawnPos}");
        
        // Setup camera if needed
        // if (cameraFollowsPlayer && mainCamera != null)
        // {
        //     // Option 1: Parent camera to player (simple)
        //     // mainCamera.transform.SetParent(spawnedPlayer.transform);
        //     // mainCamera.transform.localPosition = cameraOffset;
        //     // mainCamera.transform.LookAt(spawnedPlayer.transform);
            
        //     // Option 2: Add a follow component
        //     CameraFollowTarget follower = mainCamera.gameObject.GetComponent<CameraFollowTarget>();
        //     if (follower == null)
        //         follower = mainCamera.gameObject.AddComponent<CameraFollowTarget>();
            
        //     follower.SetTarget(spawnedPlayer.transform, cameraOffset);
        // }
        
        // Update chunk manager viewer to be the player
        if (chunkManager != null)
        {
            // Use reflection or add a public method to update viewer
            // For now, you can manually set it in inspector or add a method
        }
    }
    
    /// <summary>
    /// Spawn a player at runtime (for multiplayer)
    /// </summary>
    public GameObject SpawnPlayerAt(Vector3 position, GameObject prefab = null)
    {
        GameObject prefabToUse = prefab ?? playerPrefab;
        
        if (prefabToUse == null)
        {
            Debug.LogError("No player prefab provided!");
            return null;
        }
        
        // Raycast to find terrain height
        Vector3 rayStart = new Vector3(position.x, 1000f, position.z);
        Vector3 spawnPos = position;
        
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 2000f))
        {
            spawnPos = hit.point + Vector3.up * spawnHeightOffset;
        }
        
        GameObject player = Instantiate(prefabToUse, spawnPos, Quaternion.identity);
        return player;
    }
}

/// <summary>
/// Simple camera follow component
/// </summary>
public class CameraFollowTarget : MonoBehaviour
{
    private Transform target;
    private Vector3 offset;
    private bool hasTarget = false;
    
    [SerializeField] private float smoothSpeed = 5f;
    [SerializeField] private bool lookAtTarget = true;
    
    public void SetTarget(Transform newTarget, Vector3 newOffset)
    {
        target = newTarget;
        offset = newOffset;
        hasTarget = true;
    }
    
    void LateUpdate()
    {
        if (!hasTarget || target == null) return;
        
        // Calculate desired position
        Vector3 desiredPosition = target.position + target.TransformDirection(offset);
        
        // Smooth follow
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
        
        // Look at target
        if (lookAtTarget)
        {
            transform.LookAt(target);
        }
    }
}