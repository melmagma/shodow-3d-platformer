using UnityEngine;
using TMPro;

public class KeypadLock : MonoBehaviour
{
    string keypadValue;
    public GameObject lockedDoor;
    public GameObject keypadPanel;
    private bool atKeypad = false;
    private Keycode Keycode;
    int correctCode;
    private TextMeshProUGUI KeypadText;

    void Update()
    {
        KeypadText.text = keypadValue;
        correctCode = Keycode.KeycodeGen;
        correctCode.ToString("correctCode");

        if (keypadValue == "correctCode")
        {

            keypadPanel.SetActive(false);
            lockedDoor.SetActive(false);

        }

        if (keypadValue.Length >= 4)
        {
            keypadValue = "";
        }

    }


    private void OnTriggerEnter(Collider other)
    {
        if (other.tag == "Player")
        {
            atKeypad = true;
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (other.tag == "Player")
        {
            atKeypad = false;
            keypadPanel.SetActive(false);
        }
    }

}
