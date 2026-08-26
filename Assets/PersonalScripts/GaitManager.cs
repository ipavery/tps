using UnityEngine;
using System.Collections.Generic;

public class GaitManager : MonoBehaviour
{
    public enum GaitType { AlternatingGroups, Ripple }

    [Header("Gait Settings")]
    public GaitType currentGait = GaitType.AlternatingGroups;
    
    [Tooltip("For Ripple, lower values mean the next row starts sooner (0.0 to 1.0).")]
    public float rippleOverlapThreshold = 0.5f;

    [System.Serializable]
    public struct LegRow
    {
        public string rowName;
        public LegIK[] legs;
    }

    [Header("Leg Configuration")]
    [Tooltip("Define your legs from front to back. A row usually contains a Left and Right leg.")]
    public LegRow[] legRows;

    // State Tracking
    private bool isWaitingForStepToStart = true;
    private int activeGroupIndex = 0; // 0 for Evens (Group A), 1 for Odds (Group B)
    private int activeRippleRow = 0;

    void Update()
    {
        if (legRows == null || legRows.Length == 0) return;

        if (currentGait == GaitType.AlternatingGroups)
        {
            ProcessAlternatingGait();
        }
        else if (currentGait == GaitType.Ripple)
        {
            ProcessRippleGait();
        }
    }

    // --- GAIT LOGIC: ALTERNATING GROUPS ---
    private void ProcessAlternatingGait()
    {
        // 1. Give permission to the current active group
        SetGroupPermission(activeGroupIndex, true);
        SetGroupPermission(1 - activeGroupIndex, false); // Lock the other group

        bool anyLegStepping = IsAnyLegInGroupStepping(activeGroupIndex);

        // 2. Wait for at least one leg in the group to actually start moving
        if (isWaitingForStepToStart)
        {
            if (anyLegStepping)
            {
                isWaitingForStepToStart = false; // The step has begun!
            }
        }
        // 3. Wait for all legs in the group to finish
        else
        {
            if (!anyLegStepping)
            {
                // The group has completely finished stepping. Swap groups!
                activeGroupIndex = 1 - activeGroupIndex;
                isWaitingForStepToStart = true;
            }
        }
    }

    // --- GAIT LOGIC: CENTIPEDE RIPPLE ---
    private void ProcessRippleGait()
    {
        // 1. Lock all rows initially, then unlock the active one
        SetAllRowsPermission(false);
        SetRowPermission(activeRippleRow, true);

        // 2. Check the progress of the active row
        bool rowStarted = false;
        float maxProgress = 0f;

        foreach (var leg in legRows[activeRippleRow].legs)
        {
            if (leg.isStepping)
            {
                rowStarted = true;
                // Assuming you expose stepProgress in BirdLegIK as a public getter
                // If not, you can just check if (!leg.isStepping) to wait for it to finish entirely
                maxProgress = Mathf.Max(maxProgress, leg.stepProgress); 
            }
        }

        // 3. Move to the next row once this row has stepped (or reached a threshold)
        if (isWaitingForStepToStart)
        {
            if (rowStarted) isWaitingForStepToStart = false;
        }
        else
        {
            // If the row has finished stepping, cascade to the next row
            if (!rowStarted) 
            {
                activeRippleRow++;
                if (activeRippleRow >= legRows.Length) activeRippleRow = 0; // Loop back to front
                isWaitingForStepToStart = true;
            }
        }
    }

    // --- HELPER FUNCTIONS ---
    
    // --- HELPER FUNCTIONS ---
    
    private void SetGroupPermission(int groupMod, bool canStep)
    {
        for (int rowIndex = 0; rowIndex < legRows.Length; rowIndex++)
        {
            for (int legIndex = 0; legIndex < legRows[rowIndex].legs.Length; legIndex++)
            {
                // Checkerboard Math: Alternates diagonally across rows and columns
                if ((rowIndex + legIndex) % 2 == groupMod)
                {
                    if (legRows[rowIndex].legs[legIndex] != null)
                    {
                        legRows[rowIndex].legs[legIndex].canStep = canStep;
                    }
                }
            }
        }
    }

    private bool IsAnyLegInGroupStepping(int groupMod)
    {
        for (int rowIndex = 0; rowIndex < legRows.Length; rowIndex++)
        {
            for (int legIndex = 0; legIndex < legRows[rowIndex].legs.Length; legIndex++)
            {
                if ((rowIndex + legIndex) % 2 == groupMod)
                {
                    var leg = legRows[rowIndex].legs[legIndex];
                    if (leg != null && leg.isStepping) 
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private void SetRowPermission(int rowIndex, bool canStep)
    {
        foreach (var leg in legRows[rowIndex].legs)
        {
            if (leg != null) leg.canStep = canStep;
        }
    }

    private void SetAllRowsPermission(bool canStep)
    {
        for (int i = 0; i < legRows.Length; i++)
        {
            SetRowPermission(i, canStep);
        }
    }
}