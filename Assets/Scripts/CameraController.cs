using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 1000f;
    public float lookSpeed = 2f;
    
    private float yaw = 0f;   // Horizontal rotation
    private float pitch = 0f; // Vertical rotation
    
    void Start()
    {
        // Initialize yaw and pitch from current rotation
        Vector3 currentRot = transform.eulerAngles;
        yaw = currentRot.y;
        pitch = currentRot.x;
        
        // Normalize pitch to -180 to 180 range
        if (pitch > 180f) pitch -= 360f;
        
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        
        if (keyboard == null || mouse == null) return;

        // === MOVEMENT (WASD) ===
        Vector3 moveDirection = Vector3.zero;
        
        if (keyboard.wKey.isPressed) moveDirection += transform.forward;
        if (keyboard.sKey.isPressed) moveDirection -= transform.forward;
        if (keyboard.aKey.isPressed) moveDirection -= transform.right;
        if (keyboard.dKey.isPressed) moveDirection += transform.right;
        
        transform.position += moveDirection.normalized * moveSpeed * Time.deltaTime;

        // === MOUSE LOOK (First Person) ===
        Vector2 mouseDelta = mouse.delta.ReadValue();

        // Update yaw and pitch values
        yaw += mouseDelta.x * lookSpeed * 0.1f;
        pitch -= mouseDelta.y * lookSpeed * 0.1f;
        pitch = Mathf.Clamp(pitch, -89f, 89f);

        // Build rotation properly:
        // 1. Create yaw rotation around WORLD up axis
        // 2. Then apply pitch rotation around the resulting right axis
        Quaternion yawRotation = Quaternion.AngleAxis(yaw, Vector3.up);
        Quaternion pitchRotation = Quaternion.AngleAxis(pitch, Vector3.right);

        // Apply: yaw first (around world up), then pitch (around local right)
        transform.rotation = yawRotation * pitchRotation;
        
        // === UNLOCK CURSOR ===
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}