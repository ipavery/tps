using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Core;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using TMPro;
using System.Collections.Generic;
using System.Linq;

[RequireComponent(typeof(Rigidbody))]
public class BaseVehicle : NetworkBehaviour, IInteractable
{
    [Header("Seat Configuration")]
    [Tooltip("Index 0 is ALWAYS the Driver. Subsequent indices are passengers.")]
    public Transform[] seats;
    
    // Server-side dictionary tracking which ClientID is in which seat index
    private Dictionary<ulong, int> serverSeatMap = new Dictionary<ulong, int>();
    
    // Networked variable so clients know if the vehicle is full before interacting
    private NetworkVariable<int> occupantCount = new NetworkVariable<int>(0);

    [Header("Camera & UI")]
    public float maxDamping = 0.1f;
    public float minDamping = 0f;
    public float topSpeed = 5f;
    public TextMeshProUGUI speedometerText;
    public GameObject speedometerCanvas;
    public float speedConversionRate = 1f;
    public string speedUnitLabel = "m/s";
    
    [Header("Interaction Settings")]
    [Tooltip("The name of the layer used by your interaction system.")]
    public string interactableLayerName = "Interactable";

    protected Rigidbody rb;
    
    // Local client state
    protected bool isLocalPlayerMounted = false;
    protected bool isLocalPlayerDriver = false;
    private float m_LastInteractTime;
    private const float INTERACT_COOLDOWN = 0.5f;
    private Unity.Cinemachine.CinemachineThirdPersonFollow activeCameraBody;
    private Vector3 originalCameraDamping; // NEW: Stores the default walking damping

    // --- IInteractable Implementation ---
    public InteractionTriggerMode TriggerMode => InteractionTriggerMode.OnButtonPress;
    public int Priority => 10;
    public string InteractionPromptText => occupantCount.Value == 0 ? "Drive Vehicle" : "Ride as Passenger";

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();

        // Find the integer ID of the layer by its exact string name
        int interactLayer = LayerMask.NameToLayer(interactableLayerName);

