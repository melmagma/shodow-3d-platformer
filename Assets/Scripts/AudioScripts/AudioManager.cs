using UnityEngine;
using FMODUnity;
using FMOD.Studio;
using System.Collections.Generic;

public class AudioManager : MonoBehaviour
{
    private List<EventInstance> eventInstances;

    [field : SerializeField] public static AudioManager instance { get; private set; }

    private void Awake()
    {
        if (instance == null)
        {

            instance = this;

            DontDestroyOnLoad(gameObject); //ensures that after scene load/etc, the audiomanager doesnt get destroyed
        }
        else if (instance != this)
        {
            Destroy(gameObject); //prevent copies of this game object
        }
        eventInstances = new List<EventInstance>();
    }

    public void PlayOneShot(EventReference sound, Vector3 worldpos)
    {
        RuntimeManager.PlayOneShot(sound, worldpos);
    }

    public EventInstance CreateEventInstance(EventReference eventReference)
    {
        EventInstance eventInstance = RuntimeManager.CreateInstance(eventReference);
        eventInstances.Add(eventInstance);
        return eventInstance;
    }

    private void CleanUp()
    {
        foreach (EventInstance eventInstance in eventInstances)
        {
            eventInstance.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            eventInstance.release();
        }
    }

    private void OnDestroy()
    {
        CleanUp();
    }
}