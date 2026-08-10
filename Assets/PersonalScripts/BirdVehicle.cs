using UnityEngine;
using Blocks.Gameplay.Core;

public class BirdVehicle : BaseVehicle
{
    [Header("Input Integration")]
    public Vector2Event moveInputEvent;
    public Transform cameraTransform; // Used to determine which way the player is pointing

    [Header("State")]
    public bool isWalking = false;
    public float walkEngageDistance = 2.5f;
    public LayerMask groundLayer;

    [Header("Hover / Leg Physics")]
    public Transform[] legPoints; // Assign your two leg objects here
    public float hoverHeight = 1.0f;
    public float springConstant = 5000f;
    public float dampingConstant = 500f;

    [Header("Upright Stabilization")]
    public float uprightTorque = 5000f;
    public float uprightDamping = 500f;

    [Header("Walking Movement")]
    public float walkAcceleration = 3000f;
    public float rotationTorque = 2000f;
    [Tooltip("Drag applied only during walking mode to limit top speed without hard-capping velocity.")]
    public float walkingDrag = 3f; 
    public float walkingAngularDrag = 5f;

    // Cache inputs
    private Vector2 moveInput;

    protected override void ProcessVehicleInput()
    {
        // Capture WASD input for the driver
        moveInput = moveInputEvent != null ? moveInputEvent.LastValue : Vector2.zero;
        
        // Try to automatically find the main camera if it's not assigned
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    protected override void ApplyAlwaysPhysics()
    {
        CheckWalkingState();

        if (isWalking)
        {
            ApplyUprightStabilization();
            ApplyHoverLegs();
        }
    }

    protected override void ApplyVehiclePhysics()
    {
        CheckWalkingState();

        if (isWalking)
        {
            ApplyUprightStabilization();
            ApplyHoverLegs();
            ApplyWalkingMovement();
            ApplyRotation();
        }
    }

    private void CheckWalkingState()
    {
        bool groundDetected = false;

        // Check if either leg is close enough to the ground to engage walking mode
        foreach (Transform leg in legPoints)
        {
            if (Physics.Raycast(leg.position, Vector3.down, out RaycastHit hit, walkEngageDistance, groundLayer))
            {
                groundDetected = true;
                break;
            }
        }

        isWalking = groundDetected;

        // Adjust Rigidbody drag dynamically to simulate the walking friction/top speed
        if (isWalking)
        {
            rb.linearDamping = walkingDrag; 
            rb.angularDamping = walkingAngularDrag;
        }
        else
        {
            // Free-fall / simple rigidbody mode when in the air
            rb.linearDamping = 0.1f;
            rb.angularDamping = 0.1f;
        }
    }

    private void ApplyHoverLegs()
    {
        // Reuses the hover logic to act as the bird's legs keeping it above ground
        foreach (Transform point in legPoints)
        {
            if (Physics.Raycast(point.position, Vector3.down, out RaycastHit hit, hoverHeight * 2f, groundLayer))
            {
                Vector3 pointVelocity = rb.GetPointVelocity(point.position);
                float compression = hoverHeight - hit.distance;
                float upwardSpeed = Vector3.Dot(pointVelocity, Vector3.up); // Ensure it pushes directly up

                float hoverForce = (compression * springConstant) - (upwardSpeed * dampingConstant);
                rb.AddForceAtPosition(Vector3.up * hoverForce, point.position);
                Debug.DrawLine(point.position, Vector3.up * hoverForce / 1000f + point.position, Color.green);
            }
        }
    }

    private void ApplyUprightStabilization()
    {
        // Cross product calculates the axis and magnitude needed to rotate transform.up towards Vector3.up
        Vector3 cross = Vector3.Cross(transform.up, Vector3.up);
        
        // Apply torque to right the vehicle, minus damping to prevent infinite wobbling
        Vector3 torque = (cross * uprightTorque) - (rb.angularVelocity * uprightDamping);
        rb.AddTorque(torque, ForceMode.Acceleration);
    }

    private void ApplyWalkingMovement()
    {
        if (moveInput.sqrMagnitude < 0.01f) return;

        Vector3 moveDirection;

        // If we have a camera reference, calculate movement relative to the camera's XZ plane
        if (cameraTransform != null)
        {
            Vector3 camForward = cameraTransform.forward;
            Vector3 camRight = cameraTransform.right;
            
            camForward.y = 0;
            camRight.y = 0;

            moveDirection = (camForward.normalized * moveInput.y) + (camRight.normalized * moveInput.x);
        }
        else
        {
            // Fallback to local space movement if no camera is found
            moveDirection = (transform.forward * moveInput.y) + (transform.right * moveInput.x);
        }

        // Apply constant force; the walkingDrag will naturally cap the top speed
        rb.AddForce(moveDirection.normalized * walkAcceleration, ForceMode.Acceleration);
    }

    private void ApplyRotation()
    {
        // Point the bird in the direction of the camera (XZ axis only)
        if (cameraTransform != null)
        {
            Vector3 targetForward = cameraTransform.forward;
            targetForward.y = 0; // Flatten to XZ plane

            if (targetForward.sqrMagnitude > 0.001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(targetForward, Vector3.up);
                
                // Calculate torque required to turn towards the target rotation
                float angle = Quaternion.Angle(transform.rotation, targetRotation);
                if (angle > 1f)
                {
                    Vector3 cross = Vector3.Cross(transform.forward, targetForward.normalized);
                    rb.AddTorque(cross * rotationTorque, ForceMode.Acceleration);
                }
            }
        }
    }
}