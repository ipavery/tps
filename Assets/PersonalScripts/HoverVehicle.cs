using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Core;

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
    private NetworkVariable<bool> isMounted = new NetworkVariable<bool>(false);

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
            NetworkObject.ChangeOwnership(clientId);
            isMounted.Value = true;
        }
    }
}