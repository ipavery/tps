using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Core;

public class InteractionTest : MonoBehaviour, IInteractable
{
    public InteractionTriggerMode TriggerMode => InteractionTriggerMode.OnButtonPress;
    public int Priority => 10;
    public string InteractionPromptText => "I am a cube!";
    public int count = 0;

    public bool CanInteract(GameObject interactor)
    {
        // Only allow interaction if the vehicle is empty
        return true;
    }

    public void Interact(GameObject interactor)
    {
        // The client player presses the interact button, sending a request to the server
        NetworkObject playerNetObj = interactor.GetComponent<NetworkObject>();
        if (playerNetObj != null)
        {
            count++;
            Debug.Log($"Player {playerNetObj.OwnerClientId} interacted with the cube! Interacted {count} times.");
            //InteractionPromptText = $"I am a cube! Interacted {count} times."; //can't set this?
        }
    }
}
