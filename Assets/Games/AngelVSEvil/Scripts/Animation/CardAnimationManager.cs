using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace AngelVSZombie
{
    public class CardAnimationManager : MonoBehaviour
    {
        public static CardAnimationManager Instance;
        [Header("Prefabs & Setup")]
        public GameObject DummyCardPrefab;
        public Canvas MainCanvas;
        [Header("Animation Settings")]
        public float StartScale = 0.5f;
        public float PeakScale = 1.2f;
        public float DrawDuration = 0.6f;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else Destroy(gameObject);
        }

        public void AnimateCardMove(Sprite sprite, RectTransform from, RectTransform to,
            bool flipToFront, float duration = -1f, Action onComplete = null)
        {
            if (Instance == null) return;
            StartCoroutine(DrawRoutine(sprite, from, to, flipToFront, duration, onComplete));
        }

        private IEnumerator DrawRoutine(Sprite sprite, RectTransform from, RectTransform to,
            bool flipToFront, float customDuration, Action onComplete)
        {
            if (from == null || to == null) { onComplete?.Invoke(); yield break; }

            Image img = SpawnTemp(sprite, from.position, StartScale);
            if (img == null) { onComplete?.Invoke(); yield break; }

            RectTransform rt = img.rectTransform;
            float animDuration = customDuration > 0 ? customDuration : DrawDuration;
            float t = 0f;

            while (t < animDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / animDuration);
                float eased = Mathf.SmoothStep(0f, 1f, k);

                rt.position = Vector3.Lerp(from.position, to.position, eased);
                float scale = (k < 0.5f) ? Mathf.Lerp(StartScale, PeakScale, k * 2f) : Mathf.Lerp(PeakScale, 1f, (k - 0.5f) * 2f);
                rt.localScale = Vector3.one * scale;

                if (flipToFront)
                {
                    float yaw = Mathf.Lerp(180f, 0f, eased);
                    rt.localRotation = Quaternion.Euler(0f, yaw, 0f);
                }
                yield return null;
            }

            Destroy(rt.gameObject);
            onComplete?.Invoke();
        }

        private Image SpawnTemp(Sprite sprite, Vector3 pos, float scale)
        {
            if (DummyCardPrefab == null || MainCanvas == null) return null;
            GameObject go = Instantiate(DummyCardPrefab, MainCanvas.transform);
            go.transform.SetAsLastSibling();

            RectTransform rt = go.GetComponent<RectTransform>();
            if (rt == null) rt = go.AddComponent<RectTransform>();

            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            rt.position = pos;
            rt.localScale = Vector3.one * scale;

            Image img = go.GetComponent<Image>();
            if (img == null) img = go.GetComponentInChildren<Image>();
            if (img != null && sprite != null)
            {
                img.sprite = sprite;
                img.enabled = true;
                img.color = Color.white;
            }

            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;

            return img;
        }
    }
}