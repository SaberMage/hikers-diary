namespace HikersDiary.Core;

/// <summary>The steps of the game start (#13), in order.</summary>
public enum StartPhase
{
    /// <summary>The first screen is not there yet.</summary>
    WaitMenu,
    /// <summary>The first screen shows. The host starts after <see cref="StartSequence.HostDelay"/>.</summary>
    WaitHost,
    /// <summary>The host was asked to start. The first screen must stay when the game scene loads.</summary>
    Hosting,
    /// <summary>The game scene loads behind the first screen.</summary>
    WaitPlayer,
    /// <summary>The local player is in the drowsy view. The game view is not ready yet.</summary>
    WaitView,
    /// <summary>The black background fades out, and the intro music decreases to zero.</summary>
    Fade,
    /// <summary>The continue button shows.</summary>
    Ready,
    /// <summary>The game start is complete, or it did not start (no save).</summary>
    Done,
}

/// <summary>
/// The game start (#13) as a state machine with no game types. The game layer gives it the time and the
/// facts of each frame, and does the work of each new phase.
/// </summary>
// [impl->REQ-GAME-START]
public sealed class StartSequence
{
    /// <summary>The time from the first screen to the host start. It is the same as the tested auto host.</summary>
    // ponytail: a fixed wait for the online login of the game. Wait for the login itself if 3 s is too short.
    public const float HostDelay = 3f;

    private readonly float _fadeSeconds;
    private float _at;

    public StartSequence(float fadeSeconds) => _fadeSeconds = Math.Max(0.01f, fadeSeconds);

    public StartPhase Phase { get; private set; } = StartPhase.WaitMenu;

    /// <summary>Go to the next phase if its condition is true. Returns the phase after the step.</summary>
    public StartPhase Step(float now, bool menuShown, bool hostStarted, bool playerReady, bool viewReady)
    {
        switch (Phase)
        {
            case StartPhase.WaitMenu when menuShown:
                Go(StartPhase.WaitHost, now);
                break;
            case StartPhase.WaitHost when now - _at >= HostDelay:
                Go(StartPhase.Hosting, now);
                break;
            case StartPhase.Hosting when hostStarted:
                Go(StartPhase.WaitPlayer, now);
                break;
            case StartPhase.WaitPlayer when playerReady:
                Go(StartPhase.WaitView, now);
                break;
            case StartPhase.WaitView when viewReady:
                Go(StartPhase.Fade, now);
                break;
            case StartPhase.Fade when now - _at >= _fadeSeconds:
                Go(StartPhase.Ready, now);
                break;
        }
        return Phase;
    }

    /// <summary>
    /// The alpha of the black background and the volume of the intro music: 1 before the fade, then
    /// down to 0 at the end of the fade.
    /// </summary>
    public float Cover(float now) => Phase switch
    {
        < StartPhase.Fade => 1f,
        StartPhase.Fade => 1f - Math.Clamp((now - _at) / _fadeSeconds, 0f, 1f),
        _ => 0f,
    };

    /// <summary>Stop the game start: there is no save to host, or the player pressed continue.</summary>
    public void End() => Phase = StartPhase.Done;

    private void Go(StartPhase phase, float now)
    {
        Phase = phase;
        _at = now;
    }
}
