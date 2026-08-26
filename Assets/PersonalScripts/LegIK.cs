using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;

[CustomEditor(typeof(LegIK))]
public class LegIKEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // Draw the normal inspector first
        DrawDefaultInspector();

        LegIK sourceLeg = (LegIK)target;

        EditorGUILayout.Space();
        
        // Create the button
        if (GUILayout.Button("Copy Settings to All Legs in 'Visuals'", GUILayout.Height(30)))
        {
            CopySettingsToAll(sourceLeg);
        }
    }

    private void CopySettingsToAll(LegIK sourceLeg)
    {
        // Find the "Visuals" object at the root of the vehicle
        Transform visuals = sourceLeg.transform.root.Find("Visuals");
        if (visuals == null)
        {
            Debug.LogWarning("Could not find an object named 'Visuals' under the root!");
            return;
        }

        // Get all legs within the Visuals object
        LegIK[] allLegs = visuals.GetComponentsInChildren<LegIK>();
        
        // Register the undo action so you can Ctrl+Z if you make a mistake
        Undo.RecordObjects(allLegs, "Copy Leg IK Settings");

        int copyCount = 0;
        foreach (var targetLeg in allLegs)
        {
            if (targetLeg == sourceLeg) continue; // Don't copy to itself

            // Copy all the value types (excluding references like Transforms/Rigidbodies)
            targetLeg.maxLegLength = sourceLeg.maxLegLength;
            targetLeg.groundLayer = sourceLeg.groundLayer;
            targetLeg.bendBackward = sourceLeg.bendBackward;
            
            targetLeg.minStepSpeed = sourceLeg.minStepSpeed;
            targetLeg.stepSpeedMultiplier = sourceLeg.stepSpeedMultiplier;
            targetLeg.stepHeight = sourceLeg.stepHeight;
            targetLeg.stepDistance = sourceLeg.stepDistance;
            targetLeg.velocityAnticipation = sourceLeg.velocityAnticipation;
            targetLeg.stepCooldown = sourceLeg.stepCooldown;
            targetLeg.restThreshold = sourceLeg.restThreshold;
            
            targetLeg.stepPushForce = sourceLeg.stepPushForce;

            // Mark the object as changed so Unity saves it
            EditorUtility.SetDirty(targetLeg);
            copyCount++;
        }

        Debug.Log($"Successfully copied IK settings from {sourceLeg.gameObject.name} to {copyCount} other legs.");
    }
}
#endif


public class LegIK : MonoBehaviour
{
    [Header("Joints (Empty GameObjects)")]
    public Transform hipJoint;
    public Transform kneeJoint;
    public Transform footJoint;

    [Header("Visuals")]
    public Transform footMesh;

    [Header("Raycast Settings")]
    public Transform hoverOrigin;
    public float maxLegLength = 3f;
    public LayerMask groundLayer;
    public bool bendBackward = true;

    public float minStepSpeed = 6f;
    public float stepSpeedMultiplier = 1.5f;
    public float stepHeight = 0.5f;

    public bool canStep = false;

    public float stepDistance = 1.2f;
    public float velocityAnticipation = 0.3f;
    public float stepCooldown = 0.15f;
    public float restThreshold = 0.4f;

    [Header("Physics Interaction")]
    public Rigidbody vehicleRb;
    public float stepPushForce = 1500f;

    public bool isStepping { get; private set; } = false;
    public float lastStepEndTime { get; private set; } = 0f;

    private float thighLength;
    private float shinLength;

    private Vector3 currentPlantedPos;
    private Vector3 currentPlantedNormal = Vector3.up;
    private Vector3 stepStartPos;
    private Vector3 stepTargetPos;
    public float stepProgress = 0f; //exposed for GaitManager to read
    private float currentActiveStepSpeed = 8f;

    private Vector3 lastBodyPos;
    private Vector3 bodyVelocity;

    private void Start()
    {
        if (hipJoint != null && kneeJoint != null && footJoint != null)
        {
            thighLength = Vector3.Distance(hipJoint.position, kneeJoint.position);
            shinLength = Vector3.Distance(kneeJoint.position, footJoint.position);
        }

        currentPlantedPos = footJoint.position;
        lastBodyPos = hoverOrigin.position;
    }

    private void LateUpdate()
    {
        if (hipJoint == null || kneeJoint == null || footJoint == null) return;

        bodyVelocity = (hoverOrigin.position - lastBodyPos) / Time.deltaTime;
        lastBodyPos = hoverOrigin.position;
        // ----------------------------

        GetIdealFootPosition(out Vector3 idealPos, out Vector3 idealNormal);

        if (!isStepping)
        {
            CheckAndStartStep(idealPos, idealNormal);
        }

        Vector3 activeFootPos = currentPlantedPos;
        if (isStepping)
        {
            activeFootPos = AnimateStep();
        }

        SolveIK(activeFootPos, idealNormal);
    }

