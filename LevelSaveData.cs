using System;
using System.Collections.Generic;
using UnityEngine;

namespace com.pigsels.BubbleTrouble
{
    /// <summary>
    /// Holds all data related to a level completion by a player.
    /// Intended to be stored as a part of PlayerProfile.
    ///
    /// See the documentation: https://pigsels.atlassian.net/wiki/spaces/BUBBLEGAME/pages/76152917
    /// 
    /// WARNING: for any non value-type fields added the relevant code must be implemented in Clone() method.
    /// </summary>
    [Serializable]
    public class LevelSaveData
    {
        /// <summary>
        /// Index of the level.
        /// </summary>
        public LevelIndex levelIndex;

        /// <summary>
        /// Max number of stars a player earned at this level.
        /// </summary>
        public int maxStarsEarned;

        /// <summary>
        /// Max score a player achieved at this level.
        /// </summary>
        public int maxScore;

        /// <summary>
        /// Total count of gold earned by the player at this level in all successfull sessions.
        /// </summary>
        public int goldTotalEarned;

        /// <summary>
        /// Total count of crystals earned by the player at this level in all successfull sessions.
        /// </summary>
        public int crystalsTotalEarned;

        /// <summary>
        /// Total time (in seconds) a player spent at this level.
        /// </summary>
        public int playTime;

        /// <summary>
        /// Number of times the player completed (finished) the level successfully.
        /// </summary>
        public int completedCount;

        /// <summary>
        /// Number of times a player played this level (including levelAbortedCount).
        /// So the number of sessions played till the end is equal to playCount-levelAbortedCount. 
        /// </summary>
        public int playCount;

        /// <summary>
        /// Dictionary of Items counts (actually Bubbles with Items) collected by the player at this game level inexed by Bubbles containing these items.
        /// Contains only counts for Items that can be collected limited number of times.
        /// More details: https://pigsels.atlassian.net/wiki/spaces/BUBBLEGAME/pages/5570593
        /// After a level is completed successfully the collected Items with quantity limit no longer appear at the level in subsequent level plays.
        /// </summary>
        //[OdinSerialize]
        public Dictionary<string, int> collectedItems;


        /// <summary>
        /// ctor
        /// </summary>
        /// <param name="_levelIndex"></param>
        public LevelSaveData(LevelIndex _levelIndex) {

            //We need to clone levelIndex to break reference to levelIndex.
            //This is done cause Odin serializes clones and direct references differently.
            //So we need to use  either references or clones everywhere.
            //Cause we already use Clone method during profile sync which breaks all the rules we also want to break all other rules. 
            this.levelIndex = _levelIndex.Clone();
            collectedItems = new Dictionary<string, int>();
        }

        /// <summary>
        /// Deep-clones this instance.
        /// </summary>
        /// <returns>A deep clone.</returns>
        public LevelSaveData Clone()
        {
            if (collectedItems == null)
            {
                collectedItems = new Dictionary<string, int>();
            }

            // Making shallow copy (value-types are copied and objects are copied by references).
            var clone = this.MemberwiseClone() as LevelSaveData;

            // Now duplicating all the objects to break the references.

            clone.levelIndex = this.levelIndex.Clone();
            clone.collectedItems = new Dictionary<string, int>(this.collectedItems);

            return clone;
        }

        /// <summary>
        /// Takes data from LevelResults (results of current level play session) and adds them up to the LevelSaveData.
        /// </summary>
        public void ApplyLevelResults(int _sessionLength)
        {
            if (LevelResults.levelIndex != levelIndex)
            {
                throw new Exception($"Can't apply level results for level \"{LevelResults.levelIndex}\" because its levelIndex don't match LevelSaveData level index \"{levelIndex}\".");
            }

            playCount++;
            playTime += _sessionLength;

            if (LevelResults.isSuccessfull)
            {
                int starsEarned = LevelResults.GetAmountOfGameResourceGained(GameResource.Star);

                if (starsEarned < 0 || starsEarned > 3)
                {
                    throw new Exception($"Wrong number of stars ({starsEarned}).");
                }

                completedCount++;
                maxStarsEarned = Mathf.Max(maxStarsEarned, starsEarned);
                maxScore = Mathf.Max(maxScore, LevelResults.scoreEarned);
                goldTotalEarned += LevelResults.GetAmountOfGameResourceGained(GameResource.Gold);
                crystalsTotalEarned += LevelResults.GetAmountOfGameResourceGained(GameResource.Crystal);

                if (collectedItems == null)
                {
                    collectedItems = new Dictionary<string, int>();
                }

                // Merging LevelResults.collectedItems into levelData.collectedItems
                if (LevelResults.collectedItems != null)
                {
                    // LevelResults.collectedItems: Bubble.GameEntityName -> ItemSO.name
                    foreach (var item in LevelResults.collectedItems)
                    {
                        if (collectedItems.ContainsKey(item.Key))
                        {
                            collectedItems[item.Key]++;
                        }
                        else
                        {
                            collectedItems.Add(item.Key, 1);
                        }
                    }
                }
            }
        }
    }

}