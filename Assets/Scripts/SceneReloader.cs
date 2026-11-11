using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class SceneRefresher : MonoBehaviour
{
    [SerializeField] private Key refreshKey = Key.F5;
    [SerializeField] private float delay = 0f;

    private bool isRefreshing;

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[refreshKey].wasPressedThisFrame)
            Refresh();
    }

    // Public so you can also call it from a UI Button, a UnityEvent, or another script
    public void Refresh()
    {
        if (isRefreshing) return;
        StartCoroutine(RefreshRoutine());
    }

    private IEnumerator RefreshRoutine()
    {
        isRefreshing = true;
        Time.timeScale = 1f; // in case the scene was paused or slowed when the bug hit

        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        AsyncOperation op = SceneManager.LoadSceneAsync(SceneManager.GetActiveScene().buildIndex);
        while (!op.isDone)
            yield return null;
    }
}
