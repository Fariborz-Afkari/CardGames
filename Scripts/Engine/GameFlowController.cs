using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace AngelVSZombie
{
    public class GameFlowController : MonoBehaviour
    {
        public GameUIController UI;
        public GameState CurrentState;

        [Header("Available Player Templates")]
        public List<PlayerDefinition> PlayerDefinitions;

        [Header("All Cards")]
        public List<CardDefinition> AllCards;

        [Header("Unique Cards (برای پنل 3 کارتی)")]
        public List<CardDefinition> AllUniqueCards = new List<CardDefinition>();

        private GameData _gameData;
        private GameEngine _engine;
        private AiPlayer _aiPlayer;

        private bool _isDying = false;
        private bool _angelClicked = false;
        public bool IsPlayerDying => _isDying;

        private bool _waitingForInput = false;

        // نوبت‌های اضافه‌ای که باید به بازیکن بعدی داده شود (ناشی از Attack / DeadsAttack)
        private int _extraTurnsForNextPlayer = 0;

        private int _initialPlayerCount = 2;
        private int _hardness = 2;
        private float _speed = 3f;

        private CardDefinition _tempTopCard;
        private CardDefinition _tempSecondCard;

        //private CardDefinition _pendingCard = null;
        //private PlayerData _pendingPlayer = null;
        private int _nopeCount = 0;
        private bool _isNopeWindowOpen = false;
        public float GameSpeed => _speed / 3f; // سرعت پیش‌فرض 3 است، پس GameSpeed=1 حالت عادی است

        public bool IsNopeWindowOpen => _isNopeWindowOpen;

        private int _revealedZombieIndex = -1; // -1 یعنی هیچ زامبی برگشتی وجود ندارد

        private CardDefinition _currentAnimalCard = null;
        private List<CardDefinition> _animalCardsPlayed = new List<CardDefinition>();
        private Coroutine _animalTimer = null;

        private PlayerData _activePlayer = null;
        private List<CardDefinition> _pendingComboList = new List<CardDefinition>();
        private Coroutine _playTimerCoroutine = null;

        private PlayerData _comboTargetPlayer = null;
        private System.Action<CardDefinition> _onCardStolenCallback = null;

        private PlayerData _lastNopePlayer = null;
        public PlayerData LastNopePlayer => _lastNopePlayer; // برای دسترسی از UI

        public CardType? CurrentPendingComboType => _pendingComboList.Count > 0 ? _pendingComboList[0].CardType : (CardType?)null;
        public bool CanAddToCombo => _pendingComboList.Count > 0 && _pendingComboList.Count < 3;

        public bool HasNopeInCombo => _pendingComboList.Any(c => c.CardType == CardType.Nope);

        [Header("Animation")]
        public UiAnimation UiAnimator;

        private void Start()
        {
            if (AllCards != null && AllCards.Count > 0)
            {
                AllUniqueCards = AllCards
                    .Where(c => c.CardType != CardType.Zombie) // *** این خط برای حذف زامبی اضافه شد ***
                    .GroupBy(c => c.CardType)
                    .Select(g => g.First())
                    .OrderBy(c => c.CardType)
                    .ToList();

                Debug.Log($"لیست کارت‌های یکتا ساخته شد. تعداد کل کارت‌ها: {AllCards.Count} | تعداد کارت‌های یکتا: {AllUniqueCards.Count}");
            }
        }

        // ============================================================
        // شروع و بارگذاری بازی
        // ============================================================

        public void StartGame(int playerCount, int hardness, float speed)
        {
            _initialPlayerCount = playerCount;
            _hardness = hardness;
            _speed = speed;

            if (UI == null) UI = FindObjectOfType<GameUIController>();
            if (UI == null)
            {
                Debug.LogError("GameUIController در صحنه پیدا نشد!");
                return;
            }

            _gameData = new GameData();
            _engine = new GameEngine(_gameData, UI, this);
            _extraTurnsForNextPlayer = 0;
            _waitingForInput = false;

            if (AllCards.Count == 0)
            {
                Debug.LogError("لیست کارت‌ها خالی است!");
                return;
            }

            var defs = BuildPlayerDefinitions(playerCount);
            CurrentState = GameState.Setup;
            _engine.SetupGame(defs, AllCards);

            // ساخت پنل بازیکن‌ها
            UI.BuildPlayerPanels(_gameData.Players);

            _gameData.CurrentPlayerIndex = Random.Range(0, _gameData.Players.Count);
            UI.UpdateStatus($"بازی شروع شد! اولین بازیکن: {_gameData.Players[_gameData.CurrentPlayerIndex].Definition.PlayerName}");

            StartTurn();
        }

        private List<PlayerDefinition> BuildPlayerDefinitions(int count)
        {
            var result = new List<PlayerDefinition>();
            for (int i = 0; i < count; i++)
            {
                if (i < PlayerDefinitions.Count)
                {
                    result.Add(PlayerDefinitions[i]);
                }
                else
                {
                    var def = ScriptableObject.CreateInstance<PlayerDefinition>();
                    def.PlayerName = i == 0 ? "بازیکن" : $"هوش مصنوعی {i}";
                    def.IsHuman = i == 0;
                    def.AvatarIndex = i;
                    result.Add(def);
                }
            }
            return result;
        }

        // ============================================================
        // Helpers
        // ============================================================
        public void OnPlayerSelectedBankCard(CardDefinition card)
        {
            if (card == null) return;

            PlayerData humanP = GetHumanPlayer();
            if (humanP == null || !humanP.Hand.Contains(card)) return;

            // 1. بررسی حالت مرگ (فرشته)
            if (_isDying)
            {
                if (card.CardType == CardType.Angel)
                {
                    _angelClicked = true;
                    UI.UpdateStatus("فرشته انتخاب شد! در حال نجات...");
                }
                return;
            }

            bool isHumanTurn = (CurrentState == GameState.PlayerTurn);

            // 2. مدیریت ترکیب‌ها (Combo)
            if (_pendingComboList.Count > 0)
            {
                // اگر در لیست ترکیب فعلی Nope وجود دارد، فقط می‌توان Nope بازی کرد
                if (HasNopeInCombo && card.CardType != CardType.Nope)
                {
                    UI.UpdateStatus("شما فقط می‌توانید کارت Nope بازی کنید یا صبر کنید!");
                    return;
                }
                // اگر Nope در لیست نیست، می‌توان کارت هم‌نوع یا Nope بازی کرد
                else if (!HasNopeInCombo && card.CardType != _pendingComboList[0].CardType && card.CardType != CardType.Nope)
                {
                    UI.UpdateStatus($"شما فقط می‌توانید کارت {_pendingComboList[0].CardName} یا Nope را بازی کنید!");
                    return;
                }

                if (_pendingComboList.Count >= 3)
                {
                    UI.UpdateStatus("حداکثر می‌توانید 3 کارت را با هم ترکیب کنید!");
                    return;
                }
            }
            else
            {
                // *** این بخش اصلاح شد: شرط مربوط به Nope در خلأ حذف شد ***

                if (!isHumanTurn && !card.Immediate) return;
                if (!humanP.IsAlive && !card.Immediate) return;
            }

            // 3. اضافه کردن به ترکیب
            if (_pendingComboList.Count == 0) _activePlayer = humanP;

            _pendingComboList.Add(card);
            humanP.Hand.Remove(card);

            UI.ShowCardsOnBoard(_pendingComboList);
            RefreshHumanHandDisplay();

            // 4. شروع/ریست تایمر
            _isNopeWindowOpen = true;
            if (_playTimerCoroutine != null) StopCoroutine(_playTimerCoroutine);
            _playTimerCoroutine = StartCoroutine(PlayTimerRoutine(humanP, isHumanTurn));
        }
        public void HandleZombieDraw(PlayerData player, CardDefinition zombieCard)
        {
            UI.ShowZombiePlace(zombieCard);
            Debug.Log($"[Zombie] بازیکن {player.Definition.PlayerName} زامبی کشید.");

            // اگر هوش مصنوعی زامبی کشید
            if (!player.Definition.IsHuman)
            {
                StartCoroutine(AiZombieDecisionRoutine(player, zombieCard));
                return;
            }

            // اگر بازیکن انسانی زامبی کشید
            _angelClicked = false;
            _isDying = true;

            Debug.Log("[Zombie] _isDying = true. فرصت 5 ثانیه‌ای شروع شد.");

            // رفرش دست تا فرشته فعال شود
            RefreshHumanHandDisplay();

            CurrentState = GameState.WaitingForAction;

            StartCoroutine(ZombieDeathTimer(player, zombieCard));
        }
        private PlayerData GetHumanPlayer()
        {
            return _gameData.Players.FirstOrDefault(p => p.Definition.IsHuman);
        }

        /// <summary>
        /// همیشه دست بازیکن انسانی را در PlayerBank نمایش می‌دهد.
        /// در نوبت انسانی تعاملی، در غیر این‌صورت فقط قابل مشاهده.
        /// </summary>
        private void RefreshHumanHandDisplay()
        {
            PlayerData human = GetHumanPlayer();
            if (human != null) // شرط IsAlive را برداشتیم تا کارت‌های Immediate در حالت مرده هم دیده شوند
                UI.RenderHand(human);
            else
                UI.ClearPlayerBank();
        }

        private void UpdateDiscard()
        {
            if (_gameData.DiscardPile.Count > 0)
                UI.UpdateDiscardDisplay(_gameData.DiscardPile[_gameData.DiscardPile.Count - 1]);
            else
                UI.UpdateDiscardDisplay(null);
        }

        /// <summary>
        /// انتقال نوبت‌های اضافه‌ای که در اثر بازی یک کارت افزایش یافته‌اند به صف «بازیکن بعدی».
        /// </summary>
        private void ApplyExtrasDelta(int extrasBefore)
        {
            int delta = _gameData.ExtraTurns - extrasBefore;
            if (delta > 0)
            {
                _extraTurnsForNextPlayer += delta;
                _gameData.ExtraTurns = extrasBefore;
            }
        }

        // ============================================================
        // مدیریت نوبت
        // ============================================================

        public void StartTurn()
        {
            if (CurrentState == GameState.GameOver) return;

            // نمایش نشانگر نوبت برای بازیکن فعلی
            UI.UpdateTurnIndicator(_gameData.CurrentPlayerIndex);

            PlayerData currentPlayer = _gameData.Players[_gameData.CurrentPlayerIndex];
            if (!currentPlayer.IsAlive) { NextTurn(); return; }

            _gameData.SkipDraw = false;

            // محاسبه تعداد کل نوبت‌های باقی‌مانده (1 + نوبت‌های اضافه)
            int totalTurnsLeft = _gameData.ExtraTurns + 1;

            if (currentPlayer.Definition.IsHuman)
            {
                CurrentState = GameState.PlayerTurn;
                RefreshHumanHandDisplay();

                if (totalTurnsLeft > 1)
                {
                    UI.UpdateStatus($"نوبت {currentPlayer.Definition.PlayerName} است. شما {ToPersianNumber(totalTurnsLeft)} مرتبه باید بازی کنید.");
                }
                else
                {
                    UI.UpdateStatus($"نوبت {currentPlayer.Definition.PlayerName} است. کارت بازی کنید یا دک بکشید.");
                }
            }
            else
            {
                CurrentState = GameState.AiTurn;
                RefreshHumanHandDisplay(); // فقط نمایش دست بازیکن انسانی

                if (totalTurnsLeft > 1)
                {
                    UI.UpdateStatus($"نوبت {currentPlayer.Definition.PlayerName} (هوش مصنوعی). {ToPersianNumber(totalTurnsLeft)} نوبت باقی مانده.");
                }
                else
                {
                    UI.UpdateStatus($"نوبت {currentPlayer.Definition.PlayerName} (هوش مصنوعی).");
                }

                StartCoroutine(AiTurnCoroutine(currentPlayer));
            }
        }

        // *** متد کمکی برای تبدیل اعداد انگلیسی به فارسی ***
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

        //public void NextTurn()
        //{
        //    CheckWinCondition();
        //    if (CurrentState == GameState.GameOver) return;

        //    // بررسی نوبت‌های اضافه
        //    if (_gameData.ExtraTurns > 0)
        //    {
        //        _gameData.ExtraTurns--;
        //        UI.UpdateStatus($"نوبت اضافه! {_gameData.Players[_gameData.CurrentPlayerIndex].Definition.PlayerName} دوباره بازی می‌کند.");
        //        StartTurn();
        //        return;
        //    }

        //    // انتقال به بازیکن زنده بعدی
        //    int safety = 0;
        //    do
        //    {
        //        _gameData.CurrentPlayerIndex = (_gameData.CurrentPlayerIndex + 1) % _gameData.Players.Count;
        //        safety++;
        //        if (safety > _gameData.Players.Count + 1) { EndGame(); return; }
        //    } while (!_gameData.Players[_gameData.CurrentPlayerIndex].IsAlive);

        //    // انتقال نوبت‌های اضافه به بازیکن جدید
        //    if (_extraTurnsForNextPlayer > 0)
        //    {
        //        _gameData.ExtraTurns += _extraTurnsForNextPlayer;
        //        _extraTurnsForNextPlayer = 0;
        //    }

        //    StartTurn();
        //} 1405816 1
        public void NextTurn(bool forceAdvance = false)
        {
            CheckWinCondition();
            if (CurrentState == GameState.GameOver) return;

            // اگر forceAdvance نیست و بازیکن فعلی ExtraTurns دارد، یکی مصرف کن و دوباره بازی کن
            if (!forceAdvance && _gameData.ExtraTurns > 0)
            {
                _gameData.ExtraTurns--;
                StartTurn();
                return;
            }

            // اگر forceAdvance است (Attack/DeadsAttack)، نوبت‌های باقیمانده + رفاند جاری منتقل می‌شوند
            if (forceAdvance)
            {
                int refund = _playerStartedChainWithExtraTurns ? 1 : 0;
                _extraTurnsForNextPlayer += _gameData.ExtraTurns + refund;
                _gameData.ExtraTurns = 0;
            }

            // انتقال به بازیکن زنده بعدی
            int safety = 0;
            do
            {
                _gameData.CurrentPlayerIndex = (_gameData.CurrentPlayerIndex + 1) % _gameData.Players.Count;
                safety++;
                if (safety > _gameData.Players.Count + 1) { EndGame(); return; }
            } while (!_gameData.Players[_gameData.CurrentPlayerIndex].IsAlive);

            // انتقال نوبت‌های اضافه به بازیکن جدید
            if (_extraTurnsForNextPlayer > 0)
            {
                _gameData.ExtraTurns += _extraTurnsForNextPlayer;
                _extraTurnsForNextPlayer = 0;
            }

            _playerStartedChainWithExtraTurns = _gameData.ExtraTurns > 0;

            StartTurn();
        }
        // ============================================================
        // تعاملات بازیکن انسانی
        // ============================================================



        // این متد را برای مدیریت کارت‌های Immediate خارج از نوبت اضافه کنید
        private IEnumerator FinishImmediatePlayOutOfTurn(GameState previousState)
        {
            yield return new WaitForSeconds(1.2f);
            UI.ClearBoard();
            UpdateDiscard();

            // بازگرداندن وضعیت بازی به حالت قبل (مثلاً نوبت هوش مصنوعی)
            CurrentState = previousState;
            RefreshHumanHandDisplay();
        }

        public void OnEndTurnClicked()
        {
            if (CurrentState != GameState.PlayerTurn) return;
            if (_pendingComboList.Count > 0) return; // در حال ترکیب است

            EndTurn(true);
        }

        //private void EndTurn(bool shouldDraw)
        //{
        //    if (CurrentState == GameState.GameOver) return;
        //    UI.ClearBoard();

        //    PlayerData currentPlayer = _gameData.Players[_gameData.CurrentPlayerIndex];

        //    if (shouldDraw && !_gameData.SkipDraw)
        //    {
        //        CurrentState = GameState.WaitingForAction;

        //        // کشیدن کارت
        //        bool isZombie = _engine.DrawCardFromDeck(currentPlayer);

        //        // *** اینجا همیشه دست بازیکن انسانی را رندر می‌کنیم، نه بازیکن فعلی ***
        //        RefreshHumanHandDisplay();
        //        UI.UpdateDeckCount(_gameData.Deck.Count);
        //        UpdateDiscard();

        //        if (isZombie) return;
        //        if (_isDying || _waitingForInput) return;
        //        if (!currentPlayer.IsAlive)
        //        {
        //            NextTurn();
        //            return;
        //        }
        //    }

        //    NextTurn();
        //}
        private void EndTurn(bool shouldDraw)
        {
            if (CurrentState == GameState.GameOver) return;
            UI.ClearBoard();

            PlayerData currentPlayer = _gameData.Players[_gameData.CurrentPlayerIndex];

            if (shouldDraw && !_gameData.SkipDraw)
            {
                // قفل کردن ورودی‌ها در حین انیمیشن
                CurrentState = GameState.WaitingForAction;

                // شروع Coroutine کشیدن کارت با انیمیشن
                StartCoroutine(DrawAndEndTurnRoutine(currentPlayer));
            }
            else
            {
                NextTurn();
            }
        }

        //private IEnumerator DrawAndEndTurnRoutine(PlayerData player)
        //{
        //    Debug.Log($"[GameFlow] DrawAndEndTurnRoutine شروع شد. بازیکن: {player.Definition.PlayerName}");

        //    CardDefinition topCard = _gameData.Deck.Count > 0 ? _gameData.Deck[0] : null;

        //    if (topCard == null)
        //    {
        //        Debug.LogWarning("[GameFlow] کارت رویی دک NULL است!");
        //    }

        //    bool animationDone = false;

        //    if (player.Definition.IsHuman && topCard != null && UiAnimator != null)
        //    {
        //        Debug.Log("[GameFlow] فراخوانی UiAnimator.AnimateDrawCard");
        //        UiAnimator.AnimateDrawCard(topCard, () => {
        //            Debug.Log("[GameFlow] انیمیشن تمام شد (Callback)");
        //            animationDone = true;
        //        });

        //        float timer = 0f;
        //        while (!animationDone && timer < 3f)
        //        {
        //            timer += Time.deltaTime;
        //            yield return null;
        //        }

        //        if (!animationDone) Debug.LogWarning("[GameFlow] انیمیشن تایم‌اوت شد و کامل نشد!");
        //    }
        //    else
        //    {
        //        Debug.Log($"[GameFlow] انیمیشن اجرا نشد. IsHuman={player.Definition.IsHuman}, topCard={(topCard != null)}, UiAnimator={(UiAnimator != null)}");
        //        yield return new WaitForSeconds(0.2f);
        //    }

        //    Debug.Log("[GameFlow] کشیدن واقعی کارت");
        //    bool isZombie = _engine.DrawCardFromDeck(player);
        //    RefreshHumanHandDisplay();
        //    UI.UpdateDeckCount(_gameData.Deck.Count);
        //    UpdateDiscard();

        //    if (isZombie) yield break;
        //    if (_isDying || _waitingForInput) yield break;
        //    if (!player.IsAlive) { NextTurn(); yield break; }

        //    NextTurn();
        //}
        //14050814 3
        private IEnumerator DrawAndEndTurnRoutine(PlayerData player)
        {
            CardDefinition topCard = _gameData.Deck.Count > 0 ? _gameData.Deck[0] : null;
            bool animationDone = false;

            if (topCard != null && UiAnimator != null)
            {
                if (player.Definition.IsHuman)
                {
                    UiAnimator.AnimateDrawCard(topCard, () => { animationDone = true; });
                }
                else
                {
                    UiAnimator.AnimateDrawForAI(topCard, player, () => { animationDone = true; });
                }

                float timer = 0f;
                while (!animationDone && timer < 3f)
                {
                    timer += Time.deltaTime;
                    yield return null;
                }
            }
            else
            {
                yield return new WaitForSeconds(0.2f);
            }

            bool isZombie = _engine.DrawCardFromDeck(player);
            RefreshHumanHandDisplay();
            UI.UpdateDeckCount(_gameData.Deck.Count);
            UpdateDiscard();

            if (isZombie) yield break;
            if (_isDying || _waitingForInput) yield break;
            if (!player.IsAlive) { NextTurn(); yield break; }

            NextTurn();
        }

        private IEnumerator FinishImmediatePlay()
        {
            yield return new WaitForSeconds(1.2f);
            UI.ClearBoard();
            UpdateDiscard();

            if (_gameData.SkipDraw)
            {
                EndTurn(false);
            }
            else
            {
                CurrentState = GameState.PlayerTurn;
                RefreshHumanHandDisplay();
                UI.UpdateStatus($"نوبت {_gameData.Players[_gameData.CurrentPlayerIndex].Definition.PlayerName} است. کارت دیگری بازی کنید یا دک بکشید.");
            }
        }

        private void ResumeCurrentPlayerTurn()
        {
            if (CurrentState == GameState.GameOver) return;
            if (CurrentState == GameState.WaitingForAction) return; // پرامپت باز است

            PlayerData currentPlayer = _gameData.Players[_gameData.CurrentPlayerIndex];
            if (!currentPlayer.IsAlive) { NextTurn(); return; }

            UI.ClearBoard();
            UpdateDiscard();

            CurrentState = GameState.PlayerTurn;
            RefreshHumanHandDisplay();
            UI.UpdateStatus($"نوبت {currentPlayer.Definition.PlayerName} است.");
        }

        // ============================================================
        // هوش مصنوعی
        // ============================================================

        //private IEnumerator AiTurnCoroutine(PlayerData aiPlayer)
        //{
        //    float thinkTime = 3f / _speed;
        //    yield return new WaitForSeconds(thinkTime);

        //    _aiPlayer = new AiPlayer(aiPlayer, _engine, _hardness);
        //    CardDefinition cardToPlay = _aiPlayer.DecideCardToPlay();

        //    if (cardToPlay != null)
        //    {
        //        // نمایش کارت AI روی تخته
        //        UI.ShowCardOnBoard(cardToPlay);
        //        yield return new WaitForSeconds(1f);

        //        // اجرای افکت کارت
        //        int extrasBefore = _gameData.ExtraTurns;
        //        _engine.PlayCards(aiPlayer, new List<CardDefinition> { cardToPlay });
        //        ApplyExtrasDelta(extrasBefore);

        //        UI.RefreshPlayerPanels();
        //        UpdateDiscard();

        //        yield return new WaitForSeconds(1f);
        //        UI.ClearBoard();
        //    }

        //    // *** نکته مهم: اگر افکت کارت باعث باز شدن پنلی شد، متوقف شو ***
        //    if (_waitingForInput || _isDying) yield break;

        //    if (_gameData.SkipDraw)
        //    {
        //        EndTurn(false);
        //    }
        //    else
        //    {
        //        EndTurn(true);
        //    }
        //}
        //14050814 3
        //private IEnumerator AiTurnCoroutine(PlayerData aiPlayer)
        //{
        //    float thinkTime = 3f / _speed;
        //    yield return new WaitForSeconds(thinkTime);

        //    _aiPlayer = new AiPlayer(aiPlayer, _engine, _hardness);
        //    CardDefinition cardToPlay = _aiPlayer.DecideCardToPlay();

        //    if (cardToPlay != null)
        //    {
        //        // 1. نمایش کارت AI روی تخته
        //        UI.ShowCardOnBoard(cardToPlay);
        //        yield return new WaitForSeconds(1f);

        //        // 2. اجرای افکت کارت
        //        int extrasBefore = _gameData.ExtraTurns;
        //        _engine.PlayCards(aiPlayer, new List<CardDefinition> { cardToPlay });
        //        ApplyExtrasDelta(extrasBefore);

        //        UI.RefreshPlayerPanels();

        //        // *** نکته: UpdateDiscard اینجا صدا زده نمی‌شود ***
        //        // تا کارت جدید در دک سوخته ظاهر نشود

        //        yield return new WaitForSeconds(0.5f); // مکث کوتاه قبل از انیمیشن

        //        // 3. انیمیشن سوختن کارت AI به سمت دک سوخته
        //        bool animDone = false;
        //        if (UiAnimator != null)
        //        {
        //            UiAnimator.AnimateBurnCards(() => animDone = true);
        //            float timer = 0f;
        //            while (!animDone && timer < 3f)
        //            {
        //                timer += Time.deltaTime;
        //                yield return null;
        //            }
        //        }

        //        // 4. حالا که انیمیشن تمام شد، تخته را پاک کن و دک سوخته را آپدیت کن
        //        UI.ClearBoard();
        //        UpdateDiscard();
        //    }
        //    else
        //    {
        //        // اگر AI کارتی بازی نکرد، تخته را پاک کن (اگر چیزی روی آن مانده)
        //        UI.ClearBoard();
        //        UpdateDiscard();
        //    }

        //    // بررسی پنل‌های باز (مثل اوراکل)
        //    if (_waitingForInput || _isDying) yield break;

        //    if (_gameData.SkipDraw)
        //    {
        //        EndTurn(false);
        //    }
        //    else
        //    {
        //        EndTurn(true);
        //    }
        //}14050816 1
        private IEnumerator AiTurnCoroutine(PlayerData aiPlayer)
        {
            // 1. تاخیر اولیه برای فکر کردن هوش مصنوعی
            float thinkTime = 3f / _speed;
            yield return new WaitForSeconds(thinkTime);

            _aiPlayer = new AiPlayer(aiPlayer, _engine, _hardness);
            CardDefinition cardToPlay = _aiPlayer.DecideCardToPlay();

            if (cardToPlay != null)
            {
                // 2. نمایش کارت انتخاب شده روی تخته
                UI.ShowCardOnBoard(cardToPlay);
                yield return new WaitForSeconds(1f);

                // 3. اجرای افکت کارت در موتور بازی
                int extrasBefore = _gameData.ExtraTurns;
                _engine.PlayCards(aiPlayer, new List<CardDefinition> { cardToPlay });
                ApplyExtrasDelta(extrasBefore);

                UI.RefreshPlayerPanels();

                // 4. اگر کارت نیاز به تعامل دارد (مثل اوراکل) یا AI در حال مرگ است، منتظر بمان
                if (_waitingForInput || _isDying)
                {
                    yield break;
                }

                // 5. انیمیشن سوختن کارت AI به سمت دک سوخته
                bool animDone = false;
                if (UiAnimator != null)
                {
                    UiAnimator.AnimateBurnCards(() => animDone = true);
                    float timer = 0f;
                    while (!animDone && timer < 3f)
                    {
                        timer += Time.deltaTime;
                        yield return null;
                    }
                }

                // 6. پاکسازی تخته و آپدیت دک سوخته
                UI.ClearBoard();
                UpdateDiscard();

                // 7. بررسی اینکه آیا کارت باعث انتقال اجباری نوبت شده است
                bool forceAdvance = cardToPlay.CardType == CardType.Attack
                                    || cardToPlay.CardType == CardType.DeadsAttack
                                    || _extraTurnsForNextPlayer > 0;

                if (forceAdvance)
                {
                    // انتقال اجباری نوبت به بازیکن بعدی (با در نظر گرفتن نوبت‌های اضافه)
                    NextTurn(forceAdvance: true);
                    yield break;
                }
            }
            else
            {
                // AI کارتی بازی نکرد؛ تخته را پاک کن (اگر چیزی مانده)
                UI.ClearBoard();
                UpdateDiscard();
            }

            // 8. پایان نوبت AI: اگر SkipDraw فعال نیست، کارت می‌کشد و سپس نوبت منتقل می‌شود
            if (_gameData.SkipDraw)
            {
                EndTurn(false);
            }
            else
            {
                EndTurn(true);
            }
        }
        // ============================================================
        // پرامپت‌های تعاملی
        // ============================================================

        // ---------- دزدیدن کارت تصادفی (2 کارت همسان) ----------
        public void PromptRandomSteal()
        {
            CurrentState = GameState.WaitingForAction;
            _waitingForInput = true;
            UI.UpdateStatus("یک بازیکن را انتخاب کنید تا بطور شانسی یک کارت او را بردارید.");
        }

        public void ResolveRandomSteal(PlayerData target)
        {
            if (!_waitingForInput || target == null || target.Hand.Count == 0) return;
            _waitingForInput = false;

            PlayerData currentPlayer = _gameData.Players[_gameData.CurrentPlayerIndex];
            int idx = Random.Range(0, target.Hand.Count);
            CardDefinition stolen = target.Hand[idx];
            target.Hand.RemoveAt(idx);
            currentPlayer.Hand.Add(stolen);

            UI.UpdateStatus($"یک کارت از {target.Definition.PlayerName} گرفته شد.");
            UI.ClearBoard();      // پاک کردن کارتی که 3 ثانیه مونده بود
            UpdateDiscard();      // فرستادن کارت به دک سوخته
            ResumeCurrentPlayerTurn();
        }

        // ---------- دزدیدن کارت مشخص (3 کارت همسان) ----------
        public void PromptSpecificSteal()
        {
            CurrentState = GameState.WaitingForAction;
            _waitingForInput = true;
            UI.UpdateStatus("یک بازیکن را انتخاب کنید و کارت مشخصی از او بخواهید.");
        }

        public void ResolveSpecificSteal(PlayerData target, CardDefinition requestedCard)
        {
            if (!_waitingForInput || target == null || requestedCard == null) return;
            _waitingForInput = false;

            PlayerData currentPlayer = _gameData.Players[_gameData.CurrentPlayerIndex];
            CardDefinition cardFound = target.Hand.FirstOrDefault(c => c.CardType == requestedCard.CardType);

            if (cardFound != null)
            {
                target.Hand.Remove(cardFound);
                currentPlayer.Hand.Add(cardFound);
                UI.UpdateStatus($"کارت {requestedCard.CardName} از {target.Definition.PlayerName} گرفته شد.");
            }
            else
            {
                UI.UpdateStatus($"بازیکن {target.Definition.PlayerName} آن کارت را نداشت.");
            }
            UI.ClearBoard();      // پاک کردن کارتی که 3 ثانیه مونده بود
            UpdateDiscard();      // فرستادن کارت به دک سوخته
            ResumeCurrentPlayerTurn();
        }

        // ---------- فرشته (نجات از زامبی) ----------
        public void PromptUseAngel(PlayerData player)
        {
            if (player.Definition.IsHuman)
            {
                CurrentState = GameState.WaitingForAction;
                _waitingForInput = true;
                UI.UpdateStatus($"{player.Definition.PlayerName} زامبی کشید! آیا از فرشته استفاده می‌کند؟");
            }
            else
            {
                ResolveAngelUse(true);
            }
        }

        public void ResolveAngelUse(bool useAngel)
        {
            if (!_waitingForInput && _gameData.Players[_gameData.CurrentPlayerIndex].Definition.IsHuman) return;
            _waitingForInput = false;

            PlayerData player = _gameData.Players[_gameData.CurrentPlayerIndex];

            if (useAngel)
            {
                var angel = player.Hand.FirstOrDefault(c => c.CardType == CardType.Angel);
                if (angel != null)
                {
                    player.Hand.Remove(angel);
                    _gameData.DiscardPile.Add(angel);
                    player.HasAngel = false;
                    UI.UpdateStatus($"{player.Definition.PlayerName} با فرشته نجات یافت!");
                }
            }
            else
            {
                player.IsAlive = false;
                UI.UpdateStatus($"{player.Definition.PlayerName} مرد!");
            }

            RefreshHumanHandDisplay();
            UI.RefreshPlayerPanels();
            UpdateDiscard();

            // اگر انسان بود، نوبتش را ببند؛ اگر AI بود، EndTurn بیرونی خودش ادامه می‌دهد
            if (CurrentState == GameState.WaitingForAction)
            {
                EndTurn(false);
            }
            UI.ClearBoard();      // پاک کردن کارتی که 3 ثانیه مونده بود
            UpdateDiscard();      // فرستادن کارت به دک سوخته
        }


        private System.Collections.IEnumerator AiZombieDecisionRoutine(PlayerData aiPlayer, CardDefinition zombieCard)
        {
            yield return new UnityEngine.WaitForSeconds(2f);

            _aiPlayer = new AiPlayer(aiPlayer, _engine, _hardness);
            bool useAngel = _aiPlayer.DecideUseAngel();

            UI.ClearZombiePlace();

            if (useAngel)
            {
                // 1. زامبی دوباره به دک برمی گردد
                _gameData.Deck.Add(zombieCard);
                _engine.ShuffleDeck();
                // *** ذخیره جایگاه کارت زامبی در دک ***
                _revealedZombieIndex = _gameData.Deck.IndexOf(zombieCard);

                // 2. فرشته کلا خارج می شود
                var angel = aiPlayer.Hand.FirstOrDefault(c => c.CardType == CardType.Angel);
                if (angel != null) aiPlayer.Hand.Remove(angel);
                aiPlayer.HasAngel = false;

                UI.UpdateStatus($"{aiPlayer.Definition.PlayerName} با فرشته نجات یافت!");

                // 3. زنده کردن یک بازیکن مرده
                var deadPlayers = _gameData.Players.Where(p => !p.IsAlive).ToList();
                if (deadPlayers.Count > 0)
                {
                    var target = deadPlayers[UnityEngine.Random.Range(0, deadPlayers.Count)];
                    target.IsAlive = true;
                    UI.UpdateStatus($"... و {target.Definition.PlayerName} را زنده کرد!");
                }

                // 4. ایجاد تعادل زامبی‌ها
                BalanceZombiesInDeck();
                ResetAllTurns();
                UI.RefreshPlayerPanels();
                NextTurn();
            }
            else
            {
                // هوش مصنوعی مرد
                aiPlayer.IsAlive = false;
                UI.RefreshPlayerPanels();
                UI.UpdateStatus($"{aiPlayer.Definition.PlayerName} مرد!");

                // زامبی به گورستان می رود
                _gameData.Graveyard.Add(zombieCard);

                CheckWinCondition();
                if (CurrentState != GameState.GameOver) NextTurn();
            }
        }
        private IEnumerator ZombieDeathTimer(PlayerData player, CardDefinition zombieCard)
        {
            float timer = 5f;

            while (timer > 0f)
            {
                if (_angelClicked) break;
                UI.UpdateStatus($"زامبی کشیدید! {timer:F1} ثانیه فرصت دارید تا با کلیک روی فرشته نجات یابید.");
                yield return new WaitForSeconds(0.1f);
                timer -= 0.1f;
            }

            _isDying = false;
            UI.ClearZombiePlace();

            if (_angelClicked)
            {
                // 1. زامبی دوباره به دک برمی گردد و دک شافل می شود
                _gameData.Deck.Add(zombieCard);
                _engine.ShuffleDeck();
                // *** ذخیره جایگاه کارت زامبی در دک ***
                _revealedZombieIndex = _gameData.Deck.IndexOf(zombieCard);

                UI.UpdateDeckCount(_gameData.Deck.Count);


                // 2. کارت فرشته کلا از بازی خارج می شود (به DiscardPile اضافه نمی شود)
                var angel = player.Hand.FirstOrDefault(c => c.CardType == CardType.Angel);
                if (angel != null)
                {
                    player.Hand.Remove(angel);
                    // _gameData.DiscardPile.Add(angel); // این خط حذف شد تا فرشته از بازی خارج شود
                }

                UI.UpdateStatus($"{player.Definition.PlayerName} با فرشته نجات یافت! زامبی به دک برگشت.");
                RefreshHumanHandDisplay();

                // بررسی مرده‌ها برای زنده کردن
                var deadPlayers = _gameData.Players.Where(p => !p.IsAlive).ToList();
                if (deadPlayers.Count > 0)
                {
                    UI.ShowSelectDeadPlayerPopup(deadPlayers);
                }
                else
                {
                    UI.UpdateStatus("هیچ بازیکن مرده‌ای برای زنده کردن وجود ندارد.");
                    CurrentState = GameState.PlayerTurn;
                    NextTurn();
                }
            }
            else
            {
                // بازیکن مرد
                player.IsAlive = false;
                ResetAllTurns();
                UI.RefreshPlayerPanels();
                UI.UpdateStatus($"{player.Definition.PlayerName} مرد!");

                // زامبی به گورستان می رود (نه دک سوخته)
                _gameData.Graveyard.Add(zombieCard);

                RefreshHumanHandDisplay();
                CheckWinCondition();
                if (CurrentState != GameState.GameOver) NextTurn();
            }
        }
        public void ResolveSelectDeadPlayer(PlayerData target)
        {
            UI.HideSelectDeadPlayerPopup();
            if (target != null && !target.IsAlive)
            {
                target.IsAlive = true;
                UI.RefreshPlayerPanels();
                UI.UpdateStatus($"{target.Definition.PlayerName} زنده شد!");
            }

            // ایجاد تعادل: چون یک نفر زنده شد، باید یک زامبی به دک برگردد
            BalanceZombiesInDeck();

            CurrentState = GameState.PlayerTurn;
            NextTurn();
        }

        private void BalanceZombiesInDeck()
        {
            int aliveCount = _gameData.Players.Count(p => p.IsAlive);
            int targetZombies = Mathf.Max(0, aliveCount - 1); // باید یکی کمتر از زنده‌ها باشد

            int currentZombies = _gameData.Deck.Count(c => c.CardType == CardType.Zombie);

            // اگر زامبی‌های داخل دک کمتر از حد نیاز بود، از گورستان به دک اضافه کن
            while (currentZombies < targetZombies && _gameData.Graveyard.Count > 0)
            {
                CardDefinition zombie = _gameData.Graveyard[0];
                _gameData.Graveyard.RemoveAt(0);
                _gameData.Deck.Add(zombie);
                currentZombies++;
                Debug.Log($"[Balance] یک زامبی از گورستان به دک برگشت. تعداد زامبی‌های دک: {currentZombies}");
            }

            _engine.ShuffleDeck();
            UI.UpdateDeckCount(_gameData.Deck.Count);
        }

        // ---------- غیب بین ----------
        public void PromptEyeEffect()
        {
            CurrentState = GameState.WaitingForAction;

            if (_revealedZombieIndex != -1)
            {
                // نمایش جایگاه زامبی به بازیکن (ایندکس از 0 شروع می‌شود، پس +1 می‌کنیم)
                UI.UpdateStatus($"غیب بین: کارت زامبیِ نجات‌یافته در جایگاه {_revealedZombieIndex + 1} از بالای دک قرار دارد.");

                // افکت مصرف شده و ریست می‌شود
                _revealedZombieIndex = -1;
            }

            // چون افکت فوری است، تخته پاک شده و نوبت ادامه می‌یابد
            UI.ClearBoard();
            UpdateDiscard();
            CurrentState = GameState.PlayerTurn;
            ResumeCurrentPlayerTurn();
        }

        public void ResolveEyeEffect(bool takeCard)
        {
            if (!_waitingForInput) return;
            _waitingForInput = false;

            PlayerData player = _gameData.Players[_gameData.CurrentPlayerIndex];
            if (takeCard)
            {
                _engine.DrawCardFromDeck(player);
                UpdateDiscard();
            }
            else
            {
                UI.UpdateStatus("کارت رویی دک برداشته نشد.");
            }
            UI.ClearBoard();      // پاک کردن کارتی که 3 ثانیه مونده بود
            UpdateDiscard();      // فرستادن کارت به دک سوخته
            ResumeCurrentPlayerTurn();
        }

        // ---------- پیش گویی ----------
        // ---------- پیش گویی (Oracle) ----------
        public void PromptOracleEffect(List<CardDefinition> top3)
        {
            CurrentState = GameState.WaitingForAction;
            _waitingForInput = true;

            UI.UpdateStatus("پیش گویی: 3 کارت رویی دک را مشاهده می‌کنید.");

            // باز کردن پنل و نمایش 3 کارت
            UI.ShowOraclePopup(top3);
        }

        public void ResolveOracleEffect()
        {
            if (!_waitingForInput) return;
            _waitingForInput = false;

            // بستن پنل
            UI.HideOraclePopup();

            // چون کارت پیش گویی در GameEngine بلافاصله به DiscardPile اضافه شده بود،
            // حالا که پنل بسته شد، تخته را پاک کرده و کارت را رو به دک سوخته منتقل می‌کنیم
            UI.ClearBoard();
            UpdateDiscard();

            // این خط بسیار مهم است!(وضعیت باید به نوبت بازیکن برگردد)
            CurrentState = GameState.PlayerTurn;

            UI.UpdateStatus("پیش گویی تمام شد.");

            // بازگشت به نوبت بازیکن برای ادامه بازی
            ResumeCurrentPlayerTurn();
        }

        // ---------- هدیه ----------
        public void PromptGiftSelection()
        {
            CurrentState = GameState.WaitingForAction;
            _waitingForInput = true;
            UI.UpdateStatus("هدیه: یک بازیکن زنده را انتخاب کنید تا یک کارت از او بگیرید.");

            PlayerData currentPlayer = _gameData.Players[_gameData.CurrentPlayerIndex];

            // پیدا کردن بازیکنان زنده به جز خودمان
            var aliveTargets = _gameData.Players.Where(p => p.IsAlive && p != currentPlayer).ToList();

            // نمایش پاپ‌آپ با لیست بازیکنان زنده
            UI.ShowGiftTargetPopup(aliveTargets);
        }


        public void ResolveGiftSelection(PlayerData target)
        {
            // بررسی ایمنی
            if (!_waitingForInput || target == null) return;
            _waitingForInput = false;

            // 1. بستن فوری پنل
            UI.HideGiftTargetPopup();
            Debug.Log("[Gift] پنل بسته شد.");

            PlayerData currentPlayer = _gameData.Players[_gameData.CurrentPlayerIndex];

            // 2. جابه‌جایی کارت‌ها
            if (target.Hand.Count > 0)
            {
                int idx = UnityEngine.Random.Range(0, target.Hand.Count);
                CardDefinition stolenCard = target.Hand[idx];

                target.Hand.RemoveAt(idx);
                currentPlayer.Hand.Add(stolenCard);

                UI.UpdateStatus($"کارت {stolenCard.CardName} از {target.Definition.PlayerName} گرفته شد.");
            }
            else
            {
                UI.UpdateStatus($"بازیکن {target.Definition.PlayerName} کارتی نداشت.");
            }

            // 3. آپدیت UI بازی
            RefreshHumanHandDisplay();
            UI.RefreshPlayerPanels();

            // 4. پاک کردن تخته (کارت هدیه را به دک سوخته می‌فرستد)
            UI.ClearBoard();
            UpdateDiscard();

            // 5. برگرداندن نوبت
            CurrentState = GameState.PlayerTurn;
            ResumeCurrentPlayerTurn();
        }

        // ---------- تونل زن ----------
        public void PromptTunnelEffect(CardDefinition topCard)
        {
            CurrentState = GameState.WaitingForAction;
            _waitingForInput = true;

            CardDefinition secondCard = _gameData.Deck.Count > 1 ? _gameData.Deck[1] : null;

            // ذخیره موقت کارت‌ها برای استفاده در زمان انتخاب
            _tempTopCard = topCard;
            _tempSecondCard = secondCard;

            // خارج کردن کارت‌ها از دک
            _gameData.Deck.RemoveAt(0);
            if (secondCard != null) _gameData.Deck.RemoveAt(0);

            UI.UpdateDeckCount(_gameData.Deck.Count);

            // نمایش پنل تونل
            UI.ShowTunnelPopup(topCard, secondCard);
        }

        public void ResolveTunnelSelection(bool takeTopCard)
        {
            if (!_waitingForInput) return;
            _waitingForInput = false;

            UI.HideTunnelPopup();

            // اجرای منطق در یک Coroutine تا از تداخل با کلیک UI جلوگیری شود
            StartCoroutine(ResolveTunnelSelectionRoutine(takeTopCard));
        }

        private IEnumerator ResolveTunnelSelectionRoutine(bool takeTopCard)
        {
            Debug.Log($"[Tunnel] شروع حل و فصل. takeTopCard={takeTopCard}");

            PlayerData player = _gameData.Players[_gameData.CurrentPlayerIndex];

            CardDefinition selectedCard = takeTopCard ? _tempTopCard : _tempSecondCard;
            CardDefinition unselectedCard = takeTopCard ? _tempSecondCard : _tempTopCard;

            // اگر کارت انتخاب شده زامبی بود
            if (selectedCard != null && selectedCard.CardType == CardType.Zombie)
            {
                UI.UpdateStatus("شما زامبی کشیدید!");
                if (unselectedCard != null) _gameData.Deck.Insert(0, unselectedCard);

                HandleZombieDraw(player, selectedCard);
                _tempTopCard = null;
                _tempSecondCard = null;
                yield break;
            }

            // اگر کارت معمولی بود، به دست بازیکن اضافه می‌شود
            if (selectedCard != null)
            {
                player.Hand.Add(selectedCard);
                UI.UpdateStatus($"کارت {selectedCard.CardName} به دست شما اضافه شد.");
            }

            // کارت دیگر به بالای دک برمی‌گردد
            if (unselectedCard != null) _gameData.Deck.Insert(0, unselectedCard);

            // پاکسازی مقادیر موقت
            _tempTopCard = null;
            _tempSecondCard = null;

            // آپدیت UI
            RefreshHumanHandDisplay();
            UpdateDiscard();
            UI.ClearBoard();

            // *** نکته کلیدی: انتقال هرگونه نوبت اضافه باقی‌مانده به بازیکن بعدی ***
            if (_gameData.ExtraTurns > 0)
            {
                _extraTurnsForNextPlayer += _gameData.ExtraTurns;
                _gameData.ExtraTurns = 0;
            }

            // اطمینان از اینکه وضعیت به نوبت بازیکن برگشته
            CurrentState = GameState.PlayerTurn;

            // صبر یک فریم برای اینکه UI آپدیت شود
            yield return null;

            Debug.Log("[Tunnel] فراخوانی NextTurn");
            NextTurn();
        }

        // ---------- خیرات ----------
        // ---------- خیرات (FeedDead) ----------
        public void PromptFeedDeadSelection()
        {
            CurrentState = GameState.WaitingForAction;
            _waitingForInput = true;

            // پیدا کردن بازیکنان مرده
            var deadPlayers = _gameData.Players.Where(p => !p.IsAlive).ToList();

            if (deadPlayers.Count == 0)
            {
                UI.UpdateStatus("خیرات: هیچ بازیکن مرده‌ای برای انتخاب وجود ندارد.");
                // اگر مرده‌ای نبود، کارت باطل می‌شود و بازی ادامه پیدا می‌کند
                UI.ClearBoard();
                UpdateDiscard();
                CurrentState = GameState.PlayerTurn;
                ResumeCurrentPlayerTurn();
                return;
            }

            UI.UpdateStatus("خیرات: یک بازیکن مرده را انتخاب کنید تا بقیه به او کارت بدهند.");

            // نمایش پنل انتخاب
            UI.ShowFeedDeadPopup(deadPlayers);
        }

        //public void ResolveFeedDeadSelection(PlayerData deadTarget)
        //{
        //    if (!_waitingForInput || deadTarget == null) return;
        //    _waitingForInput = false;

        //    // بستن پنل
        //    UI.HideFeedDeadPopup();

        //    int cardsGiven = 0;

        //    // همه بازیکنان زنده (به جز خود بازیکن مرده) یک کارت به او می‌دهند
        //    foreach (var player in _gameData.Players)
        //    {
        //        if (player == deadTarget) continue; // به خود مرده کارت نمی‌دهیم
        //        if (!player.IsAlive) continue;      // از بازیکنان مرده کارت کم نمی‌شود
        //        if (player.Hand.Count == 0) continue; // اگر کارتی ندارد، رد می‌شود

        //        // انتخاب یک کارت تصادفی از بازیکن زنده
        //        int randomIdx = Random.Range(0, player.Hand.Count);
        //        CardDefinition givenCard = player.Hand[randomIdx];

        //        // انتقال کارت از بازیکن زنده به بازیکن مرده
        //        player.Hand.RemoveAt(randomIdx);
        //        deadTarget.Hand.Add(givenCard);
        //        cardsGiven++;
        //    }

        //    UI.UpdateStatus($"تعداد {cardsGiven} کارت از بازیکنان زنده به {deadTarget.Definition.PlayerName} داده شد.");

        //    // آپدیت UI (چون ممکن است کارت‌های بازیکن انسانی کم شده باشد)
        //    RefreshHumanHandDisplay();
        //    UI.RefreshPlayerPanels();

        //    // پاک کردن تخته و فرستادن کارت خیرات به دک سوخته
        //    UI.ClearBoard();
        //    UpdateDiscard();

        //    // پایان نوبت (چون کارت خیرات بازی شده است)
        //    CurrentState = GameState.PlayerTurn;
        //    EndTurn(false);
        //}
        //14050814 4
        public void ResolveFeedDeadSelection(PlayerData deadTarget)
        {
            if (!_waitingForInput || deadTarget == null) return;
            _waitingForInput = false;

            UI.HideFeedDeadPopup();
            StartCoroutine(FeedDeadRoutine(deadTarget));
        }

        private IEnumerator FeedDeadRoutine(PlayerData deadTarget)
        {
            Debug.Log("[FeedDead] شروع روتین.");

            // 1. توزیع کارت‌ها از بازیکنان زنده به بازیکن مرده
            int cardsGiven = 0;
            foreach (var player in _gameData.Players)
            {
                if (player == deadTarget) continue;
                if (!player.IsAlive) continue;
                if (player.Hand.Count == 0) continue;

                int randomIdx = Random.Range(0, player.Hand.Count);
                CardDefinition givenCard = player.Hand[randomIdx];
                player.Hand.RemoveAt(randomIdx);
                deadTarget.Hand.Add(givenCard);
                cardsGiven++;
            }

            UI.UpdateStatus($"تعداد {cardsGiven} کارت از بازیکنان زنده به {deadTarget.Definition.PlayerName} داده شد.");

            // 2. آپدیت UI
            RefreshHumanHandDisplay();
            UI.RefreshPlayerPanels();

            // 3. انیمیشن سوختن کارت خیرات به سمت دک سوخته
            bool animDone = false;
            if (UiAnimator != null)
            {
                UiAnimator.AnimateBurnCards(() => animDone = true);
                float timer = 0f;
                while (!animDone && timer < 3f) { timer += Time.deltaTime; yield return null; }
            }

            // 4. پاکسازی تخته و آپدیت دک سوخته
            UI.ClearBoard();
            UpdateDiscard();

            // 5. انتقال هرگونه نوبت اضافه باقی‌مانده به بازیکن بعدی
            if (_gameData.ExtraTurns > 0)
            {
                _extraTurnsForNextPlayer += _gameData.ExtraTurns;
                _gameData.ExtraTurns = 0;
            }

            // 6. برگرداندن وضعیت به حالت عادی و انتقال نوبت
            CurrentState = GameState.PlayerTurn;
            yield return null; // صبر یک فریم برای اطمینان از آپدیت UI

            Debug.Log("[FeedDead] فراخوانی NextTurn");
            NextTurn();
        }
        // ============================================================
        // پایان بازی
        // ============================================================

        public void CheckWinCondition()
        {
            int aliveCount = _gameData.Players.Count(p => p.IsAlive);

            if (_initialPlayerCount == 1)
            {
                if (aliveCount == 0) EndGame();
            }
            else
            {
                if (aliveCount <= 1) EndGame();
            }
        }

        public void EndGame()
        {
            CurrentState = GameState.GameOver;

            // 1. پاک کردن فایل ذخیره‌سازی
            string path = Path.Combine(Application.persistentDataPath, "savegame.json");
            if (File.Exists(path))
            {
                File.Delete(path);
                Debug.Log("فایل ذخیره‌سازی پس از پایان بازی پاک شد.");
            }

            PlayerPrefs.DeleteKey("SavedGame");
            PlayerPrefs.Save();

            UI.UpdateTurnIndicator(-1);
            UI.ClearBoard();

            PlayerData winner = _gameData.Players.FirstOrDefault(p => p.IsAlive);

            if (winner != null)
                UI.UpdateStatus($"بازی تمام شد! برنده: {winner.Definition.PlayerName}");
            else
                UI.UpdateStatus("بازی تمام شد! همه مردند.");

            // 2. آپدیت وضعیت دکمه Continue
            UI.UpdateContinueButtonState();

            UI.ShowEndGamePopup(winner);
        }
        
        private void PlayNope(PlayerData player, CardDefinition nopeCard)
        {
            player.Hand.Remove(nopeCard);
            _gameData.DiscardPile.Add(nopeCard);
            _nopeCount++;

            // *** ذخیره کسی که این Nope را بازی کرده ***
            _lastNopePlayer = player;

            UI.UpdateStatus($"{player.Definition.PlayerName} کارت باطل (Nope) بازی کرد! (تعداد: {_nopeCount})");
            UI.ShowCardsOnBoard(_pendingComboList);
            RefreshHumanHandDisplay();
            UpdateDiscard();

            if (_playTimerCoroutine != null) StopCoroutine(_playTimerCoroutine);
            _playTimerCoroutine = StartCoroutine(PlayTimerRoutine(_activePlayer, CurrentState == GameState.PlayerTurn));
        }

        //private IEnumerator PlayTimerRoutine(PlayerData player, bool isHumanTurn)
        //{
        //    // اگر بازیکن انسانی Nope زد، به هوش مصنوعی 1.5 ثانیه فرصت بده تا جواب بدهد
        //    if (player.Definition.IsHuman && _nopeCount > 0)
        //    {
        //        yield return new WaitForSeconds(1f); // صبر کوتاه

        //        // پیدا کردن یک هوش مصنوعی که Nope دارد
        //        var aiWithNope = _gameData.Players.FirstOrDefault(p => !p.Definition.IsHuman && p.IsAlive && p.Hand.Any(c => c.CardType == CardType.Nope));
        //        if (aiWithNope != null && Random.value < 0.7f) // 70% شانس پاسخ
        //        {
        //            var nopeCard = aiWithNope.Hand.First(c => c.CardType == CardType.Nope);
        //            PlayNope(aiWithNope, nopeCard);
        //            yield break; // PlayNope خودش تایمر را دوباره استارت می‌زند
        //        }
        //    }

        //    yield return new WaitForSeconds(3f); // تایمر اصلی 3 ثانیه

        //    _isNopeWindowOpen = false;

        //    // اگر تعداد Nope فرد باشد، افکت کارت‌ها باطل می‌شود
        //    if (_nopeCount % 2 != 0)
        //    {
        //        UI.UpdateStatus("قابلیت کارت با Nope باطل شد!");
        //        foreach (var c in _pendingComboList) _gameData.DiscardPile.Add(c);
        //        ResetPlaySequence();
        //        HandleTurnEndAfterPlay(player, isHumanTurn);
        //        yield break;
        //    }

        //    // اجرای قابلیت کارت‌ها
        //    if (_pendingComboList.Count == 1)
        //    {
        //        CardDefinition card = _pendingComboList[0];
        //        if (CanCardEffectExecute(card))
        //        {
        //            UI.UpdateStatus($"اجرای {card.CardName}");
        //            _gameData.DiscardPile.Add(card);

        //            int extrasBefore = _gameData.ExtraTurns;
        //            _engine.ResolveCardEffect(player, card); // اجرای مستقیم افکت
        //            ApplyExtrasDelta(extrasBefore);
        //        }
        //        else
        //        {
        //            // اگر قابلیت تکی نداشت (مثل حیوانات یا شرایط نامناسب)
        //            UI.UpdateStatus($"قابلیت {card.CardName} به تنهایی قابل اجرا نیست و به بانک بازگشت.");
        //            player.Hand.Add(card); // بازگشت به دست
        //        }
        //    }
        //    else if (_pendingComboList.Count == 2)
        //    {
        //        UI.UpdateStatus("ترکیب دو کارتی فعال شد!");

        //        if (isHumanTurn)
        //        {
        //            // اگر بازیکن انسانی است، پنل را نشان بده
        //            var aliveTargets = _gameData.Players.Where(p => p.IsAlive && p != player).ToList();

        //            if (aliveTargets.Count == 0)
        //            {
        //                UI.UpdateStatus("هیچ بازیکن زنده‌ای برای گرفتن کارت وجود ندارد.");
        //                foreach (var c in _pendingComboList) _gameData.DiscardPile.Add(c);
        //                ResetPlaySequence();
        //                HandleTurnEndAfterPlay(player, isHumanTurn);
        //                yield break;
        //            }

        //            CurrentState = GameState.WaitingForAction;
        //            _waitingForInput = true;

        //            // باز کردن پنل انتخاب
        //            UI.ShowPlayerSelectionForCombo(aliveTargets, (selectedTarget) =>
        //            {
        //                ResolveTwoCardCombo(selectedTarget);
        //            });

        //            yield break; // روتین متوقف می‌شود تا کاربر انتخاب کند
        //        }
        //        else
        //        {
        //            // اگر هوش مصنوعی است، خودکار از یک بازیکن تصادفی کارت می‌دزدد
        //            var aliveTargets = _gameData.Players.Where(p => p.IsAlive && p != player && p.Hand.Count > 0).ToList();
        //            if (aliveTargets.Count > 0)
        //            {
        //                var target = aliveTargets[Random.Range(0, aliveTargets.Count)];
        //                int randomIdx = Random.Range(0, target.Hand.Count);
        //                CardDefinition stolenCard = target.Hand[randomIdx];

        //                target.Hand.RemoveAt(randomIdx);
        //                player.Hand.Add(stolenCard);

        //                UI.UpdateStatus($"{player.Definition.PlayerName} یک کارت از {target.Definition.PlayerName} دزدید!");
        //            }
        //            else
        //            {
        //                UI.UpdateStatus("هیچ بازیکنی کارت نداشت.");
        //            }

        //            foreach (var c in _pendingComboList) _gameData.DiscardPile.Add(c);
        //            ResetPlaySequence();
        //            HandleTurnEndAfterPlay(player, isHumanTurn);
        //        }
        //    }
        //    else if (_pendingComboList.Count == 3)
        //    {
        //        UI.UpdateStatus("ترکیب سه کارتی فعال شد!");

        //        if (isHumanTurn)
        //        {
        //            var aliveTargets = _gameData.Players.Where(p => p.IsAlive && p != player).ToList();

        //            if (aliveTargets.Count == 0)
        //            {
        //                UI.UpdateStatus("هیچ بازیکن زنده‌ای برای گرفتن کارت وجود ندارد.");
        //                foreach (var c in _pendingComboList) _gameData.DiscardPile.Add(c);
        //                ResetPlaySequence();
        //                HandleTurnEndAfterPlay(player, isHumanTurn);
        //                yield break;
        //            }

        //            CurrentState = GameState.WaitingForAction;
        //            _waitingForInput = true;

        //            // مرحله 1: انتخاب بازیکن
        //            UI.ShowThreeCardComboTargetSelection(aliveTargets, (selectedTarget) =>
        //            {
        //                // *** استفاده از لیست کارت‌های یکتا ***
        //                UI.ShowThreeCardComboCardSelection(AllUniqueCards, (selectedCard) =>
        //                {
        //                    ResolveThreeCardCombo(selectedTarget, selectedCard, player);
        //                });
        //            });

        //            yield break;
        //        }
        //        else
        //        {
        //            // هوش مصنوعی: انتخاب تصادفی بازیکن و کارت از لیست یکتا
        //            var aliveTargets = _gameData.Players.Where(p => p.IsAlive && p != player).ToList();
        //            if (aliveTargets.Count > 0)
        //            {
        //                var target = aliveTargets[Random.Range(0, aliveTargets.Count)];
        //                var requestedCard = AllUniqueCards[Random.Range(0, AllUniqueCards.Count)];

        //                ResolveThreeCardCombo(target, requestedCard, player);
        //            }
        //            else
        //            {
        //                foreach (var c in _pendingComboList) _gameData.DiscardPile.Add(c);
        //                ResetPlaySequence();
        //                HandleTurnEndAfterPlay(player, isHumanTurn);
        //            }
        //        }
        //    }

        //    ResetPlaySequence();
        //    HandleTurnEndAfterPlay(player, isHumanTurn);
        //}
        private IEnumerator PlayTimerRoutine(PlayerData player, bool isHumanTurn)
        {
            // 1. صبر 3 ثانیه‌ای
            yield return new WaitForSeconds(3f);

            _isNopeWindowOpen = false;

            int nopeCount = _pendingComboList.Count(c => c.CardType == CardType.Nope);
            int totalCards = _pendingComboList.Count;

            // اگر تعداد Nope ها فرد باشد و کارت غیر Nope هم وجود داشته باشد -> کارت غیر Nope باطل می‌شود
            if (nopeCount % 2 != 0 && totalCards > nopeCount)
            {
                UI.UpdateStatus("افکت کارت اصلی با Nope باطل شد.");
                foreach (var c in _pendingComboList) _gameData.DiscardPile.Add(c);
                ResetPlaySequence();
                HandleTurnEndAfterPlay(player, isHumanTurn);
                yield break;
            }

            // در غیر این صورت، تعداد کل کارت‌ها تعیین‌کننده نوع ترکیب است
            //if (totalCards == 1)
            //{
            //    // اجرای تک کارتی
            //    CardDefinition card = _pendingComboList[0];
            //    if (CanCardEffectExecute(card))
            //    {
            //        UI.UpdateStatus($"اجرای {card.CardName}");
            //        _gameData.DiscardPile.Add(card);

            //        int extrasBefore = _gameData.ExtraTurns;
            //        _engine.ResolveCardEffect(player, card);
            //        ApplyExtrasDelta(extrasBefore);

            //        ResetPlaySequence();
            //        if (_extraTurnsForNextPlayer > 0)
            //        {
            //            StartCoroutine(BurnAndNextTurnRoutine());
            //        }
            //        else
            //        {
            //            HandleTurnEndAfterPlay(player, isHumanTurn);
            //        }
            //    }
            //    else
            //    {
            //        // *** کارت به بانک برمی‌گردد و نوبت از دست نمی‌رود ***
            //        UI.UpdateStatus($"قابلیت {card.CardName} در این لحظه قابل اجرا نیست و به بانک بازگشت.");
            //        player.Hand.Add(card);
            //        ResetPlaySequence();

            //        UI.ClearBoard();
            //        UpdateDiscard();
            //        RefreshHumanHandDisplay();
            //        UI.RefreshPlayerPanels();

            //        if (isHumanTurn)
            //        {
            //            // بازیکن انسانی نوبت خود را ادامه می‌دهد (حتی اگر SkipDraw ست شده باشد)
            //            CurrentState = GameState.PlayerTurn;
            //            UI.UpdateStatus($"نوبت {player.Definition.PlayerName} است. کارت دیگری بازی کنید یا دک بکشید.");
            //        }
            //        else
            //        {
            //            // برای هوش مصنوعی، برای جلوگیری از حلقه بی‌پایان، نوبتش پایان می‌یابد
            //            EndTurn(true);
            //        }
            //    }
            //    ResetPlaySequence();
            //    HandleTurnEndAfterPlay(player, isHumanTurn);
            //}14050816 1
            if (totalCards == 1)
            {
                CardDefinition card = _pendingComboList[0];

                if (CanCardEffectExecute(card))
                {
                    UI.UpdateStatus($"اجرای {card.CardName}");
                    _gameData.DiscardPile.Add(card);

                    int extrasBefore = _gameData.ExtraTurns;
                    _engine.ResolveCardEffect(player, card);
                    ApplyExtrasDelta(extrasBefore);

                    ResetPlaySequence();

                    // اعمال forceAdvance برای Attack، DeadsAttack و هر کارتی که ExtraTurns ایجاد کرده
                    if (card.CardType == CardType.Attack ||
                        card.CardType == CardType.DeadsAttack ||
                        _extraTurnsForNextPlayer > 0)
                    {
                        StartCoroutine(BurnAndForceNextTurnRoutine());
                    }
                    else
                    {
                        HandleTurnEndAfterPlay(player, isHumanTurn);
                    }
                }
                else
                {
                    // کارت به بانک برمی‌گردد و نوبت از دست نمی‌رود
                    UI.UpdateStatus($"قابلیت {card.CardName} در این لحظه قابل اجرا نیست و به بانک بازگشت.");
                    player.Hand.Add(card);
                    ResetPlaySequence();

                    UI.ClearBoard();
                    UpdateDiscard();
                    RefreshHumanHandDisplay();
                    UI.RefreshPlayerPanels();

                    if (isHumanTurn)
                    {
                        CurrentState = GameState.PlayerTurn;
                        UI.UpdateStatus($"نوبت {player.Definition.PlayerName} است. کارت دیگری بازی کنید یا دک بکشید.");
                    }
                    else
                    {
                        EndTurn(true);
                    }
                }
            }
            else if (totalCards == 2)
            {
                // اجرای ترکیب دو کارتی (چه هر دو Nope باشند، چه دو کارت هم‌نوع)
                UI.UpdateStatus("ترکیب دو کارتی فعال شد!");

                if (isHumanTurn)
                {
                    var aliveTargets = _gameData.Players.Where(p => p.IsAlive && p != player).ToList();

                    if (aliveTargets.Count == 0)
                    {
                        UI.UpdateStatus("هیچ بازیکن زنده‌ای برای گرفتن کارت وجود ندارد.");
                        foreach (var c in _pendingComboList) _gameData.DiscardPile.Add(c);
                        ResetPlaySequence();
                        HandleTurnEndAfterPlay(player, isHumanTurn);
                        yield break;
                    }

                    CurrentState = GameState.WaitingForAction;
                    _waitingForInput = true;

                    // *** باز کردن پنل برای حالت 2 کارتی ***
                    UI.ShowPlayerSelectionForCombo(aliveTargets, (selectedTarget) =>
                    {
                        ResolveTwoCardCombo(selectedTarget);
                    });

                    yield break;
                }
                else
                {
                    // هوش مصنوعی
                    var aliveTargets = _gameData.Players.Where(p => p.IsAlive && p != player && p.Hand.Count > 0).ToList();
                    if (aliveTargets.Count > 0)
                    {
                        var target = aliveTargets[Random.Range(0, aliveTargets.Count)];
                        int randomIdx = Random.Range(0, target.Hand.Count);
                        CardDefinition stolenCard = target.Hand[randomIdx];

                        target.Hand.RemoveAt(randomIdx);
                        player.Hand.Add(stolenCard);
                        UI.UpdateStatus($"{player.Definition.PlayerName} یک کارت از {target.Definition.PlayerName} دزدید!");
                    }
                    else
                    {
                        UI.UpdateStatus("هیچ بازیکنی کارت نداشت.");
                    }

                    foreach (var c in _pendingComboList) _gameData.DiscardPile.Add(c);
                    ResetPlaySequence();
                    HandleTurnEndAfterPlay(player, isHumanTurn);
                }
            }
            else if (totalCards == 3)
            {
                // اجرای ترکیب سه کارتی (چه سه Nope باشند، چه سه کارت هم‌نوع)
                UI.UpdateStatus("ترکیب سه کارتی فعال شد!");

                if (isHumanTurn)
                {
                    var aliveTargets = _gameData.Players.Where(p => p.IsAlive && p != player).ToList();

                    if (aliveTargets.Count == 0)
                    {
                        UI.UpdateStatus("هیچ بازیکن زنده‌ای برای گرفتن کارت وجود ندارد.");
                        foreach (var c in _pendingComboList) _gameData.DiscardPile.Add(c);
                        ResetPlaySequence();
                        HandleTurnEndAfterPlay(player, isHumanTurn);
                        yield break;
                    }

                    CurrentState = GameState.WaitingForAction;
                    _waitingForInput = true;

                    // *** باز کردن پنل برای حالت 3 کارتی ***
                    UI.ShowThreeCardComboTargetSelection(aliveTargets, (selectedTarget) =>
                    {
                        UI.ShowThreeCardComboCardSelection(AllUniqueCards, (selectedCard) =>
                        {
                            ResolveThreeCardCombo(selectedTarget, selectedCard, player);
                        });
                    });

                    yield break;
                }
                else
                {
                    // هوش مصنوعی
                    var aliveTargets = _gameData.Players.Where(p => p.IsAlive && p != player).ToList();
                    if (aliveTargets.Count > 0)
                    {
                        var target = aliveTargets[Random.Range(0, aliveTargets.Count)];
                        var requestedCard = AllUniqueCards[Random.Range(0, AllUniqueCards.Count)];
                        ResolveThreeCardCombo(target, requestedCard, player);
                    }
                    else
                    {
                        foreach (var c in _pendingComboList) _gameData.DiscardPile.Add(c);
                        ResetPlaySequence();
                        HandleTurnEndAfterPlay(player, isHumanTurn);
                    }
                }
            }
        }
        
        private void ResolveTwoCardCombo(PlayerData target)
        {
            if (!_waitingForInput || target == null) return;
            _waitingForInput = false;

            // بستن پنل
            UI.HidePlayerDetailsPopup();

            PlayerData currentPlayer = _gameData.Players[_gameData.CurrentPlayerIndex];

            if (target.Hand.Count > 0)
            {
                // انتخاب کارت تصادفی از بازیکن هدف (چون کارت‌ها پشت بودند)
                int randomIdx = Random.Range(0, target.Hand.Count);
                CardDefinition stolenCard = target.Hand[randomIdx];

                target.Hand.RemoveAt(randomIdx);       // کم کردن از حریف
                currentPlayer.Hand.Add(stolenCard);    // اضافه به بانک ما

                UI.UpdateStatus($"کارت {stolenCard.CardName} از {target.Definition.PlayerName} گرفته شد.");
            }
            else
            {
                UI.UpdateStatus("بازیکن انتخاب شده کارتی نداشت.");
            }

            // انتقال کارت‌های ترکیب به دک سوخته
            foreach (var c in _pendingComboList)
            {
                _gameData.DiscardPile.Add(c);
            }

            // پایان نوبت
            ResetPlaySequence();
            //UpdateDiscard();
            RefreshHumanHandDisplay();
            CurrentState = GameState.PlayerTurn;
            HandleTurnEndAfterPlay(currentPlayer, true);
        }
        private void ResolveThreeCardCombo(PlayerData target, CardDefinition requestedCard, PlayerData currentPlayer)
        {
            if (!_waitingForInput && currentPlayer.Definition.IsHuman) return;
            _waitingForInput = false;

            UI.HidePlayerDetailsPopup();

            // بررسی اینکه آیا بازیکن هدف کارت درخواستی را دارد یا نه
            CardDefinition foundCard = target.Hand.FirstOrDefault(c => c.CardType == requestedCard.CardType);

            if (foundCard != null)
            {
                target.Hand.Remove(foundCard);
                currentPlayer.Hand.Add(foundCard);
                UI.UpdateStatus($"{currentPlayer.Definition.PlayerName} یک {foundCard.CardName} از {target.Definition.PlayerName} گرفت.");
            }
            else
            {
                UI.UpdateStatus($"{target.Definition.PlayerName} کارت {requestedCard.CardName} را نداشت. سه کارت شما سوخت!");
            }

            // در هر صورت (موفق یا ناموفق)، هر 3 کارت ترکیب به دک سوخته می‌روند
            foreach (var c in _pendingComboList)
            {
                _gameData.DiscardPile.Add(c);
            }

            ResetPlaySequence();
            //UpdateDiscard();
            RefreshHumanHandDisplay();
            CurrentState = GameState.PlayerTurn;
            HandleTurnEndAfterPlay(currentPlayer, currentPlayer.Definition.IsHuman);
        }
        // بررسی اینکه آیا قابلیت کارت در حالت تکی قابل اجراست یا خیر
        private bool CanCardEffectExecute(CardDefinition card)
        {
            switch (card.CardType)
            {
                case CardType.Cat:
                case CardType.Rooster:
                case CardType.Lama:
                case CardType.Squash:
                    return false; // کارت‌های حیوانات قابلیت تکی ندارند
                case CardType.Clone:
                    if (_gameData.DiscardPile.Count == 0) return false;
                    if (_gameData.DiscardPile[_gameData.DiscardPile.Count - 1].CardType == CardType.Clone) return false;
                    return true;
                case CardType.FeedDead:
                    return _gameData.Players.Any(p => !p.IsAlive);
                case CardType.Eye:
                    return _revealedZombieIndex != -1; // فقط بعد از نجات با فرشته
                case CardType.Gift:
                    return _gameData.Players.Any(p => p.IsAlive && p != _activePlayer);
                case CardType.Nope:
                    return false; // Nope فقط در پنجره Nope قابل بازی است
                case CardType.DeadsAttack: // *** این بخش جدید اضافه شد ***
                    return _gameData.Players.Any(p => !p.IsAlive); // حمله ارواح فقط با وجود مرده کار می‌کند
                case CardType.GraveRubber:
                    return _gameData.Players.Any(p => !p.IsAlive); // مرده خوری فقط با وجود مرده کار می‌کند

                default:
                    return true; // بقیه کارت‌ها (حمله، برزدن، اوراکل و...) قابل اجرا هستند
            }
        }

        // ریست کردن وضعیت بازی
        private void ResetPlaySequence()
        {
            _activePlayer = null;
            _pendingComboList.Clear();
            _isNopeWindowOpen = false;
            _nopeCount = 0;
            _lastNopePlayer = null; // *** ریست کردن ***

            if (_playTimerCoroutine != null)
            {
                StopCoroutine(_playTimerCoroutine);
                _playTimerCoroutine = null;
            }
        }

        // مدیریت پایان نوبت پس از اجرای کارت
        //private void HandleTurnEndAfterPlay(PlayerData player, bool isHumanTurn)
        //{
        //    UI.ClearBoard();
        //    UpdateDiscard();
        //    RefreshHumanHandDisplay();
        //    UI.RefreshPlayerPanels();

        //    if (CurrentState == GameState.GameOver) return;
        //    if (_waitingForInput) return; // اگر پنلی باز شد (مثل اوراکل)

        //    if (isHumanTurn)
        //    {
        //        if (_gameData.SkipDraw)
        //        {
        //            EndTurn(false);
        //        }
        //        else
        //        {
        //            CurrentState = GameState.PlayerTurn;
        //            RefreshHumanHandDisplay();
        //            UI.UpdateStatus($"نوبت {player.Definition.PlayerName} است. کارت دیگری بازی کنید یا دک بکشید.");
        //        }
        //    }
        //    else
        //    {
        //        EndTurn(true); // پایان نوبت هوش مصنوعی
        //    }
        //}
        //      ------------------- 14050814 ----------------------------------------------------
        private void HandleTurnEndAfterPlay(PlayerData player, bool isHumanTurn)
        {
            StartCoroutine(BurnAndEndTurnRoutine(player, isHumanTurn));
        }

        //private IEnumerator BurnAndEndTurnRoutine(PlayerData player, bool isHumanTurn)
        //{
        //    // 1. اجرای انیمیشن سوختن کارت‌های روی تخته
        //    bool animDone = false;
        //    if (UiAnimator != null)
        //    {
        //        UiAnimator.AnimateBurnCards(() => animDone = true);
        //        float timer = 0f;
        //        while (!animDone && timer < 3f)
        //        {
        //            timer += Time.deltaTime;
        //            yield return null;
        //        }
        //    }

        //    // 2. پاک کردن تخته و آپدیت UI
        //    UI.ClearBoard();
        //    UpdateDiscard();
        //    RefreshHumanHandDisplay();
        //    UI.RefreshPlayerPanels();

        //    if (CurrentState == GameState.GameOver) yield break;
        //    if (_waitingForInput) yield break; // اگر پنلی باز شد

        //    // 3. مدیریت نوبت
        //    if (isHumanTurn)
        //    {
        //        if (_gameData.SkipDraw)
        //        {
        //            EndTurn(false);
        //        }
        //        else
        //        {
        //            CurrentState = GameState.PlayerTurn;
        //            RefreshHumanHandDisplay();
        //            UI.UpdateStatus($"نوبت {player.Definition.PlayerName} است. کارت دیگری بازی کنید یا دک بکشید.");
        //        }
        //    }
        //    else
        //    {
        //        EndTurn(true);
        //    }
        //}
        //14050814 2
        private IEnumerator BurnAndEndTurnRoutine(PlayerData player, bool isHumanTurn)
        {
            Debug.Log($"[BurnRoutine] شروع. UiAnimator: {(UiAnimator != null)}");

            bool animDone = false;
            if (UiAnimator != null)
            {
                UiAnimator.AnimateBurnCards(() => {
                    Debug.Log("[BurnRoutine] Callback پایان انیمیشن");
                    animDone = true;
                });

                float timer = 0f;
                while (!animDone && timer < 3f)
                {
                    timer += Time.deltaTime;
                    yield return null;
                }

                if (!animDone) Debug.LogWarning("[BurnRoutine] تایم‌اوت!");
            }
            else
            {
                Debug.LogWarning("[BurnRoutine] UiAnimator NULL است! انیمیشن اجرا نمی‌شود.");
            }

            Debug.Log("[BurnRoutine] پاکسازی تخته و ادامه نوبت.");
            UI.ClearBoard();
            UpdateDiscard();
            RefreshHumanHandDisplay();
            UI.RefreshPlayerPanels();

            if (CurrentState == GameState.GameOver) yield break;
            if (_waitingForInput) yield break;

            if (isHumanTurn)
            {
                if (_gameData.SkipDraw)
                {
                    EndTurn(false);
                }
                else
                {
                    CurrentState = GameState.PlayerTurn;
                    RefreshHumanHandDisplay();
                    UI.UpdateStatus($"نوبت {player.Definition.PlayerName} است. کارت دیگری بازی کنید یا دک بکشید.");
                }
            }
            else
            {
                EndTurn(true);
            }
        }
        //private IEnumerator NopeWindowTimer(bool isHumanTurn, GameState previousState)
        //{
        //yield return new WaitForSeconds(3f);

        //_isNopeWindowOpen = false;
        //UI.ClearBoard();

        //// اگر تعداد Nope ها زوج بود (یا صفر)، افکت کارت اجرا می‌شود
        //if (_nopeCount % 2 == 0)
        //{
        //    if (_nopeCount > 0)
        //        UI.UpdateStatus("با توجه به تعداد زوج Nope، افکت کارت فعال شد.");

        //    // اجرای افکت کارت (این متد خودش کارت را از دست حذف و به دک سوخته اضافه می‌کند)
        //    int extrasBefore = _gameData.ExtraTurns;
        //    _engine.PlayCards(_pendingPlayer, new List<CardDefinition> { _pendingCard });
        //    ApplyExtrasDelta(extrasBefore);
        //    UI.RefreshPlayerPanels();
        //    UpdateDiscard();

        //    if (CurrentState == GameState.GameOver) yield break;

        //    if (_waitingForInput) // اگر کارت نیاز به پنل داشت (مثل اوراکل)
        //    {
        //        _pendingCard = null;
        //        yield break;
        //    }

        //    if (!isHumanTurn)
        //    {
        //        yield return new WaitForSeconds(1f);
        //        UI.ClearBoard();
        //        CurrentState = previousState;
        //        RefreshHumanHandDisplay();
        //    }
        //    else
        //    {
        //        yield return new WaitForSeconds(1f);
        //        UI.ClearBoard();
        //        if (_gameData.SkipDraw) EndTurn(false);
        //        else
        //        {
        //            CurrentState = GameState.PlayerTurn;
        //            RefreshHumanHandDisplay();
        //            UI.UpdateStatus($"نوبت {_pendingPlayer.Definition.PlayerName} است.");
        //        }
        //    }
        //}
        //else
        //{
        //    // تعداد Nope فرد بود: افکت کارت باطل می‌شود
        //    UI.UpdateStatus("با توجه به تعداد فرد Nope، افکت کارت باطل شد!");

        //    // *** اصلاح مهم: انتقال کارت باطل شده به دک سوخته ***
        //    if (_pendingCard != null && _pendingPlayer != null)
        //    {
        //        _pendingPlayer.Hand.Remove(_pendingCard);
        //        _gameData.DiscardPile.Add(_pendingCard);
        //    }

        //    RefreshHumanHandDisplay(); // آپدیت دست بازیکن
        //    UpdateDiscard();           // آپدیت دک سوخته

        //    if (!isHumanTurn)
        //    {
        //        yield return new WaitForSeconds(1f);
        //        CurrentState = previousState;
        //        RefreshHumanHandDisplay();
        //        NextTurn(); // اگر هوش مصنوعی بود، نوبتش تمام می‌شود
        //    }
        //    else
        //    {
        //        yield return new WaitForSeconds(1f);
        //        if (_gameData.SkipDraw) EndTurn(false);
        //        else
        //        {
        //            CurrentState = GameState.PlayerTurn;
        //            RefreshHumanHandDisplay();
        //            UI.UpdateStatus("افکت باطل شد. کارت دیگری بازی کنید یا دک بکشید.");
        //        }
        //    }
        //}

        //_pendingCard = null;
        //_pendingPlayer = null;
        //}


        private bool IsAnimalCard(CardDefinition card)
        {
            return card.CardType == CardType.Cat ||
                   card.CardType == CardType.Rooster ||
                   card.CardType == CardType.Lama ||
                   card.CardType == CardType.Squash;
        }

        private void HandleAnimalCardPlayed(CardDefinition card, PlayerData player)
        {
            // اگر در حال جمع‌آوری حیوان هستیم و کارت جدید با قبلی فرق دارد
            if (_currentAnimalCard != null && _currentAnimalCard.CardType != card.CardType)
            {
                UI.UpdateStatus("شما باید کارتی از همان نوع حیوان قبلی بازی کنید یا 3 ثانیه صبر کنید!");
                return;
            }

            // اگر توالی به 3 رسیده بود، اجازه بازی کارت چهارم را نده
            if (_animalCardsPlayed.Count >= 3)
            {
                UI.UpdateStatus("قابلیت سه‌تایی فعال شده است. لطفاً صبر کنید.");
                return;
            }

            // اگر توالی جدید است
            if (_currentAnimalCard == null)
            {
                _currentAnimalCard = card;
            }

            // حذف از دست و اضافه به لیست بازی شده‌ها
            player.Hand.Remove(card);
            _animalCardsPlayed.Add(card);

            // نمایش در اسلات‌ها
            UI.ShowCardsOnBoard(_animalCardsPlayed);
            RefreshHumanHandDisplay();

            // مدیریت تایمر
            if (_animalTimer != null) StopCoroutine(_animalTimer);

            if (_animalCardsPlayed.Count == 3)
            {
                // قابلیت سه‌تایی
                UI.UpdateStatus($"کارت سوم ({card.CardName}) آمد! قابلیت سه‌تایی فعال شد. (در حال پیاده‌سازی)");
                StartCoroutine(FinishAnimalSequence(player));
            }
            else
            {
                // شروع تایمر 3 ثانیه‌ای برای کارت دوم یا سوم
                _animalTimer = StartCoroutine(AnimalTimerRoutine(player));
            }
        }

        private IEnumerator AnimalTimerRoutine(PlayerData player)
        {
            yield return new WaitForSeconds(3f);

            if (_animalCardsPlayed.Count == 1)
            {
                // کارت اول تنها ماند
                UI.UpdateStatus($"کارت {_currentAnimalCard.CardName} تک بود و به دک سوخته رفت.");
                _gameData.DiscardPile.Add(_animalCardsPlayed[0]);

                ResetAnimalSequence();
                UI.ClearBoard();
                UpdateDiscard();
                CurrentState = GameState.PlayerTurn;
                RefreshHumanHandDisplay();
            }
            else if (_animalCardsPlayed.Count == 2)
            {
                // کارت دوم هم آمد اما سوم نیامد
                UI.UpdateStatus($"کارت دوم ({_currentAnimalCard.CardName}) آمد! قابلیت دوتایی فعال شد. (در حال پیاده‌سازی)");

                // در اینجا بعد از 1.5 ثانیه مکث، کارت‌ها را به دک سوخته می‌فرستیم
                yield return new WaitForSeconds(1.5f);
                StartCoroutine(FinishAnimalSequence(player));
            }
        }

        private IEnumerator FinishAnimalSequence(PlayerData player)
        {
            yield return new WaitForSeconds(1.5f); // مکث کوتاه برای دیدن نتیجه

            // انتقال همه کارت‌های حیوان به دک سوخته
            foreach (var c in _animalCardsPlayed)
            {
                _gameData.DiscardPile.Add(c);
            }

            ResetAnimalSequence();

            UI.ClearBoard();
            UpdateDiscard();
            CurrentState = GameState.PlayerTurn;
            RefreshHumanHandDisplay();
            UI.UpdateStatus("نوبت شما ادامه دارد.");
        }

        private void ResetAnimalSequence()
        {
            _currentAnimalCard = null;
            _animalCardsPlayed.Clear();
            if (_animalTimer != null)
            {
                StopCoroutine(_animalTimer);
                _animalTimer = null;
            }
        }

        //------------- ذخیره سازی ---------------
        public bool HasSavedGame()
        {
            string path = Path.Combine(Application.persistentDataPath, "savegame.json");
            return File.Exists(path);
        }

        public void SaveGame()
        {
            if (_gameData == null) return;

            // متوقف کردن هرگونه عملیات در حال اجرا (مثل نوبت هوش مصنوعی)
            StopAllCoroutines();
            CurrentState = GameState.PlayerTurn; // ذخیره در حالت نوبت بازیکن

            SaveData data = new SaveData();

            // ذخیره کارت‌های دک، دک سوخته و گورستان بر اساس نام
            data.DeckCards = _gameData.Deck.Select(c => c.CardName).ToList();
            data.DiscardPileCards = _gameData.DiscardPile.Select(c => c.CardName).ToList();
            data.GraveyardCards = _gameData.Graveyard.Select(c => c.CardName).ToList();

            data.CurrentPlayerIndex = _gameData.CurrentPlayerIndex;
            data.ExtraTurns = _gameData.ExtraTurns;
            data.SkipDraw = _gameData.SkipDraw;

            // ذخیره اطلاعات بازیکنان
            foreach (var player in _gameData.Players)
            {
                PlayerSaveData pData = new PlayerSaveData
                {
                    PlayerName = player.Definition.PlayerName,
                    AvatarIndex = player.Definition.AvatarIndex,
                    IsHuman = player.Definition.IsHuman,
                    IsAlive = player.IsAlive,
                    HasAngel = player.HasAngel,
                    HandCardNames = player.Hand.Select(c => c.CardName).ToList()
                };
                data.Players.Add(pData);
            }

            string json = JsonUtility.ToJson(data, true);
            string path = Path.Combine(Application.persistentDataPath, "savegame.json");
            File.WriteAllText(path, json);

            Debug.Log($"بازی در مسیر {path} ذخیره شد.");
        }

        public void LoadGame()
        {
            // 1. بررسی ایمنی برای UI (مهم‌ترین بخش)
            if (UI == null)
                UI = FindObjectOfType<GameUIController>();

            if (UI == null)
            {
                Debug.LogError("GameUIController پیدا نشد! نمی‌توان بازی را لود کرد.");
                return;
            }

            string path = Path.Combine(Application.persistentDataPath, "savegame.json");
            if (!File.Exists(path))
            {
                Debug.LogError("فایل ذخیره‌سازی پیدا نشد!");
                return;
            }

            string json = File.ReadAllText(path);
            SaveData data = JsonUtility.FromJson<SaveData>(json);

            if (data == null)
            {
                Debug.LogError("داده‌های فایل ذخیره‌سازی نامعتبر است!");
                return;
            }

            // بازسازی دیتای بازی
            _gameData = new GameData();

            // تابع کمکی برای پیدا کردن کارت بر اساس نام
            CardDefinition FindCard(string cardName)
            {
                if (AllCards == null) return null;
                return AllCards.FirstOrDefault(c => c.CardName == cardName);
            }

            _gameData.Deck = data.DeckCards.Select(FindCard).Where(c => c != null).ToList();
            _gameData.DiscardPile = data.DiscardPileCards.Select(FindCard).Where(c => c != null).ToList();
            _gameData.Graveyard = data.GraveyardCards.Select(FindCard).Where(c => c != null).ToList();

            _gameData.CurrentPlayerIndex = data.CurrentPlayerIndex;
            _gameData.ExtraTurns = data.ExtraTurns;
            _gameData.SkipDraw = data.SkipDraw;

            _gameData.Players.Clear();
            if (PlayerDefinitions == null) PlayerDefinitions = new List<PlayerDefinition>();

            foreach (var pData in data.Players)
            {
                // پیدا کردن تعریف بازیکن از روی اسم
                var def = PlayerDefinitions.FirstOrDefault(p => p.PlayerName == pData.PlayerName);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<PlayerDefinition>();
                    def.PlayerName = pData.PlayerName;
                    def.AvatarIndex = pData.AvatarIndex;
                    def.IsHuman = pData.IsHuman;
                    PlayerDefinitions.Add(def); // اضافه کردن به لیست برای استفاده‌های بعدی
                }

                PlayerData p = new PlayerData(def);
                p.IsAlive = pData.IsAlive;
                p.HasAngel = pData.HasAngel;
                p.Hand = pData.HandCardNames.Select(FindCard).Where(c => c != null).ToList();

                _gameData.Players.Add(p);
            }

            // مقداردهی مجدد موتور بازی
            _engine = new GameEngine(_gameData, UI, this);

            // آپدیت UI
            UI.BuildPlayerPanels(_gameData.Players);
            UI.UpdateDeckCount(_gameData.Deck.Count);
            UpdateDiscard();
            RefreshHumanHandDisplay();

            // پاکسازی تخته و پنل‌ها
            UI.ClearBoard();
            UI.HideGiftTargetPopup(); // اگر پنلی باز مانده بود

            // شروع مجدد نوبت
            CurrentState = GameState.PlayerTurn;
            StartTurn();
        }

        //------------------- Animation ---------------
        private IEnumerator BurnAndNextTurnRoutine()
        {
            Debug.Log("[BurnNext] شروع.");

            bool animDone = false;
            if (UiAnimator != null)
            {
                UiAnimator.AnimateBurnCards(() => { animDone = true; });
                float timer = 0f;
                while (!animDone && timer < 3f) { timer += Time.deltaTime; yield return null; }
            }

            UI.ClearBoard();
            UpdateDiscard();
            RefreshHumanHandDisplay();
            UI.RefreshPlayerPanels();

            NextTurn();
        }

        private bool _playerStartedChainWithExtraTurns = false;

        public void ResetAllTurns()
        {
            _gameData.ExtraTurns = 0;
            _extraTurnsForNextPlayer = 0;
        }
        private IEnumerator BurnAndForceNextTurnRoutine()
        {
            Debug.Log("[ForceNext] شروع روتین انتقال اجباری نوبت.");

            bool animDone = false;
            if (UiAnimator != null)
            {
                UiAnimator.AnimateBurnCards(() => animDone = true);
                float timer = 0f;
                while (!animDone && timer < 3f) { timer += Time.deltaTime; yield return null; }
            }

            UI.ClearBoard();
            UpdateDiscard();
            RefreshHumanHandDisplay();
            UI.RefreshPlayerPanels();

            Debug.Log("[ForceNext] فراخوانی NextTurn(forceAdvance: true)");
            NextTurn(forceAdvance: true);
        }
    }
}