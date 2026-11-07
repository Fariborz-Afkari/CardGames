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

        [Header("Card Back")]
        [Tooltip("تصویر پشت کارت برای انیمیشن‌های هوش مصنوعی")]
        public Sprite CardBackSprite;

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
            StartCoroutine(DrawRoutine(c.Image, from, to, onComplete, faceDown: false));
        }

        private IEnumerator DrawRoutine(Sprite sprite, Transform from, Transform to, System.Action onComplete, bool faceDown = false)
        {
            RectTransform fromRect = from as RectTransform;
            float w = (fromRect != null && fromRect.rect.width > 1f) ? fromRect.rect.width : 100f;

            var img = SpawnTemp(sprite, from.position, w, StartScale);
            if (img == null) { onComplete?.Invoke(); yield break; }

            var rt = img.rectTransform;

            // اضافه کردن CanvasGroup برای کنترل شفافیت
            CanvasGroup cg = img.gameObject.GetComponent<CanvasGroup>();
            if (cg == null) cg = img.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;

            // *** محاسبه صحیح مقصد برای هوش مصنوعی (حرکت به داخل پنل) ***
            Vector3 finalPos = to.position;
            if (faceDown)
            {
                RectTransform toRect = to as RectTransform;
                if (toRect != null)
                {
                    // تبدیل 30% ارتفاع پنل (به سمت پایین در مختصات محلی) به مختصات جهانی
                    finalPos = toRect.TransformPoint(new Vector3(0f, -toRect.rect.height * 0.3f, 0f));
                }
            }

            // برای AI کمی زمان بیشتر (حرکت نرم‌تر و محو شدن)
            float duration = D(DrawDuration) * (faceDown ? 1.4f : 1f);
            float t = 0f;

            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                float eased = Mathf.SmoothStep(0f, 1f, k);

                rt.position = Vector3.Lerp(from.position, finalPos, eased);

                // مقیاس: برای AI کارت در انتها کوچک می‌شود (0.15)، برای انسان اندازه کامل (1)
                float endScale = faceDown ? 0.15f : 1f;
                float scale;
                if (k < 0.5f)
                    scale = Mathf.Lerp(StartScale, PeakScale, k * 2f);
                else
                    scale = Mathf.Lerp(PeakScale, endScale, (k - 0.5f) * 2f);
                rt.localScale = Vector3.one * scale;

                if (faceDown)
                {
                    // برای AI: چرخش ملایم حول Z + محو شدن تدریجی در 40% آخر
                    rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(10f, 0f, eased));
                    cg.alpha = Mathf.Lerp(1f, 0f, Mathf.Clamp01((k - 0.6f) / 0.4f));
                }
                else
                {
                    // برای انسان: چرخش Yaw (پشت به رو) و شفافیت کامل
                    float yaw = Mathf.Lerp(180f, 0f, eased);
                    rt.localRotation = Quaternion.Euler(0f, yaw, 0f);
                    cg.alpha = 1f;
                }

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

            Transform target = Controller.DiscardCardWidget != null
                ? Controller.DiscardCardWidget.transform
                : null;

            if (target == null) { onComplete?.Invoke(); return; }

            int n = Controller.BoardSlots.Count;
            int activeCount = 0;

            for (int i = 0; i < n; i++)
            {
                var slotWidget = Controller.BoardSlots[i];
                if (slotWidget != null && slotWidget.BoundCard != null)
                {
                    activeCount++;
                }
            }

            if (activeCount == 0) { onComplete?.Invoke(); return; }

            int index = 0;
            for (int i = 0; i < n; i++)
            {
                var slotWidget = Controller.BoardSlots[i];
                if (slotWidget == null || slotWidget.BoundCard == null) continue;

                // *** موقعیت واقعی از والد (Slot_00) گرفته می‌شود ***
                Transform slotTransform = slotWidget.transform.parent != null
                    ? slotWidget.transform.parent
                    : slotWidget.transform;

                if (slotWidget.BoundCard.Image != null)
                {
                    StartCoroutine(BurnRoutine(slotWidget, slotTransform, target, slotWidget.BoundCard.Image, index * 0.15f));
                    index++;
                }
            }

            float totalTime = D(BurnDuration) + (activeCount - 1) * 0.15f;
            StartCoroutine(DelayCallback(totalTime, onComplete));
        }

        private IEnumerator BurnRoutine(CardWidget slotWidget, Transform slotTransform, Transform target, Sprite sprite, float delay)
        {
            if (delay > 0) yield return new WaitForSeconds(delay);

            // موقعیت شروع از والد (Slot_00) گرفته می‌شود
            Vector3 startPos = slotTransform.position;
            Vector3 endPos = target.position;

            float w = 100f;
            RectTransform rect = slotTransform as RectTransform;
            if (rect != null && rect.rect.width > 1f) w = rect.rect.width;

            var img = SpawnTemp(sprite, startPos, w, 1f);
            if (img == null) yield break;

            // حالا کارت اصلی را مخفی می‌کنیم
            slotWidget.Show(false);

            var rt = img.rectTransform;
            var cg = img.gameObject.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;

            float t = 0f;
            float duration = D(BurnDuration);
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

        public void AnimateDrawForAI(CardDefinition c, PlayerData aiPlayer, System.Action onComplete = null)
        {
            if (Controller == null) { onComplete?.Invoke(); return; }

            Transform from = Controller.DeckButton != null ? Controller.DeckButton.transform : null;
            if (from == null) { onComplete?.Invoke(); return; }

            RectTransform toRect = Controller.GetPlayerPanelTransform(aiPlayer);
            if (toRect == null) { onComplete?.Invoke(); return; }

            // *** استفاده از تصویر پشت کارت ***
            Sprite spriteToUse = CardBackSprite;
            if (spriteToUse == null && c != null) spriteToUse = c.Image; // fallback اگر تصویر پشت تنظیم نشده بود

            StartCoroutine(DrawRoutine(spriteToUse, from, toRect, onComplete, faceDown: true));
        }
    }
}