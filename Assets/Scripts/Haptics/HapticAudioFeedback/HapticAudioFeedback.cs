using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
#if HAS_META_HAPTICS
using Oculus.Haptics;
#endif

/// <summary>
/// ★ Haptic + audio feedback in one component. Copy this folder into your own project. ★
///
/// Add it to any object, choose WHEN it plays (XRI events, collisions, while held, or from your
/// own code), WHAT to feel (a .haptic clip from Haptics Studio, or a simple pulse) and WHAT to hear
/// (one or more AudioClips). Sound and touch always start together.
///
/// Needs XR Interaction Toolkit 3. The Meta XR Haptics SDK is optional: with it installed, .haptic
/// clips play through HapticClipPlayer; without it (or where it can't run) the pulse settings are used.
///
/// From code:
///   feedback.Play();                       // default hand, full strength
///   feedback.Play(0.5f);                   // half strength
///   feedback.Play(HapticAudioFeedback.Hand.Left, 1f);
///   feedback.StartLoop(HapticAudioFeedback.Hand.Right);  feedback.StopLoop();
/// </summary>
[AddComponentMenu("Haptics/Haptic Audio Feedback")]
public class HapticAudioFeedback : MonoBehaviour
{
    public enum Hand { Left, Right, Both }
    public enum HandMode { FromInteractor, Left, Right, Both }

    /// <summary>Global strength (0..1) for every HapticAudioFeedback, e.g. for a settings slider.</summary>
    public static float GlobalHapticStrength = 1f;
    /// <summary>Global switch for haptics. Sound still plays when this is off.</summary>
    public static bool HapticsEnabled = true;

    // ------------------------------------------------------------------ inspector
#if HAS_META_HAPTICS
    [Header("What to feel")]
    [Tooltip(".haptic clip from Haptics Studio. Leave empty to use the simple pulse below.")]
    public HapticClip hapticClip;
    [Range(-1f, 1f)] [Tooltip("Shifts the clip's frequency: negative = softer, rumbly; positive = sharper, buzzy.")]
    public float frequencyShift;
    [Range(0, 255)] [Tooltip("0 = most important. Each controller plays only one clip at a time; the most important wins.")]
    public int priority = 128;
#else
    [Header("What to feel (install the Meta XR Haptics SDK for .haptic clips)")]
#endif
    [Range(0f, 1f)]
    [Tooltip("Overall haptic strength.")]
    public float hapticStrength = 1f;

    [Header("Simple pulse (used when there is no clip)")]
    [Range(0f, 1f)] public float pulseAmplitude = 0.5f;
    [Min(0f)][Tooltip("Seconds.")] public float pulseDuration = 0.08f;
    [Min(0f)][Tooltip("Hz. 0 = the controller's default.")] public float pulseFrequency;

    [Header("What to hear (optional)")]
    [Tooltip("One clip, or several: a random one is picked each time.")]
    public AudioClip[] sounds = new AudioClip[0];
    [Range(0f, 1f)] public float volume = 1f;
    [Range(0f, 0.5f)]
    [Tooltip("Random pitch change per play, so repeated sounds feel less mechanical.")]
    public float pitchVariation = 0.05f;
    [Tooltip("3D sound from this object. Off = plays at the same volume everywhere.")]
    public bool spatialSound = true;
    [Tooltip("Optional: your own AudioSource (e.g. with Meta XR Audio). Left empty, one is added.")]
    public AudioSource audioSource;

    [Header("When")]
    public bool onHoverEnter;
    public bool onHoverExit;
    public bool onSelectEnter = true;
    public bool onSelectExit;
    public bool onActivate;
    public bool onDeactivate;
    [Tooltip("Plays on collisions, scaled by impact speed.")]
    public bool onCollision;
    [Tooltip("Loops the feedback for as long as the object is held (engines, textures, motors).")]
    public bool whileHeld;

    [Header("Collisions")]
    [Tooltip("Only while the object is held (avoids feedback when it falls on the floor).")]
    public bool collisionOnlyWhileHeld = true;
    [Tooltip("Impact speeds (m/s) mapped to strength. Below x nothing plays.")]
    public Vector2 impactSpeedRange = new Vector2(0.25f, 2.5f);

    [Header("While held")]
    [Tooltip("Drive the loop's strength from how fast the object moves.")]
    public bool scaleWithSpeed;
    [Tooltip("Speeds (m/s) mapped to strength.")]
    public Vector2 speedRange = new Vector2(0.05f, 1.5f);
    [Range(0f, 1f)]
    [Tooltip("Strength when the object is still.")]
    public float minLoopStrength = 0.2f;

