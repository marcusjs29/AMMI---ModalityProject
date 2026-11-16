using UnityEngine;

public class PlayerHitDetector : MonoBehaviour
{
    void OnParticleCollision(GameObject other)
    {
        if (other.name.Contains("Water") || other.CompareTag("WaterParticle"))
        {
            FindObjectOfType<GameManager>().TriggerLoseState();
        }
    }
}