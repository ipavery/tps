using UnityEngine;

public class JitterSled : MonoBehaviour
{
    private Rigidbody rb;
    public float testSpeed = 100f;
    public float maxDistance = 2000f;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        // Force the sled to move perfectly forward at a constant speed
        rb.linearVelocity = new Vector3(0, 0, testSpeed);

        // Check if the sled has passed the distance threshold
        if (transform.position.z >= maxDistance || transform.position.magnitude >= maxDistance)
        {
            // Teleport back to origin by setting the rigidbody position directly
            rb.position = new Vector3(0, 2f, 0);
        }
    }
}