    [Header("Hand and timing")]
    [Tooltip("FromInteractor = the hand that hovers, grabs or collides.")]
    public HandMode hand = HandMode.FromInteractor;
    [Tooltip("Used when the hand can't be detected, e.g. for Play() from a UI button.")]
    public Hand defaultHand = Hand.Right;
    [Min(0f)]
    [Tooltip("Minimum seconds between two one-shot plays.")]
    public float cooldown = 0.05f;

    [Header("Events")]
    [Tooltip("Called every time the feedback plays (one-shot or loop start).")]
    public UnityEvent onPlayed = new UnityEvent();

    // ------------------------------------------------------------------ state
    XRBaseInteractable _interactable;
    IXRInteractor _holder;
    Hand _lastHand;
    float _nextPlay;
    bool _looping;
    Hand _loopHand;
    float _nextLoopPulse;
    Vector3 _lastPos;
    float _speed;
    AudioSource _loopAudio;

#if HAS_META_HAPTICS
    HapticClipPlayer _player;
    HapticClip _playerClip;
    float _metaRetryAt;
#endif

    /// <summary>The hand that played most recently.</summary>
    public Hand LastHand => _lastHand;
    /// <summary>True while a loop is running.</summary>
    public bool IsLooping => _looping;

    // ------------------------------------------------------------------ Unity
    void Awake()
    {
        _interactable = GetComponent<XRBaseInteractable>();
        _lastHand = defaultHand;
    }

    void OnEnable()
    {
        _lastPos = transform.position;
        if (_interactable == null) return;
        _interactable.hoverEntered.AddListener(OnHoverEntered);
        _interactable.hoverExited.AddListener(OnHoverExited);
        _interactable.selectEntered.AddListener(OnSelectEntered);
        _interactable.selectExited.AddListener(OnSelectExited);
        _interactable.activated.AddListener(OnActivated);
        _interactable.deactivated.AddListener(OnDeactivated);
    }

    void OnDisable()
    {
        StopLoop();
        _holder = null;
        if (_interactable == null) return;
        _interactable.hoverEntered.RemoveListener(OnHoverEntered);
        _interactable.hoverExited.RemoveListener(OnHoverExited);
        _interactable.selectEntered.RemoveListener(OnSelectEntered);
        _interactable.selectExited.RemoveListener(OnSelectExited);
        _interactable.activated.RemoveListener(OnActivated);
        _interactable.deactivated.RemoveListener(OnDeactivated);
    }

    void OnDestroy()
    {
#if HAS_META_HAPTICS
        try { _player?.Dispose(); }
        catch (Exception) { /* SDK already shut down */ }
        _player = null;
#endif
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            float raw = (transform.position - _lastPos).magnitude / dt;
            _speed = Mathf.Lerp(_speed, raw, 1f - Mathf.Exp(-dt * 15f)); // smoothed
        }
        _lastPos = transform.position;

