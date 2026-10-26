using UnityEngine;

public class WaterEvaporate : MonoBehaviour
{
    private ParticleSystem ps;
    private ParticleSystem.Particle[] particles;

    void Start()
    {
        ps = GetComponent<ParticleSystem>();
    }

    void LateUpdate()
    {
        int numParticles = ps.particleCount;
        if (particles == null || particles.Length < numParticles)
            particles = new ParticleSystem.Particle[numParticles];

        int num = ps.GetParticles(particles);

        // Example: If particles get too close to a point, shrink their size to 0
        for (int i = 0; i < num; i++)
        {
            // If touched by leafblower (simplified by checking distance to a target)
            // particles[i].size *= 0.95f; 
        }
        ps.SetParticles(particles, num);
    }
}