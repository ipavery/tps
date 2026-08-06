using UnityEngine;
using Blocks.Gameplay.Core;

public class PlaneVehicle : BaseVehicle
{
    [Header("Input Integration")]
    [Tooltip("Drag your move event here. Y = Throttle, X = Roll")]
    public Vector2Event moveInputEvent; 
    
    [Tooltip("Drag your look event here. Y = Pitch, X = Yaw")]
    public Vector2Event lookInputEvent; 

    [Header("Control Sensitivity")]
    [Range(0.1f, 10f)]
    public float controlSensitivity = 1f;

    [Header("Plane Physics & Lift")]
    [Tooltip("How much lift is generated based on forward speed squared.")]
    public float liftCoefficient = 0.05f;
    [Tooltip("How much air resistance is applied based on speed squared.")]
    public float dragCoefficient = 0.02f;
    [Tooltip("Maximum engine thrust force.")]
    public float maxThrust = 10000f;
    [Tooltip("How fast the throttle increases/decreases when holding W/S.")]
    public float throttleSpeed = 0.5f;

    [Header("Aerodynamic Grip")]
    [Tooltip("Prevents sideways drifting (simulates the vertical tail stabilizer).")]
    public float lateralGrip = 5f;
    [Tooltip("Prevents vertical drifting when pitching (simulates the flat surface of the wings).")]
    public float verticalGrip = 5f;
    
    [Header("Control Surfaces (Handling)")]
    public float pitchSpeed = 50f;
    public float yawSpeed = 30f;
    public float rollSpeed = 60f;
    
    // Local inputs & states
    private float currentThrottle = 0f;
    private float pitchInput = 0f;
    private float yawInput = 0f;
    private float rollInput = 0f;

    protected override void ProcessVehicleInput()
    {
        // 1. Throttle and Roll from moveInputEvent (W/S and A/D)
        Vector2 keyboardInput = moveInputEvent != null ? moveInputEvent.LastValue : Vector2.zero;
        
        float throttleInput = keyboardInput.y; 
        currentThrottle = Mathf.Clamp01(currentThrottle + (throttleInput * throttleSpeed * Time.deltaTime));
        
        rollInput = keyboardInput.x * controlSensitivity; 

        // 2. Pitch and Yaw from the lookInputEvent (Mouse)
        Vector2 lookInput = lookInputEvent != null ? lookInputEvent.LastValue : Vector2.zero;
        
        yawInput = lookInput.x * controlSensitivity;
        pitchInput = lookInput.y * controlSensitivity; 
    }

    protected override void ApplyVehiclePhysics()
    {
        Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
        float forwardSpeed = Mathf.Max(0, localVelocity.z); 
        float totalSpeed = rb.linearVelocity.magnitude;

        // 1. Apply Thrust
        rb.AddForce(transform.forward * currentThrottle * maxThrust);

        // 2. Apply Lift
        float liftForce = liftCoefficient * (forwardSpeed * forwardSpeed);
        rb.AddForce(transform.up * liftForce);

        // 3. Apply Aerodynamic Drag
        float dragForce = dragCoefficient * (totalSpeed * totalSpeed);
        if (totalSpeed > 0.1f)
        {
            rb.AddForce(-rb.linearVelocity.normalized * dragForce);
        }

        // 4. Apply Steering
        float controlAuthority = Mathf.Clamp(forwardSpeed / 20f, 0f, 1f); 
        rb.AddTorque(transform.right * -pitchInput * pitchSpeed * controlAuthority, ForceMode.Acceleration);
        rb.AddTorque(transform.up * yawInput * yawSpeed * controlAuthority, ForceMode.Acceleration);
        rb.AddTorque(transform.forward * -rollInput * rollSpeed * controlAuthority, ForceMode.Acceleration);

        // 5. Apply Aerodynamic Grip (Prevents drifting)
        Vector3 sidewaysVelocity = Vector3.Project(rb.linearVelocity, transform.right);
        rb.AddForce(-sidewaysVelocity * lateralGrip, ForceMode.Acceleration);

        // We apply slightly less aggressive grip vertically so we don't completely fight our own lift
        Vector3 verticalVelocity = Vector3.Project(rb.linearVelocity, transform.up);
        rb.AddForce(-verticalVelocity * (verticalGrip * controlAuthority), ForceMode.Acceleration);
    }
}