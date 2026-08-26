using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Core;
using Blocks.Gameplay.Shooter;

[RequireComponent(typeof(BaseVehicle))]
public class VehicleWeaponController : NetworkBehaviour
{
    [Header("Weapon Setup")]
    [Tooltip("The weapon component attached to the vehicle.")]
    public ModularWeapon mountedWeapon;
    
    [Tooltip("Where the weapon is physically mounted on the vehicle.")]
    public Transform weaponMount;

    private BaseVehicle m_Vehicle;
    private GameplayInputSystem_Actions m_InputActions;
    private bool m_IsFiring;

    private void Awake()
    {
        m_Vehicle = GetComponent<BaseVehicle>();
        m_InputActions = new GameplayInputSystem_Actions();

        // Listen for the fire button being pressed
        m_InputActions.Player.PrimaryAction.started += ctx => 
        {
            // STRICT GATE: Only allow firing if THIS local player is actively driving THIS vehicle
            if (m_Vehicle.IsLocalPlayerDriver) 
            {
                m_IsFiring = true;
            }
        };

        // Listen for the fire button being released
        m_InputActions.Player.PrimaryAction.canceled += ctx => 
        {
            m_IsFiring = false;
            mountedWeapon?.StopFiring();
        };
    }

    private void OnEnable()
    {
        m_InputActions.Player.Enable();
    }

    private void OnDisable()
    {
        m_InputActions.Player.Disable();
        m_IsFiring = false;
        mountedWeapon?.StopFiring();
    }

    private void Update()
    {
        // Failsafe: If the player dismounts while holding the fire button, force the weapon to stop
        if (!m_Vehicle.IsLocalPlayerDriver && m_IsFiring)
        {
            m_IsFiring = false;
            mountedWeapon?.StopFiring();
            return;
        }

        // Handle active firing
        if (m_IsFiring && mountedWeapon != null)
        {
            Vector3 fireOrigin = mountedWeapon.Muzzle != null ? mountedWeapon.Muzzle.position : weaponMount.position;
            
            // Aim straight down the nose of the vehicle
            Vector3 fireDirection = transform.forward; 

            // Fire the weapon, passing the vehicle as the owner entity
            mountedWeapon.Fire(gameObject, fireOrigin, fireDirection);
        }
    }
}