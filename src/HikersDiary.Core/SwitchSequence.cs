namespace HikersDiary.Core;

/// <summary>The steps of a save switch (#14) before the disconnection, in order.</summary>
public enum SwitchPhase
{
    /// <summary>No command.</summary>
    Idle,
    /// <summary>A command waits for the sleep. The sleep timeout is short while the player sits.</summary>
    Pending,
    /// <summary>The player sleeps. The view and the audio decrease to zero.</summary>
    FadeOut,
    /// <summary>The view and the audio are zero. The game layer disconnects and hosts the target.</summary>
    Disconnect,
}

/// <summary>
/// The local save switch (#14, T3c) as a state machine with no game types. The game layer gives it the time and
/// the sit and sleep states of each frame. Then it does the work of each new phase.
/// </summary>
// [impl->REQ-SAVE-SWITCH]
public sealed class SwitchSequence
{
    private readonly float _fadeSeconds;
    private float _at;
    private bool _sat;

    public SwitchSequence(float fadeSeconds) => _fadeSeconds = Math.Max(0.01f, fadeSeconds);

    public SwitchPhase Phase { get; private set; } = SwitchPhase.Idle;

    /// <summary>The command of the switch. A new command replaces the previous command.</summary>
    public ChatCommand? Command { get; private set; }

    /// <summary>True while the game must use the short sleep timeout: from the sit to the disconnection.</summary>
    public bool ShortTimeout { get; private set; }

    public void Arm(ChatCommand command)
    {
        Command = command;
        Phase = SwitchPhase.Pending;
        _sat = false;
    }

    /// <summary>Go to the next phase if its condition is true. Returns the phase after the step.</summary>
    public SwitchPhase Step(float now, bool sitting, bool sleeping)
    {
        switch (Phase)
        {
            case SwitchPhase.Pending:
                if (_sat && !sitting) { Cancel(); break; } // the player stood up
                _sat |= sitting;
                if (sitting && sleeping) { Phase = SwitchPhase.FadeOut; _at = now; }
                break;
            case SwitchPhase.FadeOut:
                if (!sleeping || !sitting) Cancel(); // the player woke
                else if (now - _at >= _fadeSeconds) Phase = SwitchPhase.Disconnect;
                break;
        }
        // The short timeout stays until the disconnection: the initial timeout ends the sleep of a player who sleeps.
        ShortTimeout = Phase is SwitchPhase.FadeOut or SwitchPhase.Disconnect || (Phase == SwitchPhase.Pending && sitting);
        return Phase;
    }

    /// <summary>The alpha of the black cover: 0, then up to 1 at the end of the fade out.</summary>
    public float Cover(float now) => Phase switch
    {
        SwitchPhase.FadeOut => Math.Clamp((now - _at) / _fadeSeconds, 0f, 1f),
        SwitchPhase.Disconnect => 1f,
        _ => 0f,
    };

    /// <summary>Stop the switch. The game layer calls it after the disconnection, or when the target is not valid.</summary>
    public void Cancel()
    {
        Phase = SwitchPhase.Idle;
        Command = null;
        ShortTimeout = false;
    }
}
