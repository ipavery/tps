using UnityEngine;
using UnityEngine.InputSystem; // Required for the new Input System

public class JitterTestController : MonoBehaviour
{
    [Header("Dependencies")]
    public Rigidbody sledRigidbody;
    public BasicCameraControl controlCamera;

    private void Start()
    {
        // Force VSync off so we can manipulate the framerate
        QualitySettings.vSyncCount = 0;
    }

    private void Update()
    {
        // Safety check to ensure a keyboard is connected
        if (Keyboard.current == null) return;

        // --- 1. TOGGLE RIGIDBODY INTERPOLATION ---
        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            sledRigidbody.interpolation = RigidbodyInterpolation.None;
            Debug.Log("Physics: Interpolation OFF");
        }
        if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            sledRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            Debug.Log("Physics: Interpolation ON");
        }

        // --- 2. TOGGLE CAMERA UPDATE METHOD ---
        if (Keyboard.current.digit3Key.wasPressedThisFrame)
        {
            controlCamera.updateMode = BasicCameraControl.UpdateTiming.FixedUpdate;
            Debug.Log("Camera: FIXED Update");
        }
        if (Keyboard.current.digit4Key.wasPressedThisFrame)
        {
            controlCamera.updateMode = BasicCameraControl.UpdateTiming.LateUpdate;
            Debug.Log("Camera: LATE Update");
        }

        // --- 3. FORCE FRAMERATE MISMATCHES ---
        if (Keyboard.current.digit5Key.wasPressedThisFrame)
        {
            Application.targetFrameRate = 50; 
            Debug.Log("Framerate: 50 FPS (Matched to Physics)");
        }
        if (Keyboard.current.digit6Key.wasPressedThisFrame)
        {
            Application.targetFrameRate = 144; 
            Debug.Log("Framerate: 144 FPS (High Refresh Mismatch)");
        }
        if (Keyboard.current.digit7Key.wasPressedThisFrame)
        {
            Application.targetFrameRate = -1; 
            Debug.Log("Framerate: UNCAPPED");
        }
    }
}