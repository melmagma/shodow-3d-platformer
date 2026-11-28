using UnityEngine;
using TMPro;

public class Keycode : MonoBehaviour
{
    public int KeycodeGen;
    public TextMeshProUGUI GeneratedCode;

    void Start()
    {

        KeycodeGen = Random.Range(0, 10000);
        KeycodeGen.ToString("D4");
        GeneratedCode.text = "KeycodeGen";

        Debug.Log(KeycodeGen);

    }

}
