using UnityEngine;
using TMPro;
using Blocks.Gameplay.Core; // Ensure this matches your namespace

public class InteractionPromptUI : MonoBehaviour
{
    [Tooltip("Drag the local player's InteractionAddon here, or find it dynamically.")]
    public InteractionAddon interactionAddon;
    
    [Tooltip("The UI Panel that holds the text (so we can hide the whole box).")]
    public GameObject promptPanel;
    
    [Tooltip("The TextMeshPro text element.")]
    public TextMeshProUGUI promptText;

    private void Update()
    {
        // If we don't have an addon, try to find the local player's addon
        if (interactionAddon == null)
        {
            FindLocalPlayerAddon();
            if (interactionAddon == null) return;
        }

        // If the player is looking at an interactable object
        if (interactionAddon.CurrentFocusedInteractable != null)
        {
            promptPanel.SetActive(true);
            promptText.text = "[E] " + interactionAddon.CurrentFocusedInteractable.InteractionPromptText;
        }
        else
        {
            // Not looking at anything, hide the prompt
            promptPanel.SetActive(false);
        }
    }

    private void FindLocalPlayerAddon()
    {
        // Simple way to find the local player's addon if it spawns dynamically
        var localPlayer = Unity.Netcode.NetworkManager.Singleton?.LocalClient?.PlayerObject;
        if (localPlayer != null)
        {
            interactionAddon = localPlayer.GetComponent<InteractionAddon>();
        }
    }
}