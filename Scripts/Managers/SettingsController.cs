using Managers;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace CardGames.Managers
{
    public class SettingsController : MonoBehaviour
    {
        // ==========================================
        // Player
        // ==========================================
        [Header("Player")]
        [SerializeField] private TMP_InputField txtName;
        [SerializeField] private Image avatarImage;

        // ==========================================
        // Avatar
        // ==========================================
        [Header("Avatar")]
        [SerializeField] private Button btnAvatar;
        [SerializeField] private GameObject avatarPanel;
        [SerializeField] private AvatarDatabase avatarDatabase;

        [Header("Avatar Scroll View")]
        [SerializeField] private Transform avatarContent;
        [SerializeField] private GameObject avatarButtonPrefab;
        [SerializeField] private Button btnCloseAvatarPanel;

        [Header("Avatar Description Panel")]
        [SerializeField] private GameObject avatarDescPanel;
        [SerializeField] private Image avatarDescImage;
        [SerializeField] private TMP_Text avatarDescName;
        [SerializeField] private TMP_Text avatarDescName2;
        [SerializeField] private TMP_Text avatarDescDesc;
        [SerializeField] private TMP_Text avatarDescDesc2;
        [SerializeField] private Button btnOK;
        [SerializeField] private Button btnCancel;

        // ==========================================
        // Sound
        // ==========================================
        [Header("Sound")]
        [SerializeField] private Toggle toggleMusic;
        [SerializeField] private Toggle toggleFX;

        // ==========================================
        // Buttons
        // ==========================================
        [Header("Buttons")]
        [SerializeField] private Button btnSave;
        [SerializeField] private Button btnBack;

        // ==========================================
        // Scene
        // ==========================================
        [Header("Scene")]
        [SerializeField] private string mainSceneName = "CartGamesFirst";

        // ==========================================
        // Current Avatar
        // ==========================================
        private int currentAvatar;
        private AvatarDefinition selectedAvatar; // آواتار در حال مشاهده در پنل توضیحات
        private List<GameObject> _spawnedButtons = new List<GameObject>();

        // ==========================================
        // Awake
        // ==========================================
        private void Awake()
        {
            FindSceneObjects();
            SetupButtons();
        }

        // ==========================================
        // Start
        // ==========================================
        private void Start()
        {
            LoadSettings();
            PopulateAvatarScrollView();

            if (avatarPanel != null) avatarPanel.SetActive(false);
            if (avatarDescPanel != null) avatarDescPanel.SetActive(false);
        }

        // ==========================================
        // Find Objects
        // ==========================================
        private void FindSceneObjects()
        {
            if (txtName == null)
            {
                GameObject obj = GameObject.Find("txtName");
                if (obj != null) txtName = obj.GetComponent<TMP_InputField>();
            }

            if (avatarImage == null)
            {
                GameObject obj = GameObject.Find("AvatarImage");
                if (obj != null) avatarImage = obj.GetComponent<Image>();
            }

            if (btnAvatar == null)
            {
                GameObject obj = GameObject.Find("btnAvatar");
                if (obj != null) btnAvatar = obj.GetComponent<Button>();
            }

            if (btnSave == null)
            {
                GameObject obj = GameObject.Find("btnSave");
                if (obj != null) btnSave = obj.GetComponent<Button>();
            }

            if (btnBack == null)
            {
                GameObject obj = GameObject.Find("btnBack");
                if (obj != null) btnBack = obj.GetComponent<Button>();
            }

            if (toggleMusic == null)
            {
                GameObject obj = GameObject.Find("ToggleMusic");
                if (obj != null) toggleMusic = obj.GetComponent<Toggle>();
            }

            if (toggleFX == null)
            {
                GameObject obj = GameObject.Find("ToggleFX");
                if (obj != null) toggleFX = obj.GetComponent<Toggle>();
            }
        }

        // ==========================================
        // Setup Buttons
        // ==========================================
        private void SetupButtons()
        {
            if (btnAvatar != null)
            {
                btnAvatar.onClick.RemoveListener(OpenAvatarPanel);
                btnAvatar.onClick.AddListener(OpenAvatarPanel);
            }

            if (btnSave != null)
            {
                btnSave.onClick.RemoveListener(SaveSettings);
                btnSave.onClick.AddListener(SaveSettings);
            }

            if (btnBack != null)
            {
                btnBack.onClick.RemoveListener(BackToMainScene);
                btnBack.onClick.AddListener(BackToMainScene);
            }

            if (btnOK != null)
            {
                btnOK.onClick.RemoveAllListeners();
                btnOK.onClick.AddListener(ConfirmAvatarSelection);
            }

            if (btnCancel != null)
            {
                btnCancel.onClick.RemoveAllListeners();
                btnCancel.onClick.AddListener(CancelAvatarSelection);
            }
            if (btnCloseAvatarPanel != null)
            {
                btnCloseAvatarPanel.onClick.RemoveAllListeners();
                btnCloseAvatarPanel.onClick.AddListener(CloseAvatarPanel);
            }
        }

        // ==========================================
        // Populate Avatar Scroll View
        // ==========================================
        private void PopulateAvatarScrollView()
        {
            if (avatarContent == null || avatarButtonPrefab == null || avatarDatabase == null)
            {
                Debug.LogError("[Settings] Avatar Content یا Prefab یا Database تنظیم نشده است!");
                return;
            }

            // پاکسازی دکمه‌های قبلی
            foreach (var btn in _spawnedButtons)
            {
                if (btn != null) Destroy(btn);
            }
            _spawnedButtons.Clear();
            foreach (Transform child in avatarContent)
            {
                Destroy(child.gameObject);
            }

            // ساخت دکمه برای هر آواتار
            for (int i = 0; i < avatarDatabase.Count; i++)
            {
                var avatar = avatarDatabase.GetByIndex(i);
                if (avatar == null) continue;

                GameObject go = Instantiate(avatarButtonPrefab, avatarContent);
                _spawnedButtons.Add(go);

                // پیدا کردن اجزای داخل پریفب
                Image btnImage = go.GetComponentInChildren<Image>();
                TMP_Text btnText = go.transform.Find("txtName")?.GetComponent<TMP_Text>();
                TMP_Text btnText2 = go.transform.Find("txtName2")?.GetComponent<TMP_Text>();

                // تنظیم تصویر و نام روی دکمه
                if (btnImage != null && avatar.Image != null)
                    btnImage.sprite = avatar.Image;

                if (btnText != null)
                {
                    btnText.text = avatar.AvatarName;
                    btnText2.text = avatar.AvatarName;
                }
                // کلیک روی دکمه → باز کردن پنل توضیحات
                Button btn = go.GetComponent<Button>();
                if (btn != null)
                {
                    int capturedIndex = i; // برای جلوگیری از مشکل closure
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => OpenAvatarDescription(capturedIndex));
                }
            }
        }

        // ==========================================
        // Open Avatar Description Panel
        // ==========================================
        private void OpenAvatarDescription(int avatarIndex)
        {
            if (avatarDatabase == null) return;

            var avatar = avatarDatabase.GetByIndex(avatarIndex);
            if (avatar == null) return;

            selectedAvatar = avatar;
            currentAvatar = avatarIndex;

            if (avatarDescPanel != null) avatarDescPanel.SetActive(true);

            if (avatarDescImage != null && avatar.Image != null)
                avatarDescImage.sprite = avatar.Image;

            if (avatarDescName != null)
                avatarDescName.text = avatar.AvatarName;

            if (avatarDescDesc != null)
                avatarDescDesc.text = avatar.Description;
            if (avatarDescName2 != null)
                avatarDescName2.text = avatar.AvatarName;

            if (avatarDescDesc2 != null)
                avatarDescDesc2.text = avatar.Description;
        }

        // ==========================================
        // Confirm Avatar Selection
        // ==========================================
        private void ConfirmAvatarSelection()
        {
            if (selectedAvatar == null) return;

            // ذخیره در داده‌های بازیکن
            PlayerData.AvatarIndex = currentAvatar;
            UpdateAvatarImage();

            // بستن پنل
            if (avatarDescPanel != null) avatarDescPanel.SetActive(false);

            Debug.Log($"[Settings] آواتار انتخاب شد: {selectedAvatar.AvatarName}");
        }

        // ==========================================
        // Cancel Avatar Selection
        // ==========================================
        private void CancelAvatarSelection()
        {
            if (avatarDescPanel != null) avatarDescPanel.SetActive(false);
            selectedAvatar = null;
        }

        // ==========================================
        // Load Settings
        // ==========================================
        private void LoadSettings()
        {
            if (txtName != null)
                txtName.text = PlayerData.PlayerName;

            currentAvatar = PlayerData.AvatarIndex;
            UpdateAvatarImage();

            if (toggleMusic != null)
                toggleMusic.isOn = SettingsData.GetMusic();

            if (toggleFX != null)
                toggleFX.isOn = SettingsData.GetFX();
        }

        // ==========================================
        // Avatar Panel (Basic)
        // ==========================================
        private void OpenAvatarPanel()
        {
            if (avatarPanel == null)
            {
                Debug.LogError("Avatar Panel is not assigned.");
                return;
            }
            avatarPanel.SetActive(true);
        }

        public void CloseAvatarPanel()
        {
            if (avatarPanel != null) avatarPanel.SetActive(false);
        }

        // ==========================================
        // Update Avatar Image (Main Screen)
        // ==========================================
        private void UpdateAvatarImage()
        {
            if (avatarImage == null || avatarDatabase == null) return;

            var avatar = avatarDatabase.GetByIndex(currentAvatar);
            if (avatar != null && avatar.Image != null)
                avatarImage.sprite = avatar.Image;
        }

        // ==========================================
        // Save Settings
        // ==========================================
        private void SaveSettings()
        {
            string playerName = "Player";
            if (txtName != null) playerName = txtName.text;

            bool music = true;
            if (toggleMusic != null) music = toggleMusic.isOn;

            bool fx = true;
            if (toggleFX != null) fx = toggleFX.isOn;

            PlayerData.SavePlayer(playerName, currentAvatar);
            SettingsData.SetMusic(music);
            SettingsData.SetFX(fx);

            Debug.Log("Player settings saved.");
        }

        // ==========================================
        // Back to Main Scene
        // ==========================================
        private void BackToMainScene()
        {
            SaveSettings();

            if (string.IsNullOrEmpty(mainSceneName))
            {
                Debug.LogError("Main Scene Name is empty.");
                return;
            }

            SceneManager.LoadScene(mainSceneName);
        }
    }
}