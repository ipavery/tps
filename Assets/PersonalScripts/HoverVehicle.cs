using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Core;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using TMPro;

[RequireComponent(typeof(Rigidbody))]
public class HoverVehicle : NetworkBehaviour, IInteractable
{
    [Header("Input Integration")]
    public Vector2Event moveInputEvent;

    [Header("Hover Physics")]
    public float hoverHeight = 1.5f;
    public float springConstant = 5000f;
    public float dampingConstant = 500f;
    public Transform[] hoverPoints;
    public LayerMask groundLayer;

    [Header("Handling")]
    public float forwardThrust = 3000f;
    public float turnTorque = 1500f;
    public float lateralGrip = 50f;

    private Rigidbody rb;
    [Header("Mounting Setup")]
    public Transform seatTransform; // Drag your Seat object here in the Inspector
    private NetworkVariable<bool> isMounted = new NetworkVariable<bool>(false);

    private float m_LastInteractTime;
    private const float INTERACT_COOLDOWN = 0.5f;

    [Header("Camera Setup")]
    public CinemachineCamera playerCamera;

    [Header("Dynamic Damping")]
    public float maxDamping = 0.1f; // Damping when stopped
    public float minDamping = 0f;   // Damping at top speed
    public float topSpeed = 100f;   // Speed at which damping becomes 0

    // Caches the camera component so we don't use GetComponent every frame
    private Unity.Cinemachine.CinemachineThirdPersonFollow activeCameraBody;
    // Cache variables for restoring the camera later

    [Header("Speedometer UI")]
    public TextMeshProUGUI speedometerText;
    public GameObject speedometerCanvas; // Optional: To toggle the whole UI on/off
    
    [Tooltip("Multiplier to convert m/s. Use 2.237 for MPH, or 3.6 for KM/H")]
    public float speedConversionRate = 2.237f; 
    public string speedUnitLabel = "MPH";

