using UnityEngine;
using UnityEngine.UI;
using System.Collections;

namespace SomethingNearby
{
    /// <summary>
    /// A script that manages the game object and the attached text component
    /// </summary>
    internal class MyMessageText : MonoBehaviour
    {
        private Text textComponent;
        private Coroutine messageDisplay;
        public bool DisplayingMessage => messageDisplay != null;
        public void Initialize(Transform parent, Vector2 aMin, Vector2 aMax, Font font, Color color, int fontSize)
        {
            transform.parent = parent;
            transform.localPosition = Vector3.zero;

            gameObject.AddComponent<CanvasRenderer>();
            var rt = gameObject.AddComponent<RectTransform>();
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            textComponent = gameObject.AddComponent<Text>();
            textComponent.font = font;
            textComponent.text = "";
            Color initialColor = color;
            initialColor.a = 0; // start invisible
            textComponent.color = initialColor;
            textComponent.fontSize = fontSize;
            textComponent.alignment = TextAnchor.MiddleRight;
            textComponent.raycastTarget = false;
        }


        private IEnumerator ShowMessageAndFadeAway(string message)
        {
            float fadeInTime = SomethingNearby.Instance.FadeInTime;
            float nonFadeTime = SomethingNearby.Instance.MessageDuration;
            float fadeOutTime = SomethingNearby.Instance.FadeOutTime;
            float time = 0f;
            textComponent.text = message;
            Color ogColor = SomethingNearby.Instance.MessageColor;
            ogColor.a = 0;
            textComponent.color = ogColor;
            ogColor = SomethingNearby.Instance.MessageColor;
            do
            {
                time += Time.deltaTime;
                var color = textComponent.color;

                if (time < fadeInTime)
                {
                    color.a = Mathf.Lerp(0, ogColor.a, (time) / (fadeOutTime));
                }
                else if (time > nonFadeTime)
                {
                    color.a = Mathf.Lerp(ogColor.a, 0, (time - nonFadeTime) / (fadeOutTime));
                }
                else
                {
                    color.a = ogColor.a;
                }
                textComponent.color = color;
                yield return null;
            } while (time < fadeInTime + nonFadeTime + fadeOutTime);

            messageDisplay = null;
            SomethingNearby.Instance.ShowNextQueuedMessage();
        }

        public void ShowNextMessage(string message)
        {
            messageDisplay = StartCoroutine(ShowMessageAndFadeAway(message));
        }

        private void OnDestroy()
        {
            if (messageDisplay != null)
            {
                StopCoroutine(messageDisplay);
            }
        }
    }
}
