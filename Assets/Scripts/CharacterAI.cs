using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Terrain.Core;

public class CharacterAI : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float stoppingDistance = 2f;
    
    [Header("Tree Chopping")]
    [SerializeField] private float chopDuration = 5f;
    [SerializeField] private float searchRadius = 500f; // Increased default
    [SerializeField] private float initialWaitTime = 2f;
    
    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string walkAnimationParam = "IsWalking";
    [SerializeField] private string gatherAnimationParam = "IsGathering";
    [SerializeField] private string idleAnimationParam = "IsIdle";
    
    [Header("Ground Detection")]
    [SerializeField] private float groundCheckDistance = 10f;
    [SerializeField] private LayerMask groundLayer = ~0;
    
    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;
    
    // State machine
    public enum AIState { Idle, Walking, Chopping, SearchingForTree }
    private AIState currentState = AIState.Idle;
    
    // Current target
    private Vector3 targetPosition;
    private Matrix4x4 targetTreeMatrix;
    private int targetTreeChunkX;
    private int targetTreeChunkY;
    private int targetTreeIndex;
    private bool hasTarget = false;
    
    // References
    private FlatChunkManager chunkManager;
    
    public AIState CurrentState => currentState;

    void Start()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        
        chunkManager = FindFirstObjectByType<FlatChunkManager>();
        
        StartCoroutine(AIBehaviorLoop());
    }

    IEnumerator AIBehaviorLoop()
    {
        SetState(AIState.Idle);
        
        if (showDebugInfo)
            Debug.Log($"AI Starting. Waiting {initialWaitTime}s for terrain to load...");
        
        yield return new WaitForSeconds(initialWaitTime);
        
        while (true)
        {
            SetState(AIState.SearchingForTree);
            yield return StartCoroutine(FindClosestTree());
            
            if (hasTarget)
            {
                SetState(AIState.Walking);
                yield return StartCoroutine(WalkToTarget());
                
                SetState(AIState.Chopping);
                yield return StartCoroutine(ChopTree());
                
                SetState(AIState.Idle);
                yield return new WaitForSeconds(1f);
            }
            else
            {
                if (showDebugInfo)
                    Debug.Log("No trees found nearby. Waiting 3s before retry...");
                
                SetState(AIState.Idle);
                yield return new WaitForSeconds(3f);
            }
        }
    }

    void SetState(AIState newState)
    {
        currentState = newState;
        
        if (animator != null)
        {
            SetAnimatorBool(walkAnimationParam, false);
            SetAnimatorBool(gatherAnimationParam, false);
            SetAnimatorBool(idleAnimationParam, false);
            
            switch (newState)
            {
                case AIState.Idle:
                case AIState.SearchingForTree:
                    SetAnimatorBool(idleAnimationParam, true);
                    break;
                case AIState.Walking:
                    SetAnimatorBool(walkAnimationParam, true);
                    break;
                case AIState.Chopping:
                    SetAnimatorBool(gatherAnimationParam, true);
                    break;
            }
        }
        
        if (showDebugInfo)
            Debug.Log($"AI State: {newState}");
    }
    
    void SetAnimatorBool(string param, bool value)
    {
        if (animator != null && !string.IsNullOrEmpty(param))
        {
            foreach (AnimatorControllerParameter p in animator.parameters)
            {
                if (p.name == param && p.type == AnimatorControllerParameterType.Bool)
                {
                    animator.SetBool(param, value);
                    return;
                }
            }
        }
    }

    IEnumerator FindClosestTree()
    {
        hasTarget = false;
        
        FlatChunk[] chunks = FindObjectsOfType<FlatChunk>();
        
        if (showDebugInfo)
            Debug.Log($"=== TREE SEARCH START ===");
        if (showDebugInfo)
            Debug.Log($"Found {chunks.Length} chunks. Player at: {transform.position}");
        
        float closestDistance = float.MaxValue;
        Vector3 closestTreePos = Vector3.zero;
        FlatChunk closestChunk = null;
        int closestIndex = -1;
        
        int totalTreesFound = 0;
        int chunksWithTrees = 0;
        int highLODChunks = 0;
        
        foreach (var chunk in chunks)
        {
            if (chunk == null) continue;
            
            LODLevel lod = chunk.GetCurrentLOD();
            
            // Count High LOD chunks (only these have trees)
            if (lod == LODLevel.High)
            {
                highLODChunks++;
                if (showDebugInfo)
                    Debug.Log($"  High LOD Chunk {chunk.GetCoord()}: HasTrees={chunk.HasTrees}");
            }
            
            if (!chunk.HasTrees) continue;
            
            chunksWithTrees++;
            
            List<Matrix4x4> treeMatrices = chunk.GetTreeMatrices();
            if (treeMatrices == null || treeMatrices.Count == 0) 
            {
                if (showDebugInfo)
                    Debug.Log($"  Chunk {chunk.GetCoord()} HasTrees=true but GetTreeMatrices returned null/empty!");
                continue;
            }
            
            totalTreesFound += treeMatrices.Count;
            
            if (showDebugInfo)
                Debug.Log($"  Chunk {chunk.GetCoord()} has {treeMatrices.Count} trees");
            
            for (int i = 0; i < treeMatrices.Count; i++)
            {
                Vector3 treePos = treeMatrices[i].GetColumn(3);
                float distance = Vector3.Distance(transform.position, treePos);
                
                if (distance < closestDistance && distance < searchRadius)
                {
                    closestDistance = distance;
                    closestTreePos = treePos;
                    closestChunk = chunk;
                    closestIndex = i;
                    targetTreeMatrix = treeMatrices[i];
                }
            }
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"=== TREE SEARCH RESULTS ===");
            Debug.Log($"High LOD chunks: {highLODChunks}");
            Debug.Log($"Chunks with trees: {chunksWithTrees}");
            Debug.Log($"Total trees found: {totalTreesFound}");
            Debug.Log($"Search radius: {searchRadius}m");
        }
        
        if (closestChunk != null && closestIndex >= 0)
        {
            targetPosition = closestTreePos;
            targetTreeChunkX = closestChunk.GetCoord().x;
            targetTreeChunkY = closestChunk.GetCoord().y;
            targetTreeIndex = closestIndex;
            hasTarget = true;
            
            if (showDebugInfo)
                Debug.Log($"TARGET FOUND: Tree at {targetPosition}, distance: {closestDistance:F1}m");
        }
        else
        {
            if (showDebugInfo)
            {
                if (totalTreesFound > 0)
                    Debug.Log($"Trees exist ({totalTreesFound}) but none within {searchRadius}m radius.");
                else if (highLODChunks == 0)
                    Debug.Log("NO HIGH LOD CHUNKS! Trees only spawn on High LOD. Wait for chunks to load or move closer.");
                else
                    Debug.Log("High LOD chunks exist but no trees. Check biome (might be desert/snow) or TreeSettings.");
            }
        }
        
        yield return null;
    }

    IEnumerator WalkToTarget()
    {
        if (!hasTarget) yield break;
        
        while (true)
        {
            Vector3 directionToTarget = targetPosition - transform.position;
            directionToTarget.y = 0;
            float distanceToTarget = directionToTarget.magnitude;
            
            if (distanceToTarget <= stoppingDistance)
            {
                if (showDebugInfo)
                    Debug.Log("Arrived at tree!");
                yield break;
            }
            
            if (directionToTarget != Vector3.zero)
            {
                Quaternion targetRotation = Quaternion.LookRotation(directionToTarget);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
            }
            
            Vector3 movement = transform.forward * moveSpeed * Time.deltaTime;
            Vector3 newPosition = transform.position + movement;
            
            newPosition = GetGroundPosition(newPosition);
            transform.position = newPosition;
            
            yield return null;
        }
    }

    Vector3 GetGroundPosition(Vector3 position)
    {
        Vector3 rayStart = new Vector3(position.x, position.y + 5f, position.z);
        
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, groundCheckDistance + 5f, groundLayer))
        {
            return new Vector3(position.x, hit.point.y + 0.1f, position.z);
        }
        
        return position;
    }

    IEnumerator ChopTree()
    {
        if (!hasTarget) yield break;
        
        Vector3 directionToTree = targetPosition - transform.position;
        directionToTree.y = 0;
        if (directionToTree != Vector3.zero)
        {
            transform.rotation = Quaternion.LookRotation(directionToTree);
        }
        
        if (showDebugInfo)
            Debug.Log($"Chopping tree for {chopDuration} seconds...");
        
        yield return new WaitForSeconds(chopDuration);
        
        RemoveTargetTree();
        
        if (showDebugInfo)
            Debug.Log("Tree chopped down!");
        
        hasTarget = false;
    }

    void RemoveTargetTree()
    {
        FlatChunk[] chunks = FindObjectsOfType<FlatChunk>();
        
        foreach (var chunk in chunks)
        {
            if (chunk == null) continue;
            
            Vector2Int coord = chunk.GetCoord();
            if (coord.x == targetTreeChunkX && coord.y == targetTreeChunkY)
            {
                chunk.RemoveTree(targetTreeIndex);
                break;
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, searchRadius);
        
        if (hasTarget)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(targetPosition, 1f);
            Gizmos.DrawLine(transform.position, targetPosition);
        }
        
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, stoppingDistance);
    }
}