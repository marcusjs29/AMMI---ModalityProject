using UnityEngine;

public class WaterHazardWarning : MonoBehaviour
{
    [Header("References")]
    public Transform playerControllerTransform; // Drag the player's active controller here
    public HapticAudioFeedback waterFeedback;   // Reference to a HapticAudioFeedback component set up with a warning sound/clip

    [Header("Settings")]
    public float warningDistance = 1.5f;
    private float pulseTimer = 0f;

    void Update()
    {
        if (playerControllerTransform == null || waterFeedback == null) return;

        float distance = Vector3.Distance(transform.position, playerControllerTransform.position);

        if (distance <= warningDistance)
        {
            float strength = Mathf.InverseLerp(warningDistance, 0f, distance);

            pulseTimer += Time.deltaTime;
            float interval = Mathf.Lerp(0.4f, 0.08f, strength); // Pulses faster as you get closer

            if (pulseTimer >= interval)
            {
                pulseTimer = 0f;
                // Triggers a one-shot haptic/audio warning scaled by proximity strength
                waterFeedback.Play(strength);
            }
        }
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.name.ToLower().Contains("controller") || other.CompareTag("Player"))
        {
            Debug.Log("Game Over: Touched the bad water!");
            // TODO: Trigger your level reset / loss state here
        }
    }
}