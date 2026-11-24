using FMOD.Studio;
using FMODUnity;
using UnityEngine;

/// <summary>FMOD footsteps while moving on the ground. Optional: remove this component for silent feet.</summary>
public class PlayerFootsteps : MonoBehaviour
{
    private Rigidbody rb;
    private EventInstance footsteps;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        footsteps = AudioManager.instance.CreateEventInstance(FMODEvents.instance.playerFootsteps);
    }

    /// <summary>One physics step: plays or stops the loop depending on whether you're walking.</summary>
    public void Step(bool grounded)
    {
        Vector3 v = rb.linearVelocity;
        bool isMoving = new Vector2(v.x, v.z).sqrMagnitude > 0.01f;

        if (isMoving && grounded)
        {
            footsteps.set3DAttributes(RuntimeUtils.To3DAttributes(gameObject, rb));
            footsteps.getPlaybackState(out PLAYBACK_STATE state);
            if (state == PLAYBACK_STATE.STOPPED) footsteps.start();
        }
        else
        {
            Stop();
        }
    }

    public void Stop() => footsteps.stop(STOP_MODE.ALLOWFADEOUT);
}
