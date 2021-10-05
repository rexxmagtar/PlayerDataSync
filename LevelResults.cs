using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.pigsels.BubbleTrouble
{
    /// <summary>
    /// Tracks player progress at current game level and contains all results of a level completion.
    /// Note: if a level play session was aborted, the <see cref="isFailed"/> field will be true and all other fields will contain empty values.
    ///
    /// Also calculates the player's score during game level play session.
    /// Score base modifiers are read from GameManager.Settings.mainConfigParameters.
    /// </summary>
    public static class LevelResults
    {

#region Events and delegates

        /// <summary>
        /// This event is fired when score value is changed.
        /// </summary>
        public delegate void ScoreChanged(int oldValue, int newValue);
        public static event ScoreChanged OnScoreChanged;

        /// <summary>
        /// This event is fired when a GameResource collected value is changed.
        /// </summary>
        /// <param name="gameResource">The resource changed.</param>
        /// <param name="oldValue">The amount of the resource before change.</param>
        /// <param name="newValue">The amount of the resource after change.</param>
        /// <param name="eventPosition">World space where the event occured that led to the resource mount change.</param>
        /// <param name="details">Change details (avaulable only for some GameResources. E.g.: for Booster contains type of booster).</param>
        public delegate void GameResourceChanged(GameResource gameResource, int oldValue, int newValue, Vector2? eventPosition, System.Object details);
        public static event GameResourceChanged OnGameResourceChanged;

#endregion


#region public properties

        /// <summary>
        /// Whether current game level is finished.
        /// </summary>
        public static bool isLevelFinished { get; private set; }

        /// <summary>
        /// Is level play session was completed successfully (not aborted, not failed).
        /// </summary>
        public static bool isSuccessfull { get; private set; }

        /// <summary>
        /// Amounts of GameResources collected.
        /// </summary>
        private static Dictionary<GameResource, int> gameResourcesCollected;

        /// <summary>
        /// Details of GameResources collected.
        /// Tracked only for some GameResources that have specific details (parameters) line Boosters and Powerups.
        /// <see cref="ShouldTrackGameResourceDetails"/>.
        /// </summary>
        private static Dictionary<GameResource, List<Tuple<int, System.Object>>> gameResourcesCollectedDetails;

        /// <summary>
        /// Score the player got at the level.
        /// </summary>
        public static int scoreEarned { get; private set; }

        /// <summary>
        /// Is new high score achieved in this session?
        /// </summary>
        public static bool isNewHighScore { get; private set; }

        /// <summary>
        /// Dictionary of ItemSO's names collected by the player at this game level inexed by Bubbles containing these items.
        /// Contains only counts for Items that can be collected limited number of times.
        /// After a level is completed successfully the collected Items having quantity limit no longer appear at the level in subsequent level plays.
        /// More details: https://pigsels.atlassian.net/wiki/spaces/BUBBLEGAME/pages/5570593
        /// </summary>
        public static Dictionary<string, string> collectedItems { get; private set; }

#endregion


#region private properties

        /// <summary>
        /// Whether the class is initialized.
        /// </summary>
        private static bool isInit;

        /// <summary>
        /// Cached link to a LevelSaveData that stores all stats for a game level play results.
        /// Used to compare actual results with previous results to determine changes (e.g. isNewHighScore).
        /// </summary>
        private static LevelSaveData levelSaveData;

        /// <summary>
        /// Cached link to LevelSettings for current game level.
        /// </summary>
        private static LevelSettings levelSettings;

        private static int _score;
        private static int score {
            get => _score;
            set
            {
                if (_score != value) {
                    var oldValue = _score;
                    _score = value;

                    UpdateScore(_score);

                    if (isInit)
                    {
                        OnScoreChanged?.Invoke(oldValue, value);
                    }
                }
            }
        }

#endregion


        /// <summary>
        /// Initializes the LevelResults instance making it to subscribe to all needed events to monitor the player's progress at a game level progress.
        /// Must be followed by <see cref="Deinit()"/> call to avoid hanging handlers especially for static classes.
        /// </summary>
        /// <param name="levelSaveData"></param>
        public static void Init(LevelSaveData _levelSaveData)
        {
            if (isInit)
            {
                throw new Exception("The LevelResult is already initialized.");
            }

            if (GameManager.sceneManager == null)
            {
                throw new Exception("LevelSceneManager isn't initialized.");
            }

            isInit = true;

            levelSaveData = _levelSaveData ?? throw new ArgumentNullException(nameof(_levelSaveData));
            gameResourcesCollected = new Dictionary<GameResource, int>();
            gameResourcesCollectedDetails = new Dictionary<GameResource, List<Tuple<int, object>>>();
            collectedItems = new Dictionary<string, string>();

            levelSettings = GameManager.Settings.GetLevelSettings(levelIndex);
            if (levelSettings == null)
            {
                throw new Exception($"Game level settings not found. AppSettings doesn't contain settings for the game level {levelIndex}.");
            }

            if (levelSettings.levelIndex != levelIndex)
            {
                throw new Exception($"LevelResult init failed: levelSaveData.levelIndex ({levelIndex}) doesn't match levelSettings.levelIndex ({levelSettings.levelIndex}).");
            }

            Reset();

            _score = -1; // forcing OnScoreChanged to be fired.
            score = 0;

            // Setting all necessary events handlers.
            LevelSceneManager.CollisionController.OnBubbleSequenceBurst += CollisionController_BubbleSequencePoppedHandler;
            LevelSceneManager.CollisionController.OnLateSequenceBubbleAdded += CollisionController_LateSequenceBubbleAdded;
            Bubble.OnBubbleBurst += Bubble_OnBubblePop;
        }

        /// <summary>
        /// Deinitializes the LevelResults instance making it to unsubscribe from all needed events.
        /// Must be preceded by <see cref="Init()"/> call.
        /// </summary>
        public static void Deinit()
        {
            CheckIsInit();

            Reset();
            isInit = false;

            // Removing previously set events handlers.
            LevelSceneManager.CollisionController.OnBubbleSequenceBurst -= CollisionController_BubbleSequencePoppedHandler;
            LevelSceneManager.CollisionController.OnLateSequenceBubbleAdded -= CollisionController_LateSequenceBubbleAdded;
            Bubble.OnBubbleBurst -= Bubble_OnBubblePop;
        }

        /// <summary>
        /// Get LevelIndex of a level tracked this this instance of LevelResults.
        /// </summary>
        public static LevelIndex levelIndex
        {
            get
            {
                CheckIsInit();
                return levelSaveData.levelIndex.Clone();
            }

        }

        /// <summary>
        /// Finishes the game level.
        /// </summary>
        /// <param name="isSuccess">Ture if level completed or false if failed or aborted.</param>
        public static void FinishLevel(bool isSuccess)
        {
            CheckIsInit();

            if (isLevelFinished)
            {
                throw new Exception($"The game level {levelIndex} has been already finished.");
            }

            isLevelFinished = true;
            isSuccessfull = isSuccess;

            // Make sure score is actual (it depends on level completion).
            UpdateScore(scoreEarned);
        }

        public static int GetScore()
        {
            CheckIsInit();
            return score;
        }

        /// <summary>
        /// Adds caustom score value to the score counter.
        /// </summary>
        /// <param name="valueToAdd"></param>
        public static void AddCustomScore(int valueToAdd)
        {
            CheckIsInit();

            if (valueToAdd == 0) return;
            score += valueToAdd;
        }

        /// <summary>
        /// Add custom amount of a specified GameResource to LevelResults.
        /// </summary>
        /// <param name="resourceAmount">Amount of the resource to gain.</param>
        /// <param name="eventPosition">Where the event that granted the resource has occured. Null if none or unknown.</param>
        public static void GainGameResource(GameResource resource, int resourceAmount, System.Object resourceDetails, Vector2? eventPosition)
        {
            CheckIsInit();
            Debug.Assert(resourceAmount != 0);
            if (resourceAmount == 0)
            {
                return;
            }

            var oldValue = gameResourcesCollected[resource];
            gameResourcesCollected[resource] += resourceAmount;

#if DEBUG
            if (GameManager.Instance.debugSettings.verboseItems)
            {
                if (resource == GameResource.Crystal || resource == GameResource.Gold)
                {
                    Debug.Log($"> Total collected {resource}: {gameResourcesCollected[resource]}");
                }
            }
#endif

            if (ShouldTrackGameResourceDetails(resource))
            {
                gameResourcesCollectedDetails[resource].Add(new Tuple<int, object>(resourceAmount, resourceDetails));
            }

            OnGameResourceChanged?.Invoke(resource, oldValue, gameResourcesCollected[resource], eventPosition, resourceDetails);
        }

        /// <summary>
        /// Return all the details on a specified <see cref="GameResource"/> gained.
        /// </summary>
        /// <param name="resource">GameResource to return gained info for.</param>
        /// <returns>List of Tuple<int, System.Object> (amount and details object) or null if the specified GameResource doesn't support details.</returns>
        public static List<Tuple<int, System.Object>> GetGameResourceGained(GameResource resource)
        {
            if (!isInit)
            {
                return null;
            }
            return gameResourcesCollectedDetails.TryGetValue(resource, out var details) ? details : null;
        }

        /// <summary>
        /// Return total amount gained of a specified <see cref="GameResource"/>.
        /// </summary>
        /// <param name="resource">GameResource to return gained count of.</param>
        /// <returns></returns>
        public static int GetAmountOfGameResourceGained(GameResource resource)
        {
            if (!isInit)
            {
                return 0;
            }
            return gameResourcesCollected[resource];
        }

        /// <summary>
        /// Check whether details tracking of collected GameResources of a specified type if required.
        /// </summary>
        /// <param name="resource"></param>
        /// <returns></returns>
        private static bool ShouldTrackGameResourceDetails(GameResource resource)
        {
            switch (resource)
            {
                case GameResource.Crystal:
                case GameResource.Gold:
                case GameResource.Star:
                case GameResource.Energy:
                case GameResource.Mana:
                    return false;

                case GameResource.Booster:
                case GameResource.Powerup:
                case GameResource.Animal:
                    return true;

                default:
                    throw new Exception("Unknown GameResource.");
            }
        }

        private static void CheckIsInit()
        {
            if (!isInit)
            {
                throw new Exception("The LevelResult is not initialized.");
            }
        }

        /// <summary>
        /// Resets LevelResults to a default state.
        /// </summary>
        private static void Reset()
        {
            CheckIsInit();

            isLevelFinished = false;

            isSuccessfull = false;
            scoreEarned = 0;
            isNewHighScore = false;
            collectedItems.Clear();

            gameResourcesCollected.Clear();
            gameResourcesCollectedDetails.Clear();
            foreach (var item in Enum.GetValues(typeof(GameResource)))
            {
                gameResourcesCollected.Add((GameResource)item, 0);

                if (ShouldTrackGameResourceDetails((GameResource)item))
                {
                    gameResourcesCollectedDetails.Add((GameResource)item, new List<Tuple<int, System.Object>>());
                }
            }

            score = 0;
        }

        private static void Bubble_OnBubblePop(Bubble bubble, BubbleBurstCause cause)
        {
            if (isLevelFinished)
            {
                // No bubble processing after the level has been finished.
                Debug.LogWarning("Bubble burst after the level has been finished.");
                return;
            }

            // Don't give single bubble pop score if a bubble is a part of a prevously popped bubble sequence.
            // Also ignore bubbles popped with BubblePopCause.None ause since it's used solely for debug purpose.
            if (cause != BubbleBurstCause.BubbleSequence && cause != BubbleBurstCause.None)
            {
                score += GameManager.Settings.mainConfigParameters.scoreSingleBubbleBurst;
            }

            if (cause == BubbleBurstCause.None || bubble.containedItem == null) {
                return;
            }

            if (bubble.IsItemCollectTrackingRequired())
            {
                if (!collectedItems.ContainsKey(bubble.GameEntityName))
                {
                    collectedItems.Add(bubble.GameEntityName, bubble.containedItem.name);
                }
                else
                {
                    Debug.LogWarning($"Duplicate detected: the Bubble \"{bubble.GameEntityName}\" with Item \"{bubble.containedItem.name}\" was already collected.");
                }
            }
        }

        /// <summary>
        /// Bubble sequence collapse (burst) handler.
        /// Calculates score for a collapsed bubble sequence.
        /// The more bubbles in the sequence, the more score is given for each additional bubble.
        /// </summary>
        /// <param name="bubbles"></param>
        private static void CollisionController_BubbleSequencePoppedHandler(HashSet<Bubble> bubbles)
        {
            // No score update after the level has been finished.
            if (isLevelFinished)
            {
                Debug.LogWarning("Bubble sequence has burst after the level has been finished.");
                return;
            }

            int newScore = score;
            int sequenceLength = bubbles.Count;
            int bubblesOverThree = Mathf.Max(sequenceLength - 3, 0);
            int sequenceBubblePop = GameManager.Settings.mainConfigParameters.scoreBubbleBurst;

            // See GDD for the formula explanation:
            // https://docs.google.com/document/d/1FGdhBoocb1d4VYgKggqBGYkXbTEft45q2YPzoLoNdms/edit
            newScore += Math.Min(sequenceLength, 3) * sequenceBubblePop +
                bubblesOverThree * (sequenceBubblePop + Mathf.RoundToInt(sequenceBubblePop / 2 * bubblesOverThree));

            if (newScore != score)
            {
                score = newScore;
            }
        }

        /// <summary>
        /// This is called when a Bubble is added to an already collapsing bubble sequence the last moment.
        /// Special score award exists for the case (see <see cref="MainConfigParameters.scoreLateBubbleBurst"/> for details).
        /// </summary>
        /// <param name="bubble"></param>
        /// <param name="activatedBy"></param>
        private static void CollisionController_LateSequenceBubbleAdded(Bubble bubble, Bubble activatedBy)
        {
            // No score update after the level has been finished.
            if (isLevelFinished)
            {
                Debug.LogWarning("Late Bubble sequence added after the level has been finished.");
                return;
            }

            score += GameManager.Settings.mainConfigParameters.scoreLateBubbleBurst;
        }

        private static void UpdateScore(int newScore)
        {
            // Make sure the player has at least 1 score point if they finished the level successfully.
            int minScore = (isLevelFinished && isSuccessfull) ? 1 : 0;
            scoreEarned = Mathf.Max(newScore, minScore);

            // Updating high-score state
            isNewHighScore = scoreEarned > levelSaveData.maxScore;

            gameResourcesCollected[GameResource.Star] = CalculateStarsByScore(scoreEarned);
        }

        /// <summary>
        /// Calculates number of stars earned based on score provided and cached LevelSettings parameters.
        /// </summary>
        /// <param name="score"></param>
        /// <returns>Number of stars earned: [0..3]</returns>
        private static int CalculateStarsByScore(int score)
        {
            if (score == 0) return 0;

            int ret = 1;

            if (score >= levelSettings.scoreThreshold3Stars)
            {
                ret = 3;
            }
            else if (score >= levelSettings.scoreThreshold2Stars)
            {
                ret = 2;
            }

            return ret;
        }

    }

}
