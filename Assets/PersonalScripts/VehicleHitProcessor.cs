using UnityEngine;
using Unity.Netcode;
using Blocks.Gameplay.Core;
[RequireComponent(typeof(CoreStatsHandler))]

[RequireComponent(typeof(Rigidbody))]
public class VehicleHitProcessor : HitProcessor
{
    private CoreStatsHandler m_StatsHandler;
    private Rigidbody m_Rigidbody;

    private void Awake()
    {
        m_StatsHandler = GetComponent<CoreStatsHandler>();
        m_Rigidbody = GetComponent<Rigidbody>();
    }

    protected override void HandleHit(HitInfo info)
    {
        Debug.Log($"Vehicle {gameObject.name} was hit for {info.amount} damage");
        // 1. Apply physical knockback if the weapon causes it
        if (m_Rigidbody != null && info.impactForce != Vector3.zero)
        {
            m_Rigidbody.AddForceAtPosition(info.impactForce, info.hitPoint, ForceMode.Impulse);
        }

        // 2. Apply damage to the vehicle's health pool
        m_StatsHandler.ModifyStat(StatKeys.Health, -info.amount, info.attackerId, ModificationSource.Damage);

        // 3. Check for vehicle destruction
        if (!m_StatsHandler.IsAlive)
        {
            HandleDestruction();
        }
    }

    private void HandleDestruction()
    {
        // TODO: Spawn explosion VFX, play sound, eject players, etc.
        Debug.Log($"{gameObject.name} was destroyed!");
        
        // Despawn the vehicle from the network
        if (NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
    }
}