    private void GetIdealFootPosition(out Vector3 pos, out Vector3 normal)
    {
        // 2. NORMAL WALKING MODE
        pos = hoverOrigin.position - (hoverOrigin.up * maxLegLength);
        normal = Vector3.up;

        if (Physics.Raycast(hoverOrigin.position, -hoverOrigin.up, out RaycastHit hit, maxLegLength, groundLayer))
        {
            pos = hit.point;
            normal = hit.normal;
        }
    }

    public float GetPredictedDistanceToIdeal()
    {
        GetIdealFootPosition(out Vector3 idealPos, out _);
        Vector3 predictedIdeal = idealPos + (bodyVelocity * velocityAnticipation);
        return Vector3.Distance(currentPlantedPos, predictedIdeal);
    }

    private void CheckAndStartStep(Vector3 idealPos, Vector3 idealNormal)
    {
        if (!canStep) return;

        float currentSpeed = bodyVelocity.magnitude;
        bool isStopped = currentSpeed < 0.5f;

        Vector3 predictedIdeal = idealPos + (bodyVelocity * velocityAnticipation);
        float distanceToPredicted = Vector3.Distance(currentPlantedPos, predictedIdeal);

        float threshold = isStopped ? restThreshold : stepDistance;

        if (distanceToPredicted > threshold)
        {

            isStepping = true;
            stepProgress = 0f;
            stepStartPos = currentPlantedPos;
            currentPlantedNormal = idealNormal;
            currentActiveStepSpeed = Mathf.Max(minStepSpeed, currentSpeed * stepSpeedMultiplier);
            //Debug.Log($"Step speed is {currentActiveStepSpeed}");

            if (isStopped)
            {
                stepTargetPos = idealPos;
            }
            else
            {
                Vector3 moveDir = bodyVelocity.normalized;
                moveDir.y = 0;
                Vector3 projectedTarget = predictedIdeal + (moveDir * (stepDistance * 0.25f));

                // GROUND CLAMPING: Raycast from the vehicle's height down to the projected X/Z coordinate
                Vector3 rayOrigin = new Vector3(projectedTarget.x, hoverOrigin.position.y, projectedTarget.z);

                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, maxLegLength * 2f, groundLayer))
                {
                    stepTargetPos = hit.point;
                    currentPlantedNormal = hit.normal;
                }
                else
                {
                    // Fallback if the step is hanging off a massive cliff
                    stepTargetPos = projectedTarget;
                }
            }
        }
    }

    private Vector3 AnimateStep()
    {
        stepProgress += Time.deltaTime * currentActiveStepSpeed;

        if (vehicleRb != null)
        {
            Vector3 pushDirection = (Vector3.up + bodyVelocity.normalized).normalized;
            vehicleRb.AddForceAtPosition(pushDirection * stepPushForce * Time.deltaTime, stepStartPos, ForceMode.Force);
        }

        if (stepProgress >= 1f)
        {
            stepProgress = 1f;
            isStepping = false;
            lastStepEndTime = Time.time;
            currentPlantedPos = stepTargetPos;
            return currentPlantedPos;
        }

        Vector3 currentPos = Vector3.Lerp(stepStartPos, stepTargetPos, stepProgress);
        currentPos.y += Mathf.Sin(stepProgress * Mathf.PI) * stepHeight;

        return currentPos;
    }

    private void SolveIK(Vector3 targetFootPos, Vector3 targetNormal)
    {
        Vector3 hipToTarget = targetFootPos - hipJoint.position;
        float distance = hipToTarget.magnitude;
        float maxReach = thighLength + shinLength;
        Vector3 kneePos;

        // THE FIX: Clamp the foot target so it can NEVER exceed the physical leg length
        if (distance > maxReach)
        {
            // Pull the target back to the absolute maximum reach of the leg
            targetFootPos = hipJoint.position + (hipToTarget.normalized * maxReach);
            distance = maxReach;
        }

        if (distance >= maxReach)
        {
            kneePos = hipJoint.position + hipToTarget.normalized * thighLength;
        }
        else
        {
            float angleRad = Mathf.Acos((thighLength * thighLength + distance * distance - shinLength * shinLength) / (2f * thighLength * distance));
            float angleDeg = angleRad * Mathf.Rad2Deg;

            Vector3 kneeAimDirection = transform.forward;
            Vector3 bendAxis = Vector3.Cross(hipToTarget.normalized, kneeAimDirection);

            if (bendAxis.sqrMagnitude < 0.001f) bendAxis = transform.right;
            else bendAxis = bendAxis.normalized;

            if (bendBackward) angleDeg = -angleDeg;

            Vector3 kneeDir = Quaternion.AngleAxis(angleDeg, bendAxis) * hipToTarget.normalized;
            kneePos = hipJoint.position + kneeDir * thighLength;
        }

        kneeJoint.position = kneePos;
        footJoint.position = targetFootPos;

        hipJoint.rotation = Quaternion.LookRotation(kneePos - hipJoint.position, transform.forward);
        kneeJoint.rotation = Quaternion.LookRotation(targetFootPos - kneePos, transform.forward);

        if (footMesh != null)
        {
            footMesh.position = targetFootPos;
            footMesh.rotation = Quaternion.FromToRotation(transform.up, targetNormal) * transform.rotation;
        }
    }
}