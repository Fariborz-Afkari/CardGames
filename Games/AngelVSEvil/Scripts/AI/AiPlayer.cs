using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AngelVSZombie
{
    public class AiPlayer
    {
        public PlayerData Player;
        private GameEngine _engine;
        private int _hardness;
        private bool _hasPlayedOracleThisTurn = false;

        public AiPlayer(PlayerData player, GameEngine engine, int hardness = 2)
        {
            Player = player;
            _engine = engine;
            _hardness = hardness;
        }

        public List<CardDefinition> DecideCardsToPlay()
        {
            var result = new List<CardDefinition>();
            if (Player.Hand.Count == 0) return result;

            switch (_hardness)
            {
                case 1: // Easy: تصادفی
                    if (Random.value < 0.5f) return result;
                    result.Add(Player.Hand[Random.Range(0, Player.Hand.Count)]);
                    return result;

                case 2: // Normal: منطق قدیمی هارد
                    var normalCard = DecideOldHard();
                    if (normalCard != null) result.Add(normalCard);
                    return result;

                case 3: // Hard: قوانین جدید پیشرفته
                default:
                    return DecideHardAdvanced();
            }
        }

        // =====================================================
        // منطق Normal: همان چیزی که قبلاً به عنوان DecideHard بود
        // اولویت: حمله → حمله ارواح → برزدن → زبل خان
        // =====================================================
        private CardDefinition DecideOldHard()
        {
            var attack = Player.Hand.FirstOrDefault(c => c.CardType == CardType.Attack);
            if (attack != null) return attack;

            if (_engine.Data.Players.Any(p => !p.IsAlive))
            {
                var deadsAttack = Player.Hand.FirstOrDefault(c => c.CardType == CardType.DeadsAttack);
                if (deadsAttack != null) return deadsAttack;
            }

            var shuffle = Player.Hand.FirstOrDefault(c => c.CardType == CardType.Shuffle);
            if (shuffle != null) return shuffle;

            var zebelKhan = Player.Hand.FirstOrDefault(c => c.CardType == CardType.ZebelKhan);
            if (zebelKhan != null) return zebelKhan;

            return null;
        }

        // =====================================================
        // منطق Hard: قوانین جدید
        // =====================================================
        private List<CardDefinition> DecideHardAdvanced()
        {
            var result = new List<CardDefinition>();

            // قانون ۸: اگر مرده است، حتماً از خیرات استفاده کند
            if (!Player.IsAlive)
            {
                var feedDead = Player.Hand.FirstOrDefault(c => c.CardType == CardType.FeedDead);
                if (feedDead != null)
                {
                    result.Add(feedDead);
                    return result;
                }
                return result;
            }

            // قانون ۷: همیشه از هدیه استفاده کند
            var gift = Player.Hand.FirstOrDefault(c => c.CardType == CardType.Gift);
            if (gift != null)
            {
                result.Add(gift);
                return result;
            }

            // قانون ۶: اگر ۲ یا ۳ کارت از لاما، گربه، کدو یا خروس داشت، حتماً استفاده کند
            var animalTypes = new[] { CardType.Cat, CardType.Rooster, CardType.Lama, CardType.Squash };
            foreach (var type in animalTypes)
            {
                var cardsOfType = Player.Hand.Where(c => c.CardType == type).ToList();
                if (cardsOfType.Count >= 2)
                {
                    result.AddRange(cardsOfType.Take(3));
                    return result;
                }
            }

            // قانون ۱: اگر تعداد زامبی‌ها کمتر از ۳۰٪ دک باشد، اولویت با کشیدن کارت است
            int zombieCount = _engine.Data.Deck.Count(c => c.CardType == CardType.Zombie);
            if (_engine.Data.Deck.Count > 0 && (float)zombieCount / _engine.Data.Deck.Count < 0.3f)
            {
                return result; // لیست خالی = کارت بکش
            }

            // قوانین ۳، ۴، ۵: از نیمه بازی به بعد
            bool isMidGame = _engine.Data.Deck.Count < _engine.Data.InitialDeckCount * 0.5f;
            if (isMidGame)
            {
                // قانون ۳: استفاده از آینده‌بینی
                var oracle = Player.Hand.FirstOrDefault(c => c.CardType == CardType.Oracle);
                if (oracle != null && !_hasPlayedOracleThisTurn)
                {
                    _hasPlayedOracleThisTurn = true;
                    result.Add(oracle);
                    return result;
                }

                // بعد از آینده‌بینی، اگر کارت رویی زامبی بود، نوبت را با اولویت زیر رد کن:
                if (_hasPlayedOracleThisTurn && _engine.Data.Deck.Count > 0)
                {
                    CardDefinition topCard = _engine.Data.Deck[0];
                    if (topCard.CardType == CardType.Zombie)
                    {
                        var passCards = new[] {
                            CardType.Tunnel,
                            CardType.Attack,
                            CardType.DeadsAttack,
                            CardType.Zebel,
                            CardType.ZebelKhan,
                            CardType.Shuffle // قانون ۴: اگر هیچ‌کدام نبود، برزدن
                        };
                        foreach (var type in passCards)
                        {
                            var card = Player.Hand.FirstOrDefault(c => c.CardType == type);
                            if (card != null)
                            {
                                result.Add(card);
                                return result;
                            }
                        }
                    }
                }

                // قانون ۵: اگر آینده‌بینی نداشت، از تونل استفاده کند
                if (oracle == null)
                {
                    var tunnel = Player.Hand.FirstOrDefault(c => c.CardType == CardType.Tunnel);
                    if (tunnel != null)
                    {
                        result.Add(tunnel);
                        return result;
                    }
                }
            }

            // در نهایت به منطق قدیمی (Normal) برگرد
            var fallback = DecideOldHard();
            if (fallback != null) result.Add(fallback);
            return result;
        }

        public bool DecideUseAngel()
        {
            Debug.Log($"1");
            bool hasAngelInHand = Player.Hand.Any(c => c.CardType == CardType.Angel);

            // *** لاگ دیباگ ***
            Debug.Log($"[AI-Angel] Player: {Player.Definition.PlayerName} | HasAngel(in hand): {hasAngelInHand} | HasAngel(flag): {Player.HasAngel} | Dead: {_engine.Data.Players.Count(p => !p.IsAlive)}");

            if (!hasAngelInHand) return false;
            Debug.Log($"2");
            int deadCount = _engine.Data.Players.Count(p => !p.IsAlive);
            int aliveCount = _engine.Data.Players.Count(p => p.IsAlive);
            int allCount = _engine.Data.Players.Count;
            Debug.Log($"3     deadCount     =   " + deadCount);
            Debug.Log($"3     aliveCount    =   " + aliveCount);

            switch (_hardness)
            {
                case 1: // Easy: 30%
                    return Random.value < 0.3f;

                case 2: // Normal: 100%
                    return true;

                case 3: // Hard: اگر فقط 0 یا 1 مرده باشد، 50% شانس
                        // *** قانون جدید: اگر بیشتر از 1 بازیکن مرده باشد، حتماً استفاده کن ***
                    Debug.Log($"4");
                    if(allCount -deadCount==2)return true;
                    if (deadCount > 0)
                    {
                        Debug.Log($"[AI-Angel] بیش از 1 بازیکن مرده است (dead={deadCount}). استفاده از فرشته.");
                        return true;
                    }
                    return Random.value < 0.5f;

                default:
                    return true;
            }
        }
    }
}