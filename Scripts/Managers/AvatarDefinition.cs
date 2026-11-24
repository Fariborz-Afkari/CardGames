using UnityEngine;

namespace Managers
{
    [CreateAssetMenu(fileName = "NewAvatar", menuName = "Managers/AvatarDefinition")]
    public class AvatarDefinition : ScriptableObject
    {
        public int AvatarId;          // 1..60
        public string AvatarName;
        [TextArea] public string Description;
        public Sprite Image;
        public Geneder geneder;
        public int SkillLevel;
    }

    public enum Geneder
    {
        man,
        woman
    }
}