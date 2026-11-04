using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace AngelVSZombie
{
    public class UiAnimation : MonoBehaviour
    {
        [Header("References")]
        public GameUIController Controller;
        public GameFlowController Flow;

        [Header("Timing")]
        public float DrawDuration = 0.35f;
        public float BurnDuration = 0.7f;
        public float CollectDuration = 0.5f;

        [Header("Scales")]
        public float StartScale = 0.4f;
        public float PeakScale = 1.15f;

        [Header("Debug")]
        public bool VerboseLog = true;

        private RectTransform _animLayer;
        private Canvas _canvas;

        private float GameSpeed => (Flow != null) ? Flow.GameSpeed : 1f;
        private float D(float seconds) => seconds / Mathf.Max(0.01f, GameSpeed);

        private void EnsureLayer()
        {
            if (_animLayer != null) return;
            if (Controller == null) return;

            _canvas = Controller.GetComponentInParent<Canvas>();
            if (_canvas == null)
            {
                if (VerboseLog) Debug.LogWarning("[UiAnimation] No Canvas found in parents of Controller.");
                return;
            }

            var go = new GameObject("AnimLayer", typeof(RectTransform));
            _animLayer = go.GetComponent<RectTransform>();
            _animLayer.SetParent(_canvas.transform, false);
            _animLayer.anchorMin = Vector2.zero;
            _animLayer.anchorMax = Vector2.one;
            _animLayer.offsetMin = Vector2.zero;
            _animLayer.offsetMax = Vector2.zero;
            _animLayer.SetAsLastSibling();
        }

        private Image SpawnTemp(Sprite sprite, Vector3 worldPos, float width, float scale)
        {
            EnsureLayer();
            if (_animLayer == null) { if (VerboseLog) Debug.LogWarning("[UiAnimation] AnimLayer null, cannot spawn."); return null; }
            if (sprite == null) { if (VerboseLog) Debug.LogWarning("[UiAnimation] Sprite null."); return null; }

            var go = new GameObject("TmpCard", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(_animLayer, false);

            // تنظیم مرکزیت برای Lerp
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            rt.sizeDelta = new Vector2(width, width * 1.4f);
            rt.position = worldPos;
            rt.localScale = Vector3.one * scale;
            rt.SetAsLastSibling();

            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            img.preserveAspect = true;
            return img;
        }

        // ==========================================
        // 1. Animate Draw Card (Deck -> Player Bank)
        // ==========================================
        public void AnimateDrawCard(CardDefinition c, System.Action onComplete = null)
        {
            Debug.Log($"[UiAnimation] AnimateDrawCard شروع شد. کارت: {(c != null ? c.CardName : "NULL")}");

            if (c == null) { Debug.LogWarning("[UiAnimation] کارت NULL است!"); onComplete?.Invoke(); return; }
            if (c.Image == null) { Debug.LogWarning($"[UiAnimation] تصویر (Sprite) کارت {c.CardName} خالی است! فایل CardDefinition را بررسی کنید."); onComplete?.Invoke(); return; }
            if (Controller == null) { Debug.LogWarning("[UiAnimation] Controller تنظیم نشده است!"); onComplete?.Invoke(); return; }

            if (Controller.DeckButton == null) { Debug.LogWarning("[UiAnimation] DeckButton در GameUIController تنظیم نشده است!"); onComplete?.Invoke(); return; }

            Transform from = Controller.DeckButton.transform;

            // پیدا کردن خانه خالی
            Transform to = null;
            Debug.Log($"[UiAnimation] تعداد PlayerBankWidgets: {Controller.PlayerBankWidgets.Count}");

            for (int i = 0; i < Controller.PlayerBankWidgets.Count; i++)
            {
                var widget = Controller.PlayerBankWidgets[i];
                if (widget == null) { Debug.LogWarning($"[UiAnimation] ویجت {i} در لیست NULL است!"); continue; }

                if (!widget.gameObject.activeSelf)
                {
                    Debug.Log($"[UiAnimation] خانه خالی پیدا شد در ایندکس {i}: {widget.name}");
                    to = widget.transform.parent != null ? widget.transform.parent : widget.transform;
                    if (widget.transform.parent != null) widget.transform.parent.gameObject.SetActive(true);
                    else widget.gameObject.SetActive(true);
                    break;
                }
            }

            if (to == null && Controller.PlayerBankParent != null)
            {
                Debug.Log("[UiAnimation] خانه خالی پیدا نشد. به کل پنل بانک می‌رویم.");
                to = Controller.PlayerBankParent;
            }

            if (to == null)
            {
                Debug.LogWarning("[UiAnimation] مقصد (Bank) پیدا نشد!");
                onComplete?.Invoke();
                return;
            }

            Debug.Log($"[UiAnimation] شروع Coroutine. From: {from.name} at {from.position}, To: {to.name} at {to.position}");
            StartCoroutine(DrawRoutine(c.Image, from, to, onComplete));
        }

        private IEnumerator DrawRoutine(Sprite sprite, Transform from, Transform to, System.Action onComplete)
        {
            RectTransform fromRect = from as RectTransform;
            float w = (fromRect != null && fromRect.rect.width > 1f) ? fromRect.rect.width : 100f;

            var img = SpawnTemp(sprite, from.position, w, StartScale);
            if (img == null) { onComplete?.Invoke(); yield break; }

            var rt = img.rectTransform;

            // *** استفاده از D() برای اعمال سرعت ***
            float duration = D(DrawDuration);

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                float eased = Mathf.SmoothStep(0f, 1f, k);

                rt.position = Vector3.Lerp(from.position, to.position, eased);

                float scale = (k < 0.5f)
                    ? Mathf.Lerp(StartScale, PeakScale, k * 2f)
                    : Mathf.Lerp(PeakScale, 1f, (k - 0.5f) * 2f);
                rt.localScale = Vector3.one * scale;

                float yaw = Mathf.Lerp(180f, 0f, eased);
                rt.localRotation = Quaternion.Euler(0f, yaw, 0f);

                yield return null;
            }

            Destroy(rt.gameObject);
            onComplete?.Invoke();
        }

        // ==========================================
        // 2. Animate Burn Cards (Board -> Discard Pile)
        // ==========================================
        public void AnimateBurnCards(System.Action onComplete = null)
        {
            if (Controller == null) { onComplete?.Invoke(); return; }

            // مقصد: دک سوخته (از DiscardCardWidget استفاده می‌کنیم که همیشه وجود دارد)
            Transform target = Controller.DiscardCardWidget != null
                ? Controller.DiscardCardWidget.transform
                : null;

            if (target == null)
            {
                onComplete?.Invoke();
                return;
            }

            int n = Controller.BoardSlots.Count;
            bool hasCards = false;

            for (int i = 0; i < n; i++)
            {
                var slot = Controller.BoardSlots[i];
                if (slot == null || slot.BoundCard == null || !slot.gameObject.activeInHierarchy) continue;

                if (slot.BoundCard.Image != null)
                {
                    hasCards = true;
                    StartCoroutine(BurnRoutine(slot, target, slot.BoundCard.Image));
                }
            }

            if (!hasCards) { onComplete?.Invoke(); return; }

            StartCoroutine(DelayCallback(D(BurnDuration), onComplete)); // ← D() اضافه شد
        }

        private IEnumerator BurnRoutine(CardWidget slot, Transform target, Sprite sprite)
        {
            Vector3 startPos = slot.transform.position;
            Vector3 endPos = target.position;

            float w = 100f;
            var rect = slot.GetComponent<RectTransform>();
            if (rect != null && rect.rect.width > 1f) w = rect.rect.width;

            var img = SpawnTemp(sprite, startPos, w, 1f);
            if (img == null) yield break;

            var rt = img.rectTransform;
            var cg = img.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;

            float t = 0f;
            float duration = D(BurnDuration); // ← D() اضافه شد
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                float eased = Mathf.SmoothStep(0f, 1f, k);

                rt.position = Vector3.Lerp(startPos, endPos, eased);
                rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.15f, eased);
                rt.localRotation = Quaternion.Euler(0f, Mathf.Lerp(0f, 540f, eased), 0f);
                cg.alpha = Mathf.Lerp(1f, 0f, eased);

                yield return null;
            }

            Destroy(rt.gameObject);
        }

        // ==========================================
        // 3. Animate Collect (Board -> Bank or Player Panel)
        // ==========================================
        public void AnimateCollect(PlayerData p, System.Action onDone)
        {
            StartCoroutine(CollectRoutine(p, onDone));
        }

        private IEnumerator CollectRoutine(PlayerData p, System.Action onDone)
        {
            if (p == null || Controller == null || Controller.BoardSlots.Count == 0)
            {
                yield return new WaitForSeconds(0.2f);
                onDone?.Invoke();
                yield break;
            }

            int n = Controller.BoardSlots.Count;
            float stagger = 0.06f;

            for (int i = 0; i < n; i++)
            {
                var slot = Controller.BoardSlots[i];
                if (slot == null || slot.BoundCard == null || !slot.gameObject.activeInHierarchy) continue;

                Vector3 from = slot.transform.position;
                Vector3 to = Vector3.zero;

                if (p.Definition.IsHuman)
                {
                    to = Controller.PlayerBankParent != null ? Controller.PlayerBankParent.position : Vector3.zero;
                }
                else
                {
                    RectTransform panel = Controller.GetPlayerPanelTransform(p);
                    to = panel != null ? panel.position : (Controller.PlayerBankParent != null ? Controller.PlayerBankParent.position : Vector3.zero);
                }

                StartCoroutine(FlyTo(from, to, slot.BoundCard.Image, slot));
                yield return new WaitForSeconds(stagger);
            }

            yield return new WaitForSeconds(D(CollectDuration)); // ← D() اضافه شد
            onDone?.Invoke();
        }

        private IEnumerator FlyTo(Vector3 from, Vector3 to, Sprite sprite, CardWidget slot)
        {
            float w = 100f;
            var rect = slot.GetComponent<RectTransform>();
            if (rect != null && rect.rect.width > 1f) w = rect.rect.width;

            var img = SpawnTemp(sprite, from, w, 1f);
            if (img == null) yield break;

            var rt = img.rectTransform;
            float t = 0f;
            float duration = CollectDuration;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                float eased = Mathf.SmoothStep(0f, 1f, k);
                rt.position = Vector3.Lerp(from, to, eased);
                rt.localScale = Vector3.one * Mathf.Lerp(1f, 0.4f, eased);
                yield return null;
            }
            Destroy(rt.gameObject);
        }

        private IEnumerator DelayCallback(float delay, System.Action onComplete)
        {
            yield return new WaitForSeconds(delay);
            onComplete?.Invoke();
        }

        public void PulseElement(RectTransform rt, float duration = 0.3f)
        {
            if (rt == null) return;
            StartCoroutine(PulseRoutine(rt, duration));
        }

        private IEnumerator PulseRoutine(RectTransform rt, float duration)
        {
            var baseScale = rt.localScale;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = t / duration;
                float s = 1f + Mathf.Sin(k * Mathf.PI) * 0.25f;
                rt.localScale = baseScale * s;
                yield return null;
            }
            rt.localScale = baseScale;
        }
    }
}