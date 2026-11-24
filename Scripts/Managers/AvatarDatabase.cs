using System.Collections.Generic;
using UnityEngine;

namespace Managers
{
    [CreateAssetMenu(fileName = "AvatarDatabase", menuName = "Managers/Avatar Database")]
    public class AvatarDatabase : ScriptableObject
    {
        [Tooltip("همه آواتارهای بازی (هم برای انسان، هم برای هوش مصنوعی)")]
        public List<AvatarDefinition> AllAvatars = new List<AvatarDefinition>();

        public int Count => AllAvatars.Count;

        public AvatarDefinition GetByIndex(int index)
        {
            if (index < 0 || index >= AllAvatars.Count) return null;
            return AllAvatars[index];
        }

        public AvatarDefinition GetById(int avatarId)
        {
            return AllAvatars.Find(a => a != null && a.AvatarId == avatarId);
        }

        public int GetIndexById(int avatarId)
        {
            return AllAvatars.FindIndex(a => a != null && a.AvatarId == avatarId);
        }

        public List<AvatarDefinition> GetBySkillLevel(int skillLevel)
        {
            return AllAvatars.FindAll(a => a != null && a.SkillLevel == skillLevel);
        }
    }
}