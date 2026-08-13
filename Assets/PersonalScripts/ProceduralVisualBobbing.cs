using UnityEngine;

public class ProceduralVisualBobbing : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The main vehicle Rigidbody used to calculate speed.")]
    public Rigidbody vehicleRb;

    public BirdVehicle birdVehicle; //to know when to stop oscillating when flying

    [Header("Bobbing Settings")]
    [Tooltip("How fast the bobbing cycles relative to movement speed.")]
    public float bobSpeedMultiplier = 2f; 
    
    [Tooltip("How high the body bounces (meters).")]
    public float verticalBobAmount = 0.15f; 
    
    [Tooltip("How much the body sways side-to-side (degrees).")]
    public float rollAmount = 3f; 
    
    [Tooltip("How much the body nods forward with each step (degrees).")]
    public float pitchAmount = 1.5f;

    private Vector3 originalLocalPos;
    private Quaternion originalLocalRot;
    
    // We track 'distance' rather than time so the bobbing speeds up/slows down naturally
    private float distanceTraveled = 0f;

    private void Start()
    {
        // Cache the resting position and rotation of the mesh
        originalLocalPos = transform.localPosition;
        originalLocalRot = transform.localRotation;
    }

    private void Update()
    {
        if (vehicleRb == null) return;

        // 1. Get horizontal speed
        Vector3 horizontalVelocity = vehicleRb.linearVelocity;
        horizontalVelocity.y = 0;
        float speed = horizontalVelocity.magnitude;

        // 2. Advance the wave if moving, or smoothly rest if stopped
        if (speed > 0.1f && birdVehicle.isWalking)
        {
            distanceTraveled += speed * Time.deltaTime * bobSpeedMultiplier;
        }
        else
        {
            // Smoothly ease the wave back to a resting state (0 or PI)
            // Modulo math helps find the closest resting point to prevent snapping
            float closestRest = Mathf.Round(distanceTraveled / Mathf.PI) * Mathf.PI;
            distanceTraveled = Mathf.Lerp(distanceTraveled, closestRest, Time.deltaTime * 5f);
        }

        // 3. Calculate Sine waves
        // Use absolute Sine for vertical & pitch so it bounces UP and nods DOWN twice per stride (once per foot)
        float verticalOffset = Mathf.Abs(Mathf.Sin(distanceTraveled)) * verticalBobAmount;
        float pitchOffset = Mathf.Abs(Mathf.Sin(distanceTraveled)) * pitchAmount;
        
        // Use regular Sine for roll so it sways LEFT and RIGHT once per stride
        float rollOffset = Mathf.Sin(distanceTraveled) * rollAmount;

        // 4. Apply offsets to the target transforms
        Vector3 targetPos = originalLocalPos + new Vector3(0, verticalOffset, 0);
        Quaternion targetRot = originalLocalRot * Quaternion.Euler(pitchOffset, 0, rollOffset);

        // 5. Smoothly apply the final transforms to the mesh
        transform.localPosition = Vector3.Lerp(transform.localPosition, targetPos, Time.deltaTime * 15f);
        transform.localRotation = Quaternion.Slerp(transform.localRotation, targetRot, Time.deltaTime * 15f);
    }
}