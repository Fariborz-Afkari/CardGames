using UnityEngine;
using UnityEngine.UI;
using RTLTMPro;

namespace AngelVSZombie
{
    public class CardWidget : MonoBehaviour
    {
        public Image CardImage;
        public RTLTextMeshPro ValueText;
        public Button Button;
        public GameObject CardBackVisual; // آبجکتی برای نمایش پشت کارت (اختیاری)

        public CardDefinition BoundCard { get; private set; }
        private GameObject _root;

        private void Awake()
        {
            InitReferences();
        }

        private void InitReferences()
        {
            // روت (والد) که همان دکمه است
            if (_root == null)
                _root = transform.parent != null ? transform.parent.gameObject : gameObject;

            // پیدا کردن دکمه از روی والد
            if (Button == null) Button = _root.GetComponent<Button>();
            if (Button == null) Button = GetComponent<Button>();
            if (Button == null) Button = _root.GetComponentInChildren<Button>(true);

            // پیدا کردن تصویر: اول روی والد، بعد خودمان، بعد در فرزندان والد (که شامل خودمان و خواهر/برادرم می‌شود)
            if (CardImage == null) CardImage = _root.GetComponent<Image>();
            if (CardImage == null) CardImage = GetComponent<Image>();
            if (CardImage == null) CardImage = _root.GetComponentInChildren<Image>(true);

            // پیدا کردن متن
            if (ValueText == null) ValueText = _root.GetComponentInChildren<RTLTextMeshPro>(true);
            if (ValueText == null) ValueText = GetComponentInChildren<RTLTextMeshPro>(true);
        }

        public void Show(bool show)
        {
            if (_root != null) _root.SetActive(show);
            else gameObject.SetActive(show);
        }

        // متد جدید برای پشت و رو کردن کارت
        public void SetFaceDown(bool faceDown)
        {
            if (CardImage != null) CardImage.enabled = !faceDown;
            if (ValueText != null) ValueText.gameObject.SetActive(!faceDown);

            // اگر آبجکتی برای پشت کارت تعریف کرده باشید، آن را فعال می‌کند
            if (CardBackVisual != null) CardBackVisual.SetActive(faceDown);
        }

        public void Bind(CardDefinition c, int count = 1)
        {
            BoundCard = c;
            SetFaceDown(false); // به طور پیش‌فرض رو باشد

            if (c == null)
            {
                if (CardImage != null) CardImage.enabled = false;
                if (ValueText != null) ValueText.text = "";
                return;
            }

            if (CardImage != null)
            {
                CardImage.enabled = true;
                CardImage.sprite = c.Image;
                CardImage.preserveAspect = true;
                CardImage.color = Color.white;
            }

            if (ValueText != null)
            {
                ValueText.text = $"{ToPersianNumber(count)}";
            }
        }

        private string ToPersianNumber(int number)
        {
            string english = number.ToString();
            string persian = "";
            foreach (char c in english)
            {
                if (c >= '0' && c <= '9')
                    persian += char.ConvertFromUtf32(c - '0' + '۰');
                else
                    persian += c;
            }
            return persian;
        }

        /// <summary>
        /// این متد برای سازگاری با کدهای قبلی اضافه شده است
        /// </summary>
        public void Setup(CardDefinition card, bool faceDown = false, bool interactive = true)
        {
            Show(true);          // نمایش کارت (فعال کردن والد)
            Bind(card);          // تنظیم تصویر و نام
            SetFaceDown(faceDown); // تنظیم رو یا پشت بودن

            // تنظیم قابلیت کلیک
            if (Button != null)
            {
                Button.interactable = interactive;
            }
        }
    }
}