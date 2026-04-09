using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Fades a UI Text element's alpha in and out using a ping-pong wave.
/// Attach to any GameObject and assign the target Text component.
/// Uses unscaled time so it works while Time.timeScale is 0 (menus).
/// </summary>
public class FadeText : MonoBehaviour
{
    [Tooltip("The Text component to fade. If left empty, tries GetComponent on this object.")]
    public Text textToFade;

    [Tooltip("Speed of the fade cycle (higher = faster).")]
    public float fadeSpeed = 1.5f;

    private void Awake()
    {
        if (textToFade == null)
            textToFade = GetComponent<Text>();
    }

    private void Update()
    {
        if (textToFade == null) return;

        float alpha = Mathf.PingPong(Time.unscaledTime * fadeSpeed, 1f);
        Color color = textToFade.color;
        color.a = alpha;
        textToFade.color = color;
    }
}
