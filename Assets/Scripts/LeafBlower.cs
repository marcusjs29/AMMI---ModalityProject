using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Leaf blower: while firing, plays an "air" effect + looping sound and shrinks
/// any water particles inside a cone in front of the nozzle. Particles that
/// shrink below a threshold are killed.
///
/// Setup:
///  1. Put this on the leaf blower object. Create an empty child at the nozzle
///     tip, with its blue (Z) axis pointing out of the nozzle, and assign it to "Nozzle".
///  2. Assign the water ParticleSystem(s) to "Water Systems".
///  3. (Optional) Assign an "Air Fx" ParticleSystem (child of the nozzle) and an AudioSource.
///  4. Hook up input: with XR Grab Interactable, use the "Activated" event -> StartBlowing()
///     and the "Deactivated" event -> StopBlowing(). That gives you hold-to-spray.
/// </summary>
public class LeafBlower : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Mouth of the blower. Its forward (+Z) is the blow direction.")]
    public Transform nozzle;
    [Tooltip("Your comrade's water particle system(s).")]
    public ParticleSystem[] waterSystems;
    [Tooltip("Optional visual 'air' particle system (set it to loop, not play on awake).")]
    public ParticleSystem airFx;
    [Tooltip("Optional looping hair-dryer sound.")]
    public AudioSource blowerAudio;

    [Header("Cone")]
    public float range = 3f;
    [Range(1f, 90f)] public float coneHalfAngle = 20f;

    [Header("Shrinking")]
    [Tooltip("Size units lost per second (at point-blank range).")]
    public float shrinkPerSecond = 0.05f;
    [Tooltip("Particle is killed once its size drops to this.")]
    public float killThreshold = 0.005f;
    [Tooltip("Shrink is weaker at the far end of the cone. 0 = uniform, 1 = fades to nothing at max range.")]
    [Range(0f, 1f)] public float distanceFalloff = 0.5f;

    [Header("Audio")]
    public float audioFadeSpeed = 6f;
    public float maxVolume = 1f;

    public bool IsBlowing { get; private set; }

    ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[1024];

    // Hook these to your input / XR events -----------------------------------
    public void StartBlowing()
    {
        IsBlowing = true;
        if (airFx != null && !airFx.isPlaying) airFx.Play();
        if (blowerAudio != null && !blowerAudio.isPlaying)
        {
            blowerAudio.loop = true;
            blowerAudio.volume = 0f;
            blowerAudio.Play();
        }
    }

    public void StopBlowing()
    {
        IsBlowing = false;
        if (airFx != null) airFx.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
    // ------------------------------------------------------------------------

    void Update()
    {
        UpdateAudio();
        if (IsBlowing) ShrinkWaterInCone(Time.deltaTime);
    }

    void UpdateAudio()
    {
        if (blowerAudio == null) return;
        float target = IsBlowing ? maxVolume : 0f;
        blowerAudio.volume = Mathf.MoveTowards(blowerAudio.volume, target, audioFadeSpeed * Time.deltaTime);
        if (!IsBlowing && blowerAudio.isPlaying && blowerAudio.volume <= 0f) blowerAudio.Stop();
    }

    void ShrinkWaterInCone(float dt)
    {
        Transform n = nozzle != null ? nozzle : transform;
        Vector3 origin = n.position;
        Vector3 dir = n.forward;
        float cosHalf = Mathf.Cos(coneHalfAngle * Mathf.Deg2Rad);
        float rangeSqr = range * range;

        foreach (var ps in waterSystems)
        {
            if (ps == null) continue;

            int max = ps.main.maxParticles;
            if (buffer.Length < max) buffer = new ParticleSystem.Particle[max];

            int count = ps.GetParticles(buffer);
            if (count == 0) continue;

            var main = ps.main;
            bool local = main.simulationSpace != ParticleSystemSimulationSpace.World;
            Transform simSpace = main.simulationSpace == ParticleSystemSimulationSpace.Custom
                ? main.customSimulationSpace
                : ps.transform;

            bool changed = false;

            for (int i = 0; i < count; i++)
            {
                Vector3 pos = buffer[i].position;
                if (local && simSpace != null) pos = simSpace.TransformPoint(pos);

                Vector3 to = pos - origin;
                float sqr = to.sqrMagnitude;
                if (sqr > rangeSqr) continue;

                float dist = Mathf.Sqrt(sqr);
                // Particle right at the nozzle counts as in the cone.
                if (dist > 0.0001f && Vector3.Dot(to / dist, dir) < cosHalf) continue;

                float strength = 1f - distanceFalloff * (dist / range);
                float size = buffer[i].startSize - shrinkPerSecond * strength * dt;

                if (size <= killThreshold)
                {
                    buffer[i].remainingLifetime = 0f; // dies this frame
                }
                else
                {
                    buffer[i].startSize = size;
                }
                changed = true;
            }

            if (changed) ps.SetParticles(buffer, count);
        }
    }

    void OnDrawGizmosSelected()
    {
        Transform n = nozzle != null ? nozzle : transform;
        Gizmos.color = Color.cyan;
        float r = Mathf.Tan(coneHalfAngle * Mathf.Deg2Rad) * range;
        Vector3 end = n.position + n.forward * range;
        Gizmos.DrawLine(n.position, end);
        foreach (Vector3 d in new[] { n.up, -n.up, n.right, -n.right })
            Gizmos.DrawLine(n.position, end + d * r);
    }
}
