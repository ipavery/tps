using UnityEngine;

public class BirdLegIK : MonoBehaviour
{
    [Header("Joints (Empty GameObjects)")]
    public Transform hipJoint;
    public Transform kneeJoint;
    public Transform footJoint;
    
    [Header("Visuals")]
    [Tooltip("The actual foot model that should sit flat on the ground.")]
    public Transform footMesh;

    [Header("Raycast Settings")]
    [Tooltip("The physics origin for the hover (from BirdVehicle) to match the length.")]
    public Transform hoverOrigin;
    public float maxLegLength = 3f;
    public LayerMask groundLayer;
    
    [Tooltip("True for bird knees (which are technically ankles and bend backwards). False for human knees.")]
    public bool bendBackward = true; 

    // We calculate these automatically on start based on your Editor setup
    private float thighLength;
    private float shinLength;

    private void Start()
    {
        // Automatically measure the lengths between the joints you placed in the scene
        if (hipJoint != null && kneeJoint != null && footJoint != null)
        {
            thighLength = Vector3.Distance(hipJoint.position, kneeJoint.position);
            shinLength = Vector3.Distance(kneeJoint.position, footJoint.position);
        }
    }

    private void LateUpdate()
    {
        if (hipJoint == null || kneeJoint == null || footJoint == null) return;

        // 1. Raycast to find where the foot should plant
        Vector3 targetFootPos = hoverOrigin.position - (hoverOrigin.up * maxLegLength);
        Vector3 targetFootNormal = Vector3.up;

        if (Physics.Raycast(hoverOrigin.position, -hoverOrigin.up, out RaycastHit hit, maxLegLength, groundLayer))
        {
            targetFootPos = hit.point;
            targetFootNormal = hit.normal;
        }

        // 2. Calculate the distance and vector from Hip to Foot
        Vector3 hipToTarget = targetFootPos - hipJoint.position;
        float distance = hipToTarget.magnitude;
        Vector3 kneePos;

        // 3. Solve the Knee Position
        if (distance >= thighLength + shinLength)
        {
            // The foot is too far! Stretch the leg straight out.
            kneePos = hipJoint.position + hipToTarget.normalized * thighLength;
        }
        else
        {
            // The foot is close enough to bend the knee using Law of Cosines
            float a = thighLength;
            float b = shinLength;
            float c = distance;

            // Calculate the angle in radians, then convert to degrees
            float angleRad = Mathf.Acos((a * a + c * c - b * b) / (2f * a * c));
            float angleDeg = angleRad * Mathf.Rad2Deg;

            // 1. Determine which way the knee should try to point
            Vector3 kneeAimDirection = bendBackward ? -transform.forward : transform.forward;
            
            // 2. Create a dynamic hinge axis perpendicular to the leg line and the aim direction
            Vector3 bendAxis = Vector3.Cross(hipToTarget.normalized, kneeAimDirection);
            
            // 3. Fallback just in case the leg is completely perfectly straight forward/backward
            if (bendAxis.sqrMagnitude < 0.001f)
            {
                bendAxis = transform.right;
            }
            else
            {
                bendAxis = bendAxis.normalized;
            }

            // Rotate the straight-line vector by our calculated angle to find the knee direction
            Vector3 kneeDir = Quaternion.AngleAxis(angleDeg, bendAxis) * hipToTarget.normalized;
            kneePos = hipJoint.position + kneeDir * thighLength;
        }

        // 4. Apply Positions
        kneeJoint.position = kneePos;
        footJoint.position = targetFootPos;

        // 5. Aim the joints at their targets (assuming the meshes are built pointing down the Z axis)
        // If your meshes look weird, just rotate the Mesh child objects in the Unity Editor until they align!
        hipJoint.rotation = Quaternion.LookRotation(kneePos - hipJoint.position, transform.forward);
        kneeJoint.rotation = Quaternion.LookRotation(targetFootPos - kneePos, transform.forward);
        
        Debug.DrawLine(kneeJoint.position, targetFootPos, Color.red, 0.1f);
        // 6. Snap the visual foot to the floor and align it with the ground slope
        if (footMesh != null)
        {
            footMesh.position = targetFootPos;
            footMesh.rotation = Quaternion.FromToRotation(transform.up, targetFootNormal) * transform.rotation;
        }
    }
}