    // --- IInteractable Implementation ---
    public InteractionTriggerMode TriggerMode => InteractionTriggerMode.OnButtonPress;
    public int Priority => 10;
    public string InteractionPromptText => "Drive Speeder";

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass = new Vector3(0, -0.5f, 0); 
    }

    private void FixedUpdate()
    {
        if (!IsSpawned) return;

        ApplyHoverPhysics();

        if (IsOwner && isMounted.Value)
        {
            ApplyPropulsionAndSteering();
        }
    }


    private void Update()
    {
        // If this instance is not spawned, or we are not the player driving it, do nothing
        if (!IsSpawned || !IsOwner) return;

        // NEW: Dynamic Damping Logic
        if (isMounted.Value && activeCameraBody != null)
        {
            float currentSpeed = rb.linearVelocity.magnitude;
            float speedPercentage = Mathf.InverseLerp(0f, topSpeed, currentSpeed);
            float targetDamping = Mathf.Lerp(maxDamping, minDamping, speedPercentage);

            activeCameraBody.Damping = new Vector3(targetDamping, targetDamping, targetDamping);
        }
        
        // NEW: Speedometer Logic
        if (isMounted.Value && speedometerText != null)
        {
            // Get raw speed in m/s, multiply for MPH/KMH, and round to a whole number
            float currentSpeed = rb.linearVelocity.magnitude * speedConversionRate;
            speedometerText.text = $"{Mathf.RoundToInt(currentSpeed)} {speedUnitLabel}";
        }

        // If we are mounted and the player presses the 'E' key
        if (isMounted.Value && Keyboard.current.eKey.wasPressedThisFrame)
        {
            // 1. Check if enough time has passed
            if (Time.time - m_LastInteractTime < INTERACT_COOLDOWN) return;

            // 2. Reset the timer
            m_LastInteractTime = Time.time;
            
            RequestDismountRpc(Unity.Netcode.NetworkManager.Singleton.LocalClientId);
        }
    }

    private void ApplyHoverPhysics()
    {
        foreach (Transform point in hoverPoints)
        {
            if (Physics.Raycast(point.position, -point.up, out RaycastHit hit, hoverHeight * 2f, groundLayer))
            {
                Vector3 pointVelocity = rb.GetPointVelocity(point.position);
                float compression = hoverHeight - hit.distance;
                float upwardSpeed = Vector3.Dot(pointVelocity, point.up);
                float hoverForce = (compression * springConstant) - (upwardSpeed * dampingConstant);

                rb.AddForceAtPosition(point.up * hoverForce, point.position);
            }
        }
    }

    private void ApplyPropulsionAndSteering()
    {
        Vector2 input = moveInputEvent != null ? moveInputEvent.LastValue : Vector2.zero;
        
        float accelInput = input.y; 
        float turnInput = input.x;  

        rb.AddForce(transform.forward * accelInput * forwardThrust);
        rb.AddTorque(transform.up * turnInput * turnTorque);

        Vector3 sidewaysVelocity = Vector3.Project(rb.linearVelocity, transform.right);
        rb.AddForce(-sidewaysVelocity * lateralGrip, ForceMode.Acceleration);
    }

    // --- Interaction Logic ---
    public bool CanInteract(GameObject interactor)
    {
        // Only allow interaction if the vehicle is empty
        return !isMounted.Value;
    }

    public void Interact(GameObject interactor)
    {
        // 1. Check if enough time has passed since the last interaction
        if (Time.time - m_LastInteractTime < INTERACT_COOLDOWN) return;

        // 2. Reset the timer
        m_LastInteractTime = Time.time;
        
        // The client player presses the interact button, sending a request to the server
        NetworkObject playerNetObj = interactor.GetComponent<NetworkObject>();
        if (playerNetObj != null)
        {
            RequestMountRpc(playerNetObj.OwnerClientId);
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestMountRpc(ulong clientId)
    {
        // The server validates the request and changes ownership
        if (!isMounted.Value)
        {
            // Get the player's NetworkObject
            var playerObj = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;

            // Parent the player to the vehicle across the network
            NetworkObject.ChangeOwnership(clientId);
            isMounted.Value = true;

            playerObj.TrySetParent(NetworkObject, false);

            LockPlayerClientRpc(RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void LockPlayerClientRpc(RpcParams rpcParams = default)
    {
        var localPlayer = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject;

        // A. Disable Colliders and CharacterController
        if (localPlayer.TryGetComponent<CharacterController>(out var controller)) { controller.enabled = false; }
        Collider[] allColliders = localPlayer.GetComponentsInChildren<Collider>();
        foreach (Collider col in allColliders) { col.enabled = false; }

        // B. Disable movement script AND turn off Network Interpolation to fix the jitter!
        if (localPlayer.TryGetComponent<CoreMovement>(out var playerMovement))
        {
            playerMovement.enabled = false;
            playerMovement.Interpolate = false; // This is the magic jitter fix
        }

        // C. Snap the player to the seat
        localPlayer.transform.SetPositionAndRotation(seatTransform.position, seatTransform.rotation);

        // D. Anchor the camera to the vehicle and zero out the mouse look
        if (localPlayer.TryGetComponent<CoreCameraController>(out var camController))
        {
            camController.RotationAnchor = transform; 
            camController.SetHorizontalLookAngle(0f);
        }

        // E. Find the camera ONLY to cache it for your dynamic damping. 
        // We no longer change the Follow or LookAt targets!
        GameObject freeLookObj = GameObject.Find("[BB] FreeLook(Clone)");
        if (freeLookObj != null)
        {
            var freeLookCam = freeLookObj.GetComponent<Unity.Cinemachine.CinemachineCamera>();
            if (freeLookCam != null)
            {
                activeCameraBody = freeLookCam.GetComponent<Unity.Cinemachine.CinemachineThirdPersonFollow>();
            }
        }

        // Show the speedometer UI
        if (speedometerCanvas != null)
        {
            speedometerCanvas.SetActive(true);
        }
        else if (speedometerText != null)
        {
            speedometerText.gameObject.SetActive(true);
        }
    }
    
    [Rpc(SendTo.Server)]
    private void RequestDismountRpc(ulong clientId)
    {
        if (isMounted.Value)
        {
            var playerObj = Unity.Netcode.NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;

            // 1. Unparent the player across the network
            playerObj.TryRemoveParent();

            // 2. Explicitly give ownership back to the Server
            NetworkObject.ChangeOwnership(Unity.Netcode.NetworkManager.ServerClientId);

            // 3. Update the state
            isMounted.Value = false;

            // 4. Tell the specific client to turn their physics back on
            UnlockPlayerClientRpc(RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void UnlockPlayerClientRpc(RpcParams rpcParams = default)
    {
        var localPlayer = Unity.Netcode.NetworkManager.Singleton.LocalClient.PlayerObject;

        // A. Re-enable Colliders and CharacterController
        if (localPlayer.TryGetComponent<CharacterController>(out var controller)) { controller.enabled = true; }
        Collider[] allColliders = localPlayer.GetComponentsInChildren<Collider>(true); 
        foreach (Collider col in allColliders) { col.enabled = true; }

        // B. Re-enable movement script AND Network Interpolation
        if (localPlayer.TryGetComponent<CoreMovement>(out var playerMovement))
        {
            playerMovement.enabled = true;
            playerMovement.Interpolate = true; 
        }

        // C. Move the player slightly to the side
        localPlayer.transform.position += localPlayer.transform.right * 2f;

        // D. Remove the Rotation Anchor
        if (localPlayer.TryGetComponent<CoreCameraController>(out var camController))
        {
            camController.RotationAnchor = null;
            camController.SetHorizontalLookAngle(camController.transform.eulerAngles.y);
        }

        // E. Clear the damping cache. No target reverting needed!
        activeCameraBody = null;

        // Hide the speedometer UI
        if (speedometerCanvas != null)
        {
            speedometerCanvas.SetActive(false);
        }
        else if (speedometerText != null)
        {
            speedometerText.gameObject.SetActive(false);
        }
    }
}