        if (interactLayer == -1)
        {
            Debug.LogWarning($"[BaseVehicle] The layer '{interactableLayerName}' does not exist! Please add it in your Unity Tags and Layers settings.");
        }
        else
        {
            // 1. Set the main vehicle object to the interactable layer
            gameObject.layer = interactLayer;

            // 2. Loop through and set all seat transforms to the interactable layer
            foreach (Transform seat in seats)
            {
                if (seat != null)
                {
                    seat.gameObject.layer = interactLayer;
                }
            }
        }
    }

    protected virtual void FixedUpdate()
    {
        if (!IsSpawned) return;

        // Only process physics if there is a driver and we own the object
        if (IsOwner && serverSeatMap.ContainsValue(0))
        {
            ApplyVehiclePhysics();
        }
    }

    protected virtual void Update()
    {
        if (!IsSpawned) return;

        // 1. Handle UI and Camera Damping (Driver Only)
        if (isLocalPlayerMounted && isLocalPlayerDriver)
        {
            ProcessDriverVisuals();
            ProcessVehicleInput();
        }

        // 2. Handle Dismounting (Driver and Passengers)
        if (isLocalPlayerMounted && Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (Time.time - m_LastInteractTime < INTERACT_COOLDOWN) return;
            m_LastInteractTime = Time.time;
            
            RequestDismountRpc(NetworkManager.Singleton.LocalClientId);
        }
    }

    // ==========================================
    // METHODS TO OVERRIDE IN SPECIFIC VEHICLES
    // ==========================================
    
    /// <summary>
    /// Override this to read input (e.g. moveInputEvent.LastValue). 
    /// Called only for the local driver in Update.
    /// </summary>
    protected virtual void ProcessVehicleInput() { }

    /// <summary>
    /// Override this to apply forces, hovering, or steering. 
    /// Called only for the owning driver in FixedUpdate.
    /// </summary>
    protected virtual void ApplyVehiclePhysics() { }


    // ==========================================
    // INTERACTION & MOUNTING LOGIC
    // ==========================================

    public bool CanInteract(GameObject interactor)
    {
        // Can interact if there are empty seats and the player isn't already in one
        return occupantCount.Value < seats.Length && !isLocalPlayerMounted;
    }

    public void Interact(GameObject interactor)
    {
        if (Time.time - m_LastInteractTime < INTERACT_COOLDOWN) return;
        m_LastInteractTime = Time.time;
        
        NetworkObject playerNetObj = interactor.GetComponent<NetworkObject>();
        if (playerNetObj != null)
        {
            RequestMountRpc(playerNetObj.OwnerClientId);
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestMountRpc(ulong clientId)
    {
        // Find the first available seat
        int availableSeat = -1;
        for (int i = 0; i < seats.Length; i++)
        {
            if (!serverSeatMap.ContainsValue(i))
            {
                availableSeat = i;
                break;
            }
        }

        if (availableSeat != -1)
        {
            serverSeatMap[clientId] = availableSeat;
            occupantCount.Value = serverSeatMap.Count;

            var playerObj = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;
            playerObj.TrySetParent(NetworkObject, false);

            // If taking the driver seat (index 0), transfer vehicle ownership to the client
            if (availableSeat == 0)
            {
                NetworkObject.ChangeOwnership(clientId);
            }

            // Tell this specific client to lock their camera and controls
            LockPlayerClientRpc(availableSeat, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void LockPlayerClientRpc(int seatIndex, RpcParams rpcParams = default)
    {
        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
        isLocalPlayerMounted = true;
        isLocalPlayerDriver = (seatIndex == 0);
        Transform targetSeat = seats[seatIndex];

        // 1. Disable physics and movement (fixes jitter via Interpolate = false)
        if (localPlayer.TryGetComponent<CharacterController>(out var controller)) { controller.enabled = false; }
        foreach (Collider col in localPlayer.GetComponentsInChildren<Collider>()) { col.enabled = false; }
        
        if (localPlayer.TryGetComponent<CoreMovement>(out var playerMovement))
        {
            playerMovement.enabled = false;
            playerMovement.Interpolate = false; 
        }

        // 2. Snap to assigned seat
        localPlayer.transform.SetPositionAndRotation(targetSeat.position, targetSeat.rotation);

        // 3. Setup Camera Anchor
        if (localPlayer.TryGetComponent<CoreCameraController>(out var camController))
        {
            camController.RotationAnchor = transform; 
            camController.SetHorizontalLookAngle(0f);
        }

        // 4. Driver-specific visual setup
        if (isLocalPlayerDriver)
        {
            GameObject freeLookObj = GameObject.Find("[BB] FreeLook(Clone)");
            if (freeLookObj != null)
            {
                var freeLookCam = freeLookObj.GetComponent<Unity.Cinemachine.CinemachineCamera>();
                if (freeLookCam != null) activeCameraBody = freeLookCam.GetComponent<Unity.Cinemachine.CinemachineThirdPersonFollow>();
                // Save the camera's original damping settings
                if (activeCameraBody != null)
                {
                    originalCameraDamping = activeCameraBody.Damping;
                }
            }

            if (speedometerCanvas) speedometerCanvas.SetActive(true);
            else if (speedometerText) speedometerText.gameObject.SetActive(true);
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestDismountRpc(ulong clientId)
    {
        if (serverSeatMap.TryGetValue(clientId, out int seatIndex))
        {
            var playerObj = NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject;
            playerObj.TryRemoveParent();

            serverSeatMap.Remove(clientId);
            occupantCount.Value = serverSeatMap.Count;

            // If the driver dismounted, give ownership back to the server
            if (seatIndex == 0)
            {
                NetworkObject.ChangeOwnership(NetworkManager.ServerClientId);
            }

            UnlockPlayerClientRpc(RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }
    }

    [Rpc(SendTo.SpecifiedInParams)]
    private void UnlockPlayerClientRpc(RpcParams rpcParams = default)
    {
        var localPlayer = NetworkManager.Singleton.LocalClient.PlayerObject;
        isLocalPlayerMounted = false;
        isLocalPlayerDriver = false;

        // 1. Re-enable physics and movement
        if (localPlayer.TryGetComponent<CharacterController>(out var controller)) { controller.enabled = true; }
        foreach (Collider col in localPlayer.GetComponentsInChildren<Collider>(true)) { col.enabled = true; }
        
        if (localPlayer.TryGetComponent<CoreMovement>(out var playerMovement))
        {
            playerMovement.enabled = true;
            playerMovement.Interpolate = true; 
        }

        // 2. Eject the player slightly to the side (uses vehicle right vector so passengers don't clip)
        localPlayer.transform.position += transform.right * 2f;

        // 3. Remove Camera Anchor
        if (localPlayer.TryGetComponent<CoreCameraController>(out var camController))
        {
            camController.RotationAnchor = null;
            camController.SetHorizontalLookAngle(camController.transform.eulerAngles.y);
        }

        // 4. Cleanup visual references
        //Restore the original walking damping
        activeCameraBody.Damping = originalCameraDamping;
        activeCameraBody = null;
        if (speedometerCanvas) speedometerCanvas.SetActive(false);
        else if (speedometerText) speedometerText.gameObject.SetActive(false);
    }

    private void ProcessDriverVisuals()
    {
        if (activeCameraBody != null)
        {
            float currentSpeed = rb.linearVelocity.magnitude;
            float speedPercentage = Mathf.InverseLerp(0f, topSpeed, currentSpeed);
            float targetDamping = Mathf.Lerp(maxDamping, minDamping, speedPercentage);
            activeCameraBody.Damping = new Vector3(targetDamping, targetDamping, targetDamping);
        }

        if (speedometerText != null)
        {
            float currentSpeed = rb.linearVelocity.magnitude * speedConversionRate;
            speedometerText.text = $"{Mathf.RoundToInt(currentSpeed)} {speedUnitLabel}";
        }
    }
}