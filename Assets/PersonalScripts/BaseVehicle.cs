using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Core;
using UnityEngine.InputSystem;
using Unity.Cinemachine;
using TMPro;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody))]
public class BaseVehicle : NetworkBehaviour, IInteractable
{
    [Header("Seat Configuration")]
    [Tooltip("Index 0 is ALWAYS the Driver. Subsequent indices are passengers.")]
    public Transform[] seats;

    public bool IsLocalPlayerDriver => isLocalPlayerDriver;
    
    // Server-side dictionary tracking which ClientID is in which seat index
    private Dictionary<ulong, int> serverSeatMap = new Dictionary<ulong, int>();
    
    // Networked variable so clients know if the vehicle is full before interacting
    private NetworkVariable<int> occupantCount = new NetworkVariable<int>(0);

    [Header("Camera & UI")]
    [Tooltip("Enable to explicitly set the Camera's Follow and LookAt targets to the Camera Anchor.")]
    public bool strictCameraFollow = false;
    [Tooltip("If true, the camera rotates with the vehicle. If false, free look is preserved.")]
    public bool anchorCameraRotation = true;
    [Tooltip("Transform used to lock the camera's view. If left blank, it defaults to the vehicle root.")]
    public Transform cameraAnchor;
    
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
    
    // Camera Caching
    private Unity.Cinemachine.CinemachineCamera activeCam;
    private Unity.Cinemachine.CinemachineThirdPersonFollow activeCameraBody;
    private Vector3 originalCameraDamping;
    private Transform originalFollow;
    private Transform originalLookAt;
    protected CoreCameraController cachedCamController;
    private bool isCameraOverridden = false;

