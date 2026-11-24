using UnityEngine;
using Managers; // برای دسترسی به AvatarDefinition

namespace AngelVSZombie
{
    [CreateAssetMenu(fileName = "NewPlayer", menuName = "AngelVSZombie/PlayerDefinition")]
    public class PlayerDefinition : ScriptableObject
    {
        public string PlayerName;
        public int AvatarIndex;
        public bool IsHuman;

        [Header("Avatar (اختیاری - برای هوش مصنوعی)")]
        public AvatarDefinition Avatar;
    }
}