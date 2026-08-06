using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Core;
using UnityEngine.InputSystem;
using Unity.Cinemachine;

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

        // A. Disable the CharacterController to stop physics sliding
        if (localPlayer.TryGetComponent<CharacterController>(out var controller))
        {
            controller.enabled = false;
        }

        // B. Find and disable EVERY collider on the player and its children (arms, legs, etc.)
        Collider[] allColliders = localPlayer.GetComponentsInChildren<Collider>();
        foreach (Collider col in allColliders)
        {
            col.enabled = false;
        }

        // B. Disable the standard movement script (Update 'ThirdPersonController' to your script's name)
        if (localPlayer.TryGetComponent<CoreMovement>(out var playerMovement))
        {
            playerMovement.enabled = false;
        }

        // C. Snap the local position and rotation exactly to the seat
        localPlayer.transform.SetPositionAndRotation(seatTransform.position, seatTransform.rotation);

        // 2. Find the Free Look Camera by its exact Hierarchy name
        GameObject freeLookObj = GameObject.Find("[BB] FreeLook(Clone)");
        if (freeLookObj != null)
        {
            var freeLookCam = freeLookObj.GetComponent<Unity.Cinemachine.CinemachineCamera>();
            if (freeLookCam != null)
            {
                freeLookCam.Follow = seatTransform;
                freeLookCam.LookAt = seatTransform;
            }
        }

        // 3. Find the Aim Camera by its exact Hierarchy name
        GameObject aimCamObj = GameObject.Find("[BB] Aim(Clone)");
        if (aimCamObj != null)
        {
            var aimCam = aimCamObj.GetComponent<Unity.Cinemachine.CinemachineCamera>();
            if (aimCam != null)
            {
                aimCam.Follow = seatTransform;
                aimCam.LookAt = seatTransform;
            }
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

        // A. Re-enable the CharacterController
        if (localPlayer.TryGetComponent<CharacterController>(out var controller))
        {
            controller.enabled = true;
        }

        // B. Find and re-enable EVERY collider on the player and its children
        Collider[] allColliders = localPlayer.GetComponentsInChildren<Collider>(true); 
        foreach (Collider col in allColliders)
        {
            col.enabled = true;
        }

        // B. Re-enable the standard movement script
        if (localPlayer.TryGetComponent<CoreMovement>(out var playerMovement))
        {
            playerMovement.enabled = true;
        }

        // C. (Crucial) Move the player slightly to the side so they don't clip inside the vehicle and get stuck
        localPlayer.transform.position += localPlayer.transform.right * 2f;

        // 2. Revert the Free Look Camera back to the player
        GameObject freeLookObj = GameObject.Find("[BB] FreeLook(Clone)");
        if (freeLookObj != null)
        {
            var freeLookCam = freeLookObj.GetComponent<Unity.Cinemachine.CinemachineCamera>();
            if (freeLookCam != null)
            {
                freeLookCam.Follow = localPlayer.transform;
                freeLookCam.LookAt = localPlayer.transform;
            }
        }

        // 3. Revert the Aim Camera back to the player
        GameObject aimCamObj = GameObject.Find("[BB] Aim(Clone)");
        if (aimCamObj != null)
        {
            var aimCam = aimCamObj.GetComponent<Unity.Cinemachine.CinemachineCamera>();
            if (aimCam != null)
            {
                aimCam.Follow = localPlayer.transform;
                aimCam.LookAt = localPlayer.transform;
            }
        }
    }
}