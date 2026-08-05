using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Core; // Added your framework's namespace

[RequireComponent(typeof(Rigidbody))]
public class HoverVehicle : NetworkBehaviour
{
    [Header("Input Integration")]
    [Tooltip("Drag the 'onMoveInput' Vector2Event ScriptableObject here.")]
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
        // Read input directly from your framework's cached event value
        Vector2 input = moveInputEvent != null ? moveInputEvent.LastValue : Vector2.zero;
        
        float accelInput = input.y; // W/S or Left Stick Up/Down
        float turnInput = input.x;  // A/D or Left Stick Left/Right

        rb.AddForce(transform.forward * accelInput * forwardThrust);
        rb.AddTorque(transform.up * turnInput * turnTorque);

        Vector3 sidewaysVelocity = Vector3.Project(rb.linearVelocity, transform.right);
        rb.AddForce(-sidewaysVelocity * lateralGrip, ForceMode.Acceleration);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer && !IsOwner) return; 

        if (!isMounted.Value && collision.gameObject.CompareTag("Player"))
        {
            NetworkObject playerNetObj = collision.gameObject.GetComponent<NetworkObject>();
            if (playerNetObj != null)
            {
                RequestMountRpc(playerNetObj.OwnerClientId);
            }
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestMountRpc(ulong clientId)
    {
        if (!isMounted.Value)
        {
            NetworkObject.ChangeOwnership(clientId);
            isMounted.Value = true;
        }
    }
}