    // --- IInteractable Implementation ---
    public InteractionTriggerMode TriggerMode => InteractionTriggerMode.OnButtonPress;
    public int Priority => 10;
    public string InteractionPromptText => occupantCount.Value == 0 ? "Drive Vehicle" : "Ride as Passenger";

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.sleepThreshold = 0f; // Prevents the vehicle from going to sleep too quickly
        int interactLayer = LayerMask.NameToLayer(interactableLayerName);
        if (interactLayer == -1)
        {
            Debug.LogWarning($"[BaseVehicle] The layer '{interactableLayerName}' does not exist! Please add it in your Unity Tags and Layers settings.");
        }
        else
        {
            gameObject.layer = interactLayer;
            foreach (Transform seat in seats)
            {
                if (seat != null) seat.gameObject.layer = interactLayer;
            }
        }
    }

    protected virtual void Start() {}

    protected virtual void FixedUpdate()
    {
        if (!IsSpawned) return;
        
        if (IsOwner)
        {


            // 2. Safely check if a driver is present across both Host and Client
            bool hasDriver = (IsServer && serverSeatMap.ContainsValue(0)) || isLocalPlayerDriver;

            if (hasDriver)
            {
                ApplyVehiclePhysics();
            } else
            {
                ApplyAlwaysPhysics();
            }
        }
    }

    protected virtual void Update()
    {
        if (!IsSpawned) return;
        
        // 1. Handle UI, Camera, and Inputs (Driver Only)
        //Debug.Log((isLocalPlayerMounted, isLocalPlayerDriver));
        if (isLocalPlayerMounted && isLocalPlayerDriver)
        {
            ProcessDriverVisuals();
            ProcessVehicleInput();
            HandleCameraToggle();
        }
        
        // 2. Handle Dismounting (Driver and Passengers)
        if (isLocalPlayerMounted && Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (Time.time - m_LastInteractTime < INTERACT_COOLDOWN) return;
            m_LastInteractTime = Time.time;
            
            RequestDismountRpc(NetworkManager.Singleton.LocalClientId);
        }
    }

    protected virtual void ProcessVehicleInput() { }
    protected virtual void ApplyAlwaysPhysics() { }
    protected virtual void ApplyVehiclePhysics() { }

    public bool CanInteract(GameObject interactor)
    {
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
            
            if (availableSeat == 0)
            {
                NetworkObject.ChangeOwnership(clientId);
            }
            
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
        
        // 1. Disable physics and movement
        if (localPlayer.TryGetComponent<CharacterController>(out var controller)) { controller.enabled = false; }
        foreach (Collider col in localPlayer.GetComponentsInChildren<Collider>()) { col.enabled = false; }
        
        if (localPlayer.TryGetComponent<CoreMovement>(out var playerMovement))
        {
            playerMovement.enabled = false;
            playerMovement.Interpolate = false; 
        }
        
        // 2. Snap to assigned seat
        localPlayer.transform.SetPositionAndRotation(targetSeat.position, targetSeat.rotation);
        
        // 3. Setup Camera Controller
        if (localPlayer.TryGetComponent<CoreCameraController>(out var camController))
        {
            cachedCamController = camController;
            Transform targetAnchor = cameraAnchor != null ? cameraAnchor : transform;
            
            // Only set the Rotation Anchor if we want the camera to turn with the vehicle
            if (anchorCameraRotation)
            {
                cachedCamController.RotationAnchor = targetAnchor;
                
                // 0 degrees relative to the vehicle anchor
                cachedCamController.SetHorizontalLookAngle(0f); 
            }
            else
            {
                cachedCamController.RotationAnchor = null;
                
                // Snap the free-look camera behind the vehicle using its world Y rotation
                cachedCamController.SetHorizontalLookAngle(transform.eulerAngles.y); 
            }

            if (isLocalPlayerDriver && strictCameraFollow)
            {
                cachedCamController.SwitchCameraMode("FlightMode");

                // DYNAMICALLY TELL ALL CAMERAS TO FOLLOW THE PLANE ANCHOR
                cachedCamController.OverrideCameraTargets(targetAnchor);
            }

            // --- THE NEW FIX: Manually assign the Flight Cam targets once! ---
            var allCams = FindObjectsByType<Unity.Cinemachine.CinemachineCamera>(
                FindObjectsInactive.Include
            );
            
            foreach (var cam in allCams)
            {
                // Note: Ensure this matches the exact name of your Flight Camera GameObject
                Debug.Log($"[BaseVehicle] Checking camera: {cam.gameObject.name}, matches? {cam.gameObject.name.Contains("FlightCamera")}");
                if (cam.gameObject.name.Contains("FlightCamera"))
                {
                    cam.Follow = targetAnchor;
                    cam.LookAt = targetAnchor;
                }
            }
        }
        
        // 4. Driver-specific visual setup
        if (isLocalPlayerDriver)
        {
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
        
        // 2. Eject the player slightly to the side
        localPlayer.transform.position += transform.right * 2f;
        
        // 3. Restore Camera & Targets
        if (cachedCamController != null)
        {
            cachedCamController.RotationAnchor = null;

            cachedCamController.SwitchCameraMode("FreeLook"); 
            
            // REVERT TARGETS BACK TO THE PLAYER'S NORMAL PIVOT
            cachedCamController.OverrideCameraTargets(null); 
            
            
            cachedCamController.SetHorizontalLookAngle(cachedCamController.transform.eulerAngles.y);
            cachedCamController = null;
        }
        
        // 4. Cleanup visual references
        if (speedometerCanvas) speedometerCanvas.SetActive(false);
        else if (speedometerText) speedometerText.gameObject.SetActive(false);
    }

    private void HandleCameraToggle()
    {
        Transform targetAnchor = cameraAnchor != null ? cameraAnchor : transform;

        if (strictCameraFollow)
        {
            // Strict Follow Mode: Overwrite Follow and LookAt targets
            if (activeCam != null)
            {
                activeCam.Follow = targetAnchor;
                activeCam.LookAt = targetAnchor;
            }
            
            // Disable the normal rotation anchor so they don't fight
            if (cachedCamController != null)
            {
                cachedCamController.RotationAnchor = null;
            }
            isCameraOverridden = true;
        }
        else
        {
            // Normal Look Mode: Restore Follow/LookAt and rely on the RotationAnchor
            if (isCameraOverridden && activeCam != null)
            {
                activeCam.Follow = originalFollow;
                activeCam.LookAt = originalLookAt;
                isCameraOverridden = false;
            }
            
            if (cachedCamController != null)
            {
                if (anchorCameraRotation && cachedCamController.RotationAnchor != targetAnchor)
                {
                    cachedCamController.RotationAnchor = targetAnchor;
                }
                else if (!anchorCameraRotation && cachedCamController.RotationAnchor != null)
                {
                    cachedCamController.RotationAnchor = null;
                }
            }
        }
    }

    private void ProcessDriverVisuals()
    {
        // --- NEW: DYNAMICALLY FETCH THE ACTIVE CINEMACHINE CAMERA ---
        if (Camera.main != null)
        {
            var brain = Camera.main.GetComponent<CinemachineBrain>();
            if (brain != null && brain.ActiveVirtualCamera != null)
            {
                // Get the camera Cinemachine is currently looking through
                CinemachineCamera currentCam = brain.ActiveVirtualCamera as CinemachineCamera;
                
                // If the camera changed (e.g., switched to FlightMode), update our references!
                if (currentCam != activeCam)
                {
                    activeCam = currentCam;
                    if (activeCam != null)
                    {
                        // Fetch the body component to control damping
                        activeCameraBody = activeCam.GetComponent<CinemachineThirdPersonFollow>();
                    }
                }
            }
        }
        // ------------------------------------------------------------

        if (activeCameraBody != null)
        {
            float currentSpeed = rb.linearVelocity.magnitude;
            float speedPercentage = Mathf.InverseLerp(0f, topSpeed, currentSpeed);
            float targetDamping = Mathf.Lerp(maxDamping, minDamping, speedPercentage);
            
            // Apply the calculated damping directly to the active camera
            activeCameraBody.Damping = new Vector3(targetDamping, targetDamping, targetDamping);
        }

        if (speedometerText != null)
        {
            float currentSpeed = rb.linearVelocity.magnitude * speedConversionRate;
            speedometerText.text = $"{Mathf.RoundToInt(currentSpeed)} {speedUnitLabel}";
        }
    }

    /// <summary>
    /// Safely switches the Cinemachine camera mode for the driver and manages target overrides.
    /// </summary>
    protected void SetDriverCameraMode(string cameraMode, bool overrideTargets)
    {
        // Only run this for the driver who actually owns the camera
        if (cachedCamController == null || !isLocalPlayerDriver) return;

        // 1. Switch the actual Cinemachine Virtual Camera
        cachedCamController.SwitchCameraMode(cameraMode);

        // 2. Handle the Follow/LookAt targets
        if (overrideTargets)
        {
            Transform targetAnchor = cameraAnchor != null ? cameraAnchor : transform;
            cachedCamController.OverrideCameraTargets(targetAnchor);
        }
        else
        {
            // Passing null reverts the camera to follow the Player model naturally
            cachedCamController.OverrideCameraTargets(null);
        }
    }
}