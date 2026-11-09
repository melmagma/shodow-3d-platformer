using UnityEngine;
using FMODUnity;

public class FMODEvents : MonoBehaviour
{
    public static FMODEvents instance {  get; private set; }

    [field: Header("Player SFX")]
    [field: SerializeField] public EventReference playerFootsteps { get; private set; } // using field: allows us to see the private syntax

    [field: Header("Ambience")]
    [field: SerializeField] public EventReference mezanineAmbience { get; private set; } 

    private void Awake()
    {
        if (instance != null)
        {
            
        }
        instance = this;
    }
}
