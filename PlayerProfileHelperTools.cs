using System.Collections.Generic;

namespace com.pigsels.BubbleTrouble
{

    /// <summary>
    /// Container for various PlayerProfile handling functions.
    /// </summary>
    public class PlayerProfileHelperTools
    {
        /// <summary>
        /// Gets last basic (non-extra) level completed by the player.
        /// </summary>
        /// <returns>LevelIndex of the last basic level completed or null is none.</returns>
        public static LevelIndex GetLastCompletedLevel(PlayerProfile profile)
        {
            if (profile.levelSaveData == null || profile.levelSaveData.Count == 0)
            {
                // No levels completed yed.
                return null;
            }

            // profile.levelSaveData doesn't guarantee level order.
            // Creating list of levels sorted by biomeIndex and then by levelId.
            var levelsComplete = new List<LevelIndex>(profile.levelSaveData.Keys);
            levelsComplete.Sort((LevelIndex a, LevelIndex b) =>
            {
                var biomeIndexA = GameManager.Settings.GetBiomeIndex(a.biomeId);
                var biomeIndexB = GameManager.Settings.GetBiomeIndex(b.biomeId);

                if (biomeIndexA == biomeIndexB)
                {
                    return a.levelId.CompareTo(b.levelId);
                }

                return biomeIndexA.CompareTo(biomeIndexB);
            });

            for (int i = levelsComplete.Count - 1; i >= 0; i--)
            {
                var levelIdx = levelsComplete[i];
                var levelInfo = profile.levelSaveData[levelIdx];

                if (levelInfo.maxStarsEarned == 0)
                {
                    continue;
                }

                var levelSettings = GameManager.Settings.GetLevelSettings(levelIdx);

                if (levelSettings == null || levelSettings.isExtra)
                {
                    continue;
                }

                return levelsComplete[i];
            }

            return null;
        }
    }
}