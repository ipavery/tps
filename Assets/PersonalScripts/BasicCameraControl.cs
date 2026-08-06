using UnityEngine;

public class BasicCameraControl : MonoBehaviour
{
    [Header("Targeting")]
    public Transform targetSled;
    public Vector3 offset = new Vector3(0, 10f, -15f); // High up and behind
    public Vector3 lookDownAngle = new Vector3(20f, 0, 0);

    public enum UpdateTiming { Update, LateUpdate, FixedUpdate }
    
    [Header("Settings")]
    public UpdateTiming updateMode = UpdateTiming.LateUpdate;

    private void Start()
    {
        // Lock the camera rotation to look down at the track
        transform.rotation = Quaternion.Euler(lookDownAngle);
    }

    private void Update()
    {
        if (updateMode == UpdateTiming.Update) SnapToTarget();
    }

    private void LateUpdate()
    {
        if (updateMode == UpdateTiming.LateUpdate) SnapToTarget();
    }

    private void FixedUpdate()
    {
        if (updateMode == UpdateTiming.FixedUpdate) SnapToTarget();
    }

    private void SnapToTarget()
    {
        if (targetSled != null)
        {
            // Pure, 1:1 mathematical lock. Zero damping.
            transform.position = targetSled.position + offset;
        }
    }
}