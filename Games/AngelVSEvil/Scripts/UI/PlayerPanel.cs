using System;
using UnityEngine;
using UnityEngine.UI;
using RTLTMPro;

namespace AngelVSZombie
{
    public class PlayerPanel : MonoBehaviour
    {
        [Header("UI Components")]
        public Image AvatarImage;
        public RTLTextMeshPro txtPlayerName;
        public Image BackgroundImage;
        public Button TargetButton;

        [Header("Turn Indicator (نشانگر نوبت)")]
        public GameObject TurnIndicator; // مثلاً یک Image (حاشیه زرد/طلایی) یا یک فلش

        [Header("Background Sprites")]
        public Sprite AliveBackground;
        public Sprite DeadBackground;

        public PlayerData _playerData;
        private Action<PlayerData> _onTargetClicked;

        [Header("Turn Scale")]
        public float NormalScale = 1f;
        public float ActiveTurnScale = 1.15f;
        public float ScaleAnimDuration = 0.25f;
        private Coroutine _scaleCoroutine;


        private void Awake()
        {
            // پیدا کردن دکمه به صورت خودکار اگر در Inspector وصل نشده باشد
            if (TargetButton == null) TargetButton = GetComponent<Button>();
            if (TargetButton == null) TargetButton = GetComponentInChildren<Button>();

            if (TargetButton != null)
            {
                TargetButton.onClick.RemoveAllListeners(); // جلوگیری از ثبت چندباره
                TargetButton.onClick.AddListener(HandleClick);
            }
        }

        //public void Setup(PlayerData player)
        //{
        //    _playerData = player;

        //    if (txtPlayerName != null) txtPlayerName.text = player.Definition.PlayerName;

        //    if (AvatarImage != null && player.Definition.Avatar != null && player.Definition.Avatar.Image != null)
        //    {
        //        AvatarImage.sprite = player.Definition.Avatar.Image;
        //        AvatarImage.preserveAspect = true;
        //    }

        //    if (TargetButton != null) TargetButton.interactable = false;
        //    SetTurn(false);
        //    UpdateVisuals();
        //    transform.localScale = Vector3.one * NormalScale;
        //}14050825 2
        public void Setup(PlayerData player, Managers.AvatarDatabase avatarDatabase)
        {
            _playerData = player;

            if (txtPlayerName != null)
                txtPlayerName.text = player.Definition.PlayerName;

            Sprite avatarSprite = null;

            if (player.Definition.IsHuman)
            {
                // *** بازیکن انسانی: خواندن از Managers.PlayerData ***
                if (avatarDatabase != null)
                {
                    int humanAvatarIndex = Managers.PlayerData.AvatarIndex;
                    var humanAvatar = avatarDatabase.GetByIndex(humanAvatarIndex);
                    if (humanAvatar != null && humanAvatar.Image != null)
                        avatarSprite = humanAvatar.Image;
                }
            }
            else
            {
                // *** هوش مصنوعی: خواندن از Definition.Avatar ***
                if (player.Definition.Avatar != null && player.Definition.Avatar.Image != null)
                    avatarSprite = player.Definition.Avatar.Image;
            }

            if (AvatarImage != null && avatarSprite != null)
            {
                AvatarImage.sprite = avatarSprite;
                AvatarImage.preserveAspect = true;
            }

            if (TargetButton != null) TargetButton.interactable = false;

            SetTurn(false);
            UpdateVisuals();
            transform.localScale = Vector3.one * NormalScale;
        }
        public void SetAsTarget(Action<PlayerData> onTargetClicked)
        {
            _onTargetClicked = onTargetClicked;
            // دکمه را برای کلیک فعال می‌کنیم
            if (TargetButton != null) TargetButton.interactable = true;
        }

        private void HandleClick()
        {
            //Debug.Log($"[PlayerPanel] کلیک شد روی: {_playerData?.Definition.PlayerName}");
            _onTargetClicked?.Invoke(_playerData);
        }

        public void UpdateVisuals()
        {
            if (_playerData == null) return;

            if (BackgroundImage != null)
            {
                Sprite target = _playerData.IsAlive ? AliveBackground : DeadBackground;
                if (target != null) BackgroundImage.sprite = target;
            }

            if (AvatarImage != null)
            {
                AvatarImage.color = _playerData.IsAlive ? Color.white : new Color(0.4f, 0.4f, 0.4f, 1f);
            }
        }

        public void SetTurn(bool isActive)
        {
            if (TurnIndicator != null)
            {
                TurnIndicator.SetActive(isActive);
            }
        }
        public void SetActiveTurn(bool isActive)
        {
            float targetScale = isActive ? ActiveTurnScale : NormalScale;

            if (_scaleCoroutine != null) StopCoroutine(_scaleCoroutine);

            // اگر بازی فعال نیست، فوراً تغییر بده
            if (!gameObject.activeInHierarchy)
            {
                transform.localScale = Vector3.one * targetScale;
                return;
            }

            _scaleCoroutine = StartCoroutine(AnimateScale(targetScale));
        }

        private System.Collections.IEnumerator AnimateScale(float targetScale)
        {
            Vector3 startScale = transform.localScale;
            Vector3 endScale = Vector3.one * targetScale;

            float t = 0f;
            while (t < ScaleAnimDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / ScaleAnimDuration);
                float eased = Mathf.SmoothStep(0f, 1f, k);
                transform.localScale = Vector3.Lerp(startScale, endScale, eased);
                yield return null;
            }
            transform.localScale = endScale;
            _scaleCoroutine = null;
        }
    }
}