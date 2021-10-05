using System;
using System.Collections.Generic;
using System.Reflection;
using com.pigsels.tools;

namespace com.pigsels.BubbleTrouble
{
    /// <summary>
    /// Holds all player profile data including saved game data, purchases made and stats.
    /// Intended to be stored locally and remotely (in cloud) using PlayerProfile storages derived from PlayerProfileStorage.
    ///
    /// WARNING: for any non value-type fields added/modified the relevant code must be implemented in Cone() method.
    /// </summary>
#if UNITY_EDITOR
    [OdinSerializableInclude]
#endif
    public class PlayerProfileV1 : IPlayerProfile
    {
        public class PowerUp
        {
            public int id;

            public int count;
        }

        public class Booster
        {
            public int id;

            public int count;
        }

        public class Payment
        {
            public string iaId;

            public string purchaseId;

            public string platform;

            public long dateTime;

            public string currency;

            public float price;
        }


        /// <summary>
        /// PlayerProfile version. Used for migration purpose.
        /// </summary>
        public int Version => 2;

#region meta data

        /// <summary>
        /// Version of the game application that was used to create (save) this player profile last time.
        /// Game application versioning must be done according to https://semver.org/
        /// TODO: use this https://github.com/Artees/Unity-SemVer for version control instead of simple string.
        /// TODO: also use this https://github.com/Artees/Unity-Application-Version (it works in conjunction with SemVer).
        /// </summary>
        public string appVersion;

        /// <summary>
        /// Id of a device which created or saved this profile last time.
        /// Used to compare profiles on load.
        /// See https://docs.unity3d.com/ScriptReference/SystemInfo-deviceUniqueIdentifier.html for details.
        /// </summary>
        public string deviceId;

        /// <summary>
        /// Used to show user last active device. Used during merge conflicts.
        /// </summary>
        public string deviceModel;

        /// <summary>
        /// UTC Unix timestamp (https://www.unixtimestamp.com/) of a moment when this pofile was updated the last time.
        /// </summary>
        [NonSerialized]
        public long updateTime;

        /// <summary>
        /// Profiles hash calculated during last save. Md5 algorithm.
        /// </summary>
        [NonSerialized]
        public string hash;

        /// <summary>
        /// Hash of the profile that last conflict was solved with.
        /// </summary>
        public string lastSolvedConflictProfileHash;

        /// <summary>
        /// UTC Unix timestamp of a moment when this profile was created
        /// </summary>
        public long creationTime;

        /// <summary>
        /// Total time (number of seconds) played (spent in the game).
        /// It's actually a cached sum of all time spent by a player at all played levels of the game (see levelSaveData[].playTime).
        /// TODO: should be fixed to track all the time in the game, including world map and other activities.
        /// </summary>
        public int playTime;

        /// <summary>
        /// Identifier of the playerProfile's owner. If the owner hasn't linked with firebase this value will be null or empty.
        /// </summary>
        /// TODO: Save this field only after link with firebase.
        public string cloudId;

#endregion


#region saved game data

        /// <summary>
        /// Amount of crystals (hard-currency) player has.
        /// </summary>
        public int crystalCounter;

        /// <summary>
        /// Amount of gold (soft-currency) player has.
        /// </summary>
        public int goldCounter;

        /// <summary>
        /// Energy (lives) counter. Determines number of times the player can play levels.
        /// TODO: also store next top-up time + unlimited period (if one).
        /// </summary>
        public int energyCounter;

        /// <summary>
        /// Time till next energy refill.
        /// </summary>
        public long energyNextTopupTime;

        /// <summary>
        /// Time till the end of users unlimited energy. 
        /// </summary>
        public long energyUnlimEndTime;

        /// <summary>
        /// Total score points player has gained in the game.
        /// </summary>
        public int totalScore;

        /// <summary>
        /// Current amount of kitty bank gold.
        /// </summary>
        public int kittyBankGold;

