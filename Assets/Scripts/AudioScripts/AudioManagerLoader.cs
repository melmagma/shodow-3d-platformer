using UnityEngine;

public class AudioManagerLoader : MonoBehaviour
{
    public AudioManager theAM;

    private void Awake()
    {
        if (FindAnyObjectByType<AudioManager>() != null)
        {
            return;
        }

        //AudioManager.instance = Instantiate(theAM);
        DontDestroyOnLoad(AudioManager.instance.gameObject);
    }
}