using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using Unity.Netcode.Components;

public class JitterTestController : MonoBehaviour
{
    [Header("Dependencies")]
    public Rigidbody sledRigidbody;
    public NetworkTransform sledNetworkTransform;
    public CinemachineBrain cameraBrain;

    private void Start()
    {
        QualitySettings.vSyncCount = 0;
    }

    private void Update()
    {
        if (Keyboard.current == null) return;

        // --- 1. TOGGLE RIGIDBODY INTERPOLATION ---
        if (Keyboard.current.digit1Key.wasPressedThisFrame)
        {
            sledRigidbody.interpolation = RigidbodyInterpolation.None;
            Debug.Log("Physics: Rigidbody Interpolation OFF");
        }
        if (Keyboard.current.digit2Key.wasPressedThisFrame)
        {
            sledRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            Debug.Log("Physics: Rigidbody Interpolation ON");
        }

        // --- 2. TOGGLE NETWORK INTERPOLATION ---
        if (Keyboard.current.digit3Key.wasPressedThisFrame)
        {
            sledNetworkTransform.Interpolate = false;
            Debug.Log("Network: NetworkTransform Interpolation OFF");
        }
        if (Keyboard.current.digit4Key.wasPressedThisFrame)
        {
            sledNetworkTransform.Interpolate = true;
            Debug.Log("Network: NetworkTransform Interpolation ON");
        }

        // --- 3. TOGGLE CAMERA UPDATE METHOD ---
        if (Keyboard.current.digit5Key.wasPressedThisFrame)
        {
            cameraBrain.UpdateMethod = CinemachineBrain.UpdateMethods.FixedUpdate;
            Debug.Log("Camera: Brain set to FIXED Update");
        }
        if (Keyboard.current.digit6Key.wasPressedThisFrame)
        {
            cameraBrain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            Debug.Log("Camera: Brain set to LATE Update");
        }
    }
}