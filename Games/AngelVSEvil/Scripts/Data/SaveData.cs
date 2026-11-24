using System.Collections.Generic;

namespace AngelVSZombie
{
    [System.Serializable]
    public class SaveData
    {
        public List<string> DeckCards = new List<string>();
        public List<string> DiscardPileCards = new List<string>();
        public List<string> GraveyardCards = new List<string>();

        public int CurrentPlayerIndex = 0;
        public int ExtraTurns = 0;
        public bool SkipDraw = false;

        public List<PlayerSaveData> Players = new List<PlayerSaveData>();
    }

    [System.Serializable]
    public class PlayerSaveData
    {
        public string PlayerName;
        public int AvatarIndex;
        public bool IsHuman;
        public bool IsAlive;
        public bool HasAngel;
        public List<string> HandCardNames = new List<string>();
    }
}