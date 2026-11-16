using UnityEngine;
using UnityEngine.SceneManagement;

public class GameManager : MonoBehaviour
{
    [Header("Settings")]
    public float survivalTimer = 10f; // Seconds to survive after clearing the grate

    [Header("References")]
    public ParticleSystem waterParticles; // Drag your Water Spray here

    private bool grateIsCleared = false;
    private bool isGameOver = false;
    private float timer = 0f;

    void Update()
    {
        if (isGameOver) return;

        // If the grate is cleared, start the win countdown
        if (grateIsCleared)
        {
            timer += Time.deltaTime;

            // Stop spawning new water so it looks like it's draining
            if (waterParticles != null && waterParticles.isPlaying)
            {
                var emission = waterParticles.emission;
                emission.enabled = false;
            }

            // If player survives the timer, YOU WIN!
            if (timer >= survivalTimer)
            {
                Debug.Log("You Survived! You Win!");
                TriggerWinState();
            }
        }
    }

    // --- LOSE CONDITION (Called when water particles hit the player) ---
    public void TriggerLoseState()
    {
        if (isGameOver) return;
        isGameOver = true;

        Debug.Log("Game Over! Water touched the player.");

        // For your 3-minute video, instantly reload or show a lose screen
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // --- WIN CONDITION (Called by your teammate's grate script) ---
    public void ClearGrate()
    {
        if (isGameOver) return;
        grateIsCleared = true;
        Debug.Log("Grate cleared! Draining water...");
    }

    void TriggerWinState()
    {
        isGameOver = true;

        // For your 3-minute video, reload or show a win screen
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}