        if (_looping) UpdateLoop();
    }

    // ------------------------------------------------------------------ XRI events
    void OnHoverEntered(HoverEnterEventArgs a) { if (onHoverEnter) Play(HandOf(a.interactorObject)); }
    void OnHoverExited(HoverExitEventArgs a) { if (onHoverExit) Play(HandOf(a.interactorObject)); }
    void OnActivated(ActivateEventArgs a) { if (onActivate) Play(HandOf(a.interactorObject)); }
    void OnDeactivated(DeactivateEventArgs a) { if (onDeactivate) Play(HandOf(a.interactorObject)); }

    void OnSelectEntered(SelectEnterEventArgs a)
    {
        _holder = a.interactorObject;
        var h = HandOf(_holder);
        if (onSelectEnter) Play(h);
        if (whileHeld) StartLoop(h);
    }

    void OnSelectExited(SelectExitEventArgs a)
    {
        if (whileHeld) StopLoop();
        if (onSelectExit) Play(HandOf(a.interactorObject));
        _holder = null;
    }

    void OnCollisionEnter(Collision c)
    {
        if (!onCollision) return;
        bool held = _holder != null;
        if (collisionOnlyWhileHeld && !held) return;
        float speed = c.relativeVelocity.magnitude;
        if (speed < impactSpeedRange.x) return;
        float strength = Mathf.Lerp(0.2f, 1f, Mathf.InverseLerp(impactSpeedRange.x, impactSpeedRange.y, speed));
        Play(held ? HandOf(_holder) : ResolveHand(null), strength);
    }

    // ------------------------------------------------------------------ public API
    /// <summary>Play once on the configured hand (handy for UnityEvents and UI buttons).</summary>
    public void Play() => Play(ResolveHand(_holder), 1f);

    /// <summary>Play once at a strength (0..1) on the configured hand.</summary>
    public void Play(float strength) => Play(ResolveHand(_holder), strength);

    public void PlayLeft() => Play(Hand.Left, 1f);
    public void PlayRight() => Play(Hand.Right, 1f);
    public void PlayBoth() => Play(Hand.Both, 1f);

    /// <summary>Play once on a hand. strength (0..1) scales both haptics and volume.</summary>
    public void Play(Hand h, float strength = 1f)
    {
        if (Time.time < _nextPlay) return;
        _nextPlay = Time.time + cooldown;
        strength = Mathf.Clamp01(strength);
        _lastHand = h;

        if (!PlayClip(h, strength, false))
            Pulse(h, pulseAmplitude * strength, pulseDuration);

        var s = PickSound();
        if (s != null)
        {
            var src = OneShotSource();
            src.pitch = 1f + UnityEngine.Random.Range(-pitchVariation, pitchVariation);
            src.PlayOneShot(s, volume * strength);
        }
        onPlayed.Invoke();
    }

    /// <summary>Start looping feedback (clip looped, or repeated pulses) and a looping sound.</summary>
    public void StartLoop(Hand h)
    {
        if (_looping) StopLoop();
        _looping = true;
        _loopHand = h;
        _lastHand = h;
        _nextLoopPulse = 0f;
        float strength = LoopStrength();

        PlayClip(h, strength, true);

        var s = PickSound();
        if (s != null)
        {
            if (_loopAudio == null)
            {
                _loopAudio = gameObject.AddComponent<AudioSource>();
                SetUp(_loopAudio);
            }
            _loopAudio.clip = s;
            _loopAudio.loop = true;
            _loopAudio.volume = volume * strength;
            _loopAudio.pitch = 1f;
            _loopAudio.Play();
        }
        onPlayed.Invoke();
    }

    /// <summary>Stop a running loop.</summary>
    public void StopLoop()
    {
        if (!_looping) return;
        _looping = false;
        StopClip();
        if (_loopAudio != null) _loopAudio.Stop();
    }

    /// <summary>Stop everything this component is playing.</summary>
    public void Stop()
    {
        StopLoop();
        StopClip();
        if (audioSource != null) audioSource.Stop();
    }

    // ------------------------------------------------------------------ loop
    float LoopStrength()
    {
        if (!scaleWithSpeed) return 1f;
        return Mathf.Lerp(minLoopStrength, 1f, Mathf.InverseLerp(speedRange.x, speedRange.y, _speed));
    }

    void UpdateLoop()
    {
        float strength = LoopStrength();
        if (_loopAudio != null && _loopAudio.isPlaying) _loopAudio.volume = volume * strength;

        if (SetClipStrength(strength)) return;

        // No clip: keep the controller buzzing with short overlapping pulses.
        if (Time.time >= _nextLoopPulse)
        {
            const float step = 0.1f;
            Pulse(_loopHand, pulseAmplitude * strength, step + 0.03f);
            _nextLoopPulse = Time.time + step;
        }
    }

    // ------------------------------------------------------------------ haptics
    float HapticScale => HapticsEnabled ? Mathf.Clamp01(hapticStrength * GlobalHapticStrength) : 0f;

    /// <summary>Plays the .haptic clip. Returns false when there is no clip or the SDK can't play it.</summary>
    bool PlayClip(Hand h, float strength, bool loop)
    {
#if HAS_META_HAPTICS
        if (hapticClip == null || !EnsurePlayer()) return false;
        try
        {
            _player.isLooping = loop;
            _player.amplitude = strength * HapticScale;
            _player.frequencyShift = frequencyShift;
            _player.priority = (uint)priority;
            if (HapticScale > 0f) _player.Play(ToMeta(h));
            return true;
        }
        catch (Exception e) { MetaFailed(e); }
#endif
        return false;
    }

    bool SetClipStrength(float strength)
    {
#if HAS_META_HAPTICS
        if (hapticClip != null && _player != null)
        {
            try { _player.amplitude = strength * HapticScale; return true; }
            catch (Exception e) { MetaFailed(e); }
        }
#endif
        return false;
    }

    void StopClip()
    {
#if HAS_META_HAPTICS
        if (_player == null) return;
        try { _player.Stop(); }
        catch (Exception e) { MetaFailed(e); }
#endif
    }

#if HAS_META_HAPTICS
    bool EnsurePlayer()
    {
        if (Time.realtimeSinceStartup < _metaRetryAt) return false;
        if (!MetaSupportedPlatform) return false;
        try
        {
            if (_player == null) { _player = new HapticClipPlayer(hapticClip); _playerClip = hapticClip; }
            else if (_playerClip != hapticClip) { _player.clip = hapticClip; _playerClip = hapticClip; }
            return true;
        }
        catch (Exception e) { MetaFailed(e); return false; }
    }

    void MetaFailed(Exception e)
    {
        // Often temporary (headset not connected yet): use pulses for a few seconds, then retry.
        if (_metaRetryAt <= 0f)
            Debug.LogWarning($"[HapticAudioFeedback] Meta Haptics SDK not ready, using simple pulses for now. ({e.Message})", this);
        _metaRetryAt = Time.realtimeSinceStartup + 5f;
        _player = null;
    }

    static bool MetaSupportedPlatform
    {
        get
        {
#if (UNITY_ANDROID && !UNITY_EDITOR) || UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            return true;
#else
            return false;
#endif
        }
    }

    static Controller ToMeta(Hand h) => h == Hand.Left ? Controller.Left : h == Hand.Right ? Controller.Right : Controller.Both;
#endif

    void Pulse(Hand h, float amplitude, float duration)
    {
        amplitude = Mathf.Clamp01(amplitude * HapticScale);
        if (amplitude <= 0f || duration <= 0f) return;
        if (h != Hand.Right) SendImpulse(XRNode.LeftHand, amplitude, duration);
        if (h != Hand.Left) SendImpulse(XRNode.RightHand, amplitude, duration);
    }

    void SendImpulse(XRNode node, float amplitude, float duration)
    {
        // Prefer the XRI impulse player on the interacting controller; fall back to Unity's XR input API.
        var player = FindImpulsePlayer(node);
        if (player != null && player.isActiveAndEnabled)
        {
            if (pulseFrequency > 0f) player.SendHapticImpulse(amplitude, duration, pulseFrequency);
            else player.SendHapticImpulse(amplitude, duration);
            return;
        }
        var device = InputDevices.GetDeviceAtXRNode(node);
        if (device.isValid) device.SendHapticImpulse(0, amplitude, duration);
    }

    static HapticImpulsePlayer s_left, s_right;

    HapticImpulsePlayer FindImpulsePlayer(XRNode node)
    {
        bool left = node == XRNode.LeftHand;
        if (_holder is Component c && HandOf(_holder) == (left ? Hand.Left : Hand.Right))
        {
            var p = c.GetComponentInParent<HapticImpulsePlayer>();
            if (p != null) return p;
        }
        // Nobody holding (e.g. Play() from code): find the rig's players once, by "Left"/"Right" in the hierarchy.
        if (s_left == null || s_right == null)
        {
            foreach (var p in FindObjectsByType<HapticImpulsePlayer>())
                for (var t = p.transform; t != null; t = t.parent)
                {
                    var n = t.name.ToLowerInvariant();
                    if (n.Contains("left")) { if (s_left == null) s_left = p; break; }
                    if (n.Contains("right")) { if (s_right == null) s_right = p; break; }
                }
        }
        return left ? s_left : s_right;
    }

    // ------------------------------------------------------------------ audio
    AudioClip PickSound()
    {
        if (sounds == null || sounds.Length == 0) return null;
        return sounds[sounds.Length == 1 ? 0 : UnityEngine.Random.Range(0, sounds.Length)];
    }

    AudioSource OneShotSource()
    {
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            SetUp(audioSource);
        }
        return audioSource;
    }

    void SetUp(AudioSource src)
    {
        src.playOnAwake = false;
        src.spatialBlend = spatialSound ? 1f : 0f;
        src.minDistance = 0.3f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
    }

    // ------------------------------------------------------------------ hands
    Hand HandOf(IXRInteractor interactor)
    {
        if (hand != HandMode.FromInteractor) return ResolveHand(interactor);
        if (interactor == null) return defaultHand;
        switch (interactor.handedness)
        {
            case InteractorHandedness.Left: return Hand.Left;
            case InteractorHandedness.Right: return Hand.Right;
        }
        // Custom rigs without handedness: look for "left"/"right" in the hierarchy.
        for (var t = interactor.transform; t != null; t = t.parent)
        {
            var n = t.name.ToLowerInvariant();
            if (n.Contains("left")) return Hand.Left;
            if (n.Contains("right")) return Hand.Right;
        }
        return defaultHand;
    }

    Hand ResolveHand(IXRInteractor interactor)
    {
        switch (hand)
        {
            case HandMode.Left: return Hand.Left;
            case HandMode.Right: return Hand.Right;
            case HandMode.Both: return Hand.Both;
            default: return interactor != null ? HandOf(interactor) : defaultHand;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Keeps the globals correct when "Enter Play Mode Options" disables domain reload.
        GlobalHapticStrength = 1f;
        HapticsEnabled = true;
        s_left = s_right = null;
    }
}
