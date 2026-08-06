using UnityEngine;
using Blocks.Gameplay.Core;

// By inheriting from BaseVehicle, you get networking, seats, UI, and cameras for free.
public class HoverVehicle2 : BaseVehicle 
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

    // We store the input in Update...
    private float accelInput;
    private float turnInput;

    protected override void ProcessVehicleInput()
    {
        // This is only called for the driver.
        Vector2 input = moveInputEvent != null ? moveInputEvent.LastValue : Vector2.zero;
        accelInput = input.y;
        turnInput = input.x;
    }

    protected override void ApplyVehiclePhysics()
    {
        // This is only called on the server/owner.
        ApplyHoverPhysics();
        ApplyPropulsionAndSteering();
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
        rb.AddForce(transform.forward * accelInput * forwardThrust);
        rb.AddTorque(transform.up * turnInput * turnTorque);

        Vector3 sidewaysVelocity = Vector3.Project(rb.linearVelocity, transform.right);
        rb.AddForce(-sidewaysVelocity * lateralGrip, ForceMode.Acceleration);
    }
}