        /// <summary>
        /// Current amount of kitty bank crystals.
        /// </summary>
        public int kittyBankCrystals;

        /// <summary>
        /// Players powerUps statuses.
        /// </summary>
        public List<PowerUp> powerUps = new List<PowerUp>();

        /// <summary>
        /// Players boosters statuses.
        /// </summary>
        public List<Booster> boosters = new List<Booster>();

        /// <summary>
        /// Players payments info.
        /// </summary>
        public List<Payment> payments = new List<Payment>();

        /// <summary>
        /// Next daily bonus claim time.
        /// </summary>
        public long dailyBonusNextTime;

        /// <summary>
        /// Last daily bonus claim time.
        /// </summary>
        public long dailyBonusLastClaimTime;

        /// <summary>
        /// Number of total claimed daily bonuses.
        /// </summary>
        public int dailyBonusTotalClaimed;

        /// <summary>
        /// Number of total star boxes opened.
        /// </summary>
        public int starBoxesOpened;

        /// <summary>
        /// Game levels saved data (completion state, stars, score etc.).
        /// </summary>
        public Dictionary<LevelIndex, LevelSaveData> levelSaveData;

        // TODO: add fields:
        /*
            4. KittyBank value.
            5. Power-ups list
            6. Boosters list
            7. Payments made (transactions) + info on how they were made (in this account or were moved from another one on merge (store another accounts data on merge too))
                (don't forget to complete the method PlayerProfile.IsEmpty() when purchase list is implemented). 
            10. List of gained collectables (like pictures).
            11. Daily bonus data (step, last received, etc).
         */

#endregion

        public PlayerProfileV1()
        {
            levelSaveData = new Dictionary<LevelIndex, LevelSaveData>();
        }

        /// <summary>
        /// Clones this PlayerProfile.
        /// </summary>
        /// <returns>PlayerProfile clone.</returns>
        public virtual PlayerProfileV1 Clone()
        {
            // Making shallow copy (value-types are copied and objects are copied by references).
            PlayerProfileV1 clone = this.MemberwiseClone() as PlayerProfileV1;

            // Now duplicating all the objects to break the references.

            clone.levelSaveData = new Dictionary<LevelIndex, LevelSaveData>();
            foreach (var item in this.levelSaveData)
            {
                clone.levelSaveData.Add(item.Key.Clone(), item.Value.Clone());
            }

            return clone;
        }

        /// <summary>
        /// Migrates this instance of PlayerProfile to the latest version of PlayerProfile.
        /// </summary>
        /// <returns>Migrated PlayerProfile.</returns>
        public IPlayerProfile MigrateToNextVersion()
        {
            throw new NotImplementedException();
        }

        private static void MapClassFields(object from, object to)
        {
            FieldInfo[] fields = from.GetType().GetFields();

            foreach (var field in fields)
            {
                field.SetValue(to, field.GetValue(from));
            }
        }

        public override string ToString()
        {
            return $"AppVersion: {appVersion}, DeviceId: {deviceId}, DeviceModel: {deviceModel}, UpdateTime: {updateTime}, Hash: {hash}, CreationTime: {creationTime}, PlayTime: {playTime}, CrystalCounter: {crystalCounter}, GoldCounter: {goldCounter}, EnergyCounter: {energyCounter}, EnergyNextTopupTime: {energyNextTopupTime}, EnergyUnlimEndTime: {energyUnlimEndTime}, TotalScore: {totalScore}, KittyBankGold: {kittyBankGold}, KittyBankCrystals: {kittyBankCrystals}, PowerUps: {powerUps}, Boosters: {boosters}, Payments: {payments}, DailyBonusNextTime: {dailyBonusNextTime}, DailyBonusLastClaimTime: {dailyBonusLastClaimTime}, DailyBonusTotalClaimed: {dailyBonusTotalClaimed}, StarBoxesOpened: {starBoxesOpened}, LevelSaveData: {levelSaveData}, Version: {Version}";
        }
    }
}