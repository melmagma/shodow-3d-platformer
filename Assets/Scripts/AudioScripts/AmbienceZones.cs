using UnityEngine;


public class AmbienceZones : MonoBehaviour
{
    [Header("Parameter Change")]
    [SerializeField] private string parameterName;
    [SerializeField] private float parameterValue;

    private void OnTriggerEnter(Collider other)
    {
        if(CompareTag("Player"))
        {
            AudioManager.instance.SetAmbienceParameter(parameterName, parameterValue);
            Debug.Log("Ambience Parameter values have Changed");
        }
    }
}
