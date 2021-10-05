using System;
using UnityEngine;
using com.pigsels.tools;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace com.pigsels.BubbleTrouble
{
    /// <summary>
    /// Manages player profiles.
    /// </summary>
    public class PlayerProfileManager
    {
        /// <summary>
        /// Bit flags for profile comparison result.
        /// May be compined. Eg.: UseLocal | DifferentDevices | MergePurchases
        /// </summary>
        [Flags]
        private enum ProfileComparisonResult
        {
            Error = 1, // Profile comparison failed (eg.: remote profile isn't loaded or appVersion of profiles differ).
            Cancel = 2,
            IdenticalProfiles = 4, // the profiles are identical.
            UseLocalProfile = 8, // Local profile should be used (otherwise use Remote one).
            SameDevice = 16, // Both profiles are created on the same device (otherwise profiles are created on different devices).
            MovePurchases = 32, // Purchases must be moved into preferred profile from another one (see UseLocalProfile).
        }


#region Events and delegates

#endregion

        /// <summary>
        /// Storage to keep PlayerProfile locally.
        /// </summary>
        private PlayerProfileStorage localStorage;

        /// <summary>
        /// Storage to keep PlayerProfile remotely.
        /// </summary>
        private PlayerProfileStorage remoteStorage;

        /// <summary>
        /// ProfileManager init ready flag.
        /// </summary>
        public bool IsReady { get; private set; }

        /// <summary>
        /// Index of a game level being played currently. Null if none.
        /// </summary>
        private LevelIndex currentGameLevel;

        /// <summary>
        /// Unix timestamp when a game level session started.
        /// </summary>
        private long gameLevelSessionStartTime;

        /// <summary>
        /// Determines if local profile synced with remote storage after last save/load.
        /// </summary>
        public bool IsLocalProfileSyncedInCloud { get; private set; }

        /// <summary>
        /// Initializes PlayerProfileManager.
        /// </summary>
        /// <param name="_localStorage"></param>
        /// <param name="_remoteStorage"></param>
        public async Task<bool> Init(PlayerProfileStorage _localStorage, PlayerProfileStorage _remoteStorage)
        {
            localStorage = _localStorage;
            remoteStorage = _remoteStorage;

            IsReady = true;

            await Task.Yield();

            return IsReady;
        }

        /// <summary>
        /// Loads local and remote profiles and makes sure they are in sync.
        /// </summary>
        /// <returns>Bool task: true on success (even if temporary problems occured), false otherwise.</returns>
        public async Task<bool> LoadAndSyncProfiles()
        {
            IsLocalProfileSyncedInCloud = false;

            //TODO: now fresh hash is never stored in profile cause it is calculated dynamically only before serialization, subsequently to get hash we have to reload profile (firstly we need to save profile to get hash, and then load it again). Another approach is to calculate hash separately from saving. In this case we still will need to serialize profile to get it's hash. Need to choose one of the approach. For now the first one has been chosen. 
            //if (!localStorage.isLoaded)
            //{
            // Loading locally saved profile.

            switch (await localStorage.LoadAsync())
            {
                case PlayerProfileStorage.OperationResult.Failure:
                    Debug.LogError("Failed to load local profile");
                    // Failed to read local storage OR loaded profile is invalid. Can't continue.
                    return false;

                case PlayerProfileStorage.OperationResult.EmptyResult:
                    // No profile is saved locally. Creating a new one...
                    localStorage.playerProfile = new PlayerProfile();

                    // ...and saving it.
                    if (await localStorage.SaveAsync() == PlayerProfileStorage.OperationResult.Failure)
                    {
                        Debug.LogError("Failed to save new local profile");
                        // Failed to save just created local profile.
                        return false;
                    }
                    break;

                case PlayerProfileStorage.OperationResult.Success:
                default:
                    break;
            }
            //}

            // If the User does not have profile linked with remote storage then no sync is possible.
            if (PlayerAuth.GetUserId() == null)
            {
                return true;
            }

            // Loading remotely stored profile.
            switch (await remoteStorage.LoadAsync())
            {
                case PlayerProfileStorage.OperationResult.Success:

                    Debug.Log("Loaded remote profile. Syncing");
                    IsLocalProfileSyncedInCloud = await SyncLocalAndRemoteProfilesAsync();

                    break;

                case PlayerProfileStorage.OperationResult.EmptyResult:
                    // No profile is saved in remote storage. Replacing it with the local one...
                    remoteStorage.playerProfile = localStorage.playerProfile.Clone();

                    IsLocalProfileSyncedInCloud = await remoteStorage.SaveAsync() == PlayerProfileStorage.OperationResult.Success;

                    break;

                case PlayerProfileStorage.OperationResult.Failure:
                    Debug.LogWarning("Failed to load remote storage");
                    break;
            }

            Debug.Log("Loaded local profile: " + localStorage.playerProfile.ToString());

            if (remoteStorage.isLoaded)
            {
                Debug.Log("Loaded remote profile: " + remoteStorage.playerProfile.ToString());
            }

            return true;

        }

        /// <summary>
        /// Compares local and remote profile and performs sync if needed.
        /// </summary>
        /// <returns>True on success, false otherwise.</returns>
        private async Task<bool> SyncLocalAndRemoteProfilesAsync(bool enableMergeCancel = true)
        {
            // Make sure both profiles are loaded.

            if (!localStorage.isLoaded)
            {
                throw new Exception("Local player profle isn't loaded yet.");
            }

            if (!remoteStorage.isLoaded)
            {
                throw new Exception("Remote (cloud) player profle isn't loaded yet.");
            }


            // Comparing remote and local profiles.
            var comparisonResult = await CompareLocalAndRemoteProfilesAsync(enableMergeCancel);

            if ((comparisonResult & ProfileComparisonResult.Cancel) != 0)
            {
                //User canceled synchronization. Continue using current local profile without synchronization.
                Debug.Log("Sync was canceled");

                return false;
            }

            if ((comparisonResult & ProfileComparisonResult.Error) != 0)
            {
                // Failed to compare profiles.
                // TODO: what should we do here? Can this error occur at all, considering both profiles (remote and local) are successfully loaded?

                //GameManager.UIManager.ShowOkDialog("File sync error","Unexpected error accrued during profiles synchronization. Restart the app and try again", result =>
                //{

                //});

                return false;
            }

            if ((comparisonResult & ProfileComparisonResult.IdenticalProfiles) != 0)
            {
                Debug.Log("Profiles are identical");
                // Profiles are already in sync.
                return true;
            }

            if ((comparisonResult & ProfileComparisonResult.UseLocalProfile) != 0)
            {
                // LOCAL profile is newer, using it.

                Debug.Log("Using local profile");
                if ((comparisonResult & ProfileComparisonResult.MovePurchases) != 0)
                {
                    // Have to move purchases from the remote profile to the local.
                    MovePurchases(remoteStorage.playerProfile, localStorage.playerProfile);
                    await localStorage.SaveAsync();
                }

                //TODO: decide if backup is needed in this case
                //await remoteStorage.BackupAsync(remoteStorage.playerProfile);

                localStorage.playerProfile.Touch();

                localStorage.playerProfile.lastSolvedConflictProfileHash = remoteStorage.playerProfile.hash;

                // Replacing remote profile with updated local one.
                remoteStorage.playerProfile = localStorage.playerProfile.Clone();

                if (await localStorage.SaveAsync() == PlayerProfileStorage.OperationResult.Failure)
                {
                    return false;
                }

                await remoteStorage.SaveAsync();

            }
            else
            {
                // REMOTE profile is newer, using it.
                Debug.Log("Using remote profile");
                if ((comparisonResult & ProfileComparisonResult.MovePurchases) != 0)
                {
                    // Have to move purchases from the local profile to the remote.
                    MovePurchases(localStorage.playerProfile, remoteStorage.playerProfile);
                }

                if (await remoteStorage.BackupAsync(localStorage.playerProfile) == PlayerProfileStorage.OperationResult.Failure)
                {
                    Debug.LogError("Failed to do backup");
                }

                remoteStorage.playerProfile.Touch();

                // Replacing local profile with updated remote one.
                localStorage.playerProfile = remoteStorage.playerProfile.Clone();

                localStorage.playerProfile.lastSolvedConflictProfileHash = remoteStorage.playerProfile.hash;

                if (await localStorage.SaveAsync() == PlayerProfileStorage.OperationResult.Failure)
                {
                    return false;
                }

                await remoteStorage.SaveAsync();

            }

            return true;
        }

        /// <summary>
        /// Actual Player Profile.
        /// </summary>
        public PlayerProfile profile
        {
            get
            {
                return localStorage.playerProfile;
            }

            private set
            {
                localStorage.playerProfile = value;
            }
        }

        /// <summary>
        /// Compares local and remote profiles. Asks player which profile to choose in case of conflict.
        /// </summary>
        /// <returns></returns>
        private async Task<ProfileComparisonResult> CompareLocalAndRemoteProfilesAsync(bool enableMergeCancel)
        {
            if (!localStorage.isLoaded)
            {
                throw new Exception("Local player profle isn't loaded yet.");
            }

            if (!remoteStorage.isLoaded)
            {
                throw new Exception("Remote (cloud) player profle isn't loaded yet.");
            }


            PlayerProfile localPlayerProfile = localStorage.playerProfile;
            PlayerProfile remotePlayerProfile = remoteStorage.playerProfile;

            Debug.Log("remote creation time = " + DateTimeTools.UnixTimeStampToString(remotePlayerProfile.creationTime) +
                      "; local creation time = " + DateTimeTools.UnixTimeStampToString(localPlayerProfile.creationTime));

            Debug.Log("remote update time = " + DateTimeTools.UnixTimeStampToString(remotePlayerProfile.updateTime) +
                      "; local update time = " + DateTimeTools.UnixTimeStampToString(localPlayerProfile.updateTime));

            Debug.Log("remote device id = " + remotePlayerProfile.deviceId + ". local device id = " + localPlayerProfile.deviceId);

            ProfileComparisonResult useLocalSaves = 0;
            ProfileComparisonResult sameDevice = 0;
            ProfileComparisonResult needMovePurchase = 0;

            if (remotePlayerProfile.hash == localPlayerProfile.hash)
            {
                Debug.Log("Profiles have same hash. They are identical");
                return ProfileComparisonResult.IdenticalProfiles;
            }

            if (!string.IsNullOrEmpty(localPlayerProfile.lastSolvedConflictProfileHash) &&
                remotePlayerProfile.hash == localPlayerProfile.lastSolvedConflictProfileHash)
            {
                Debug.Log("Local profile lastSyncedProfileHash equal to remote hash");
                return ProfileComparisonResult.UseLocalProfile;
            }

            if (remotePlayerProfile.deviceId.Equals(localPlayerProfile.deviceId))
            {
                sameDevice = ProfileComparisonResult.SameDevice;
            }

            if (CheckIfNeedToMoveIAP(localPlayerProfile, remotePlayerProfile))
            {
                needMovePurchase = ProfileComparisonResult.MovePurchases;
            }

            if (localPlayerProfile.IsEmpty())
            {
                Debug.Log("Local profile is empty");
                useLocalSaves = 0;
            }
            else if (remotePlayerProfile.IsEmpty())
            {
                Debug.Log("Remote profile is empty");
                useLocalSaves = ProfileComparisonResult.UseLocalProfile;
            }
            else if (remotePlayerProfile.creationTime != localPlayerProfile.creationTime)
            {
                //Profiles created at different time. Merge conflict is inevitable. 
                var result = await GameManager.UIManager.ShowSyncPopUp("Conflict saves detected on server",
                    "Welcome back!\n We found your saves on the server. Do you want to overwrite your local changes with server saves? Pressing 'No' will overwrite server saves with your current local saves.\n" +
                    " Last server update device:\n " + remotePlayerProfile.deviceModel, enableMergeCancel, localPlayerProfile.Clone(),remotePlayerProfile.Clone()).WaitForDialogCloseAsync();

                if (result.Equals(UIManager.ButtonCloseId))
                {
                    return ProfileComparisonResult.Cancel;
                }

                if (result.Equals(SyncPopUpDialog.LocalProfileChooseId))
                {
                    useLocalSaves = ProfileComparisonResult.UseLocalProfile;
                }
                else
                {
                    useLocalSaves = 0;
                }
            }
            //Different device made changes in the remote storage. 
            else if (sameDevice == 0)
            {
                var result = await GameManager.UIManager.ShowSyncPopUp("Old saves detected on server",
                    "We found your new saves on the server that conflict with your offline progress. Do you want to overwrite your local changes with server saves? Pressing 'No' will overwrite server saves with your current local saves.\n" +
                    " Last server update device:\n " + remotePlayerProfile.deviceModel, enableMergeCancel, localPlayerProfile.Clone(), remotePlayerProfile.Clone()).WaitForDialogCloseAsync();

                if (result.Equals(UIManager.ButtonCloseId))
                {
                    return ProfileComparisonResult.Cancel;
                }

                if (result.Equals(SyncPopUpDialog.LocalProfileChooseId))
                {
                    useLocalSaves = ProfileComparisonResult.UseLocalProfile;
                }
                else
                {
                    useLocalSaves = 0;
                }

            }
            //Last save in remote storage was made by the same device.
            else
            {
                //Local profile has fresh updates
                if (remotePlayerProfile.updateTime < localPlayerProfile.updateTime)
                {
                    Debug.Log("local profile is fresh");

                    useLocalSaves = ProfileComparisonResult.UseLocalProfile;
                    needMovePurchase = 0;
                }
                //2 saves made on the same device by the same user at the same time. Profiles are identical
                else if (remotePlayerProfile.updateTime == localPlayerProfile.updateTime)
                {
                    return ProfileComparisonResult.IdenticalProfiles;
                }
                //Server has fresh updates made by the same device or somehow in different way (impossible case for normal usage. Can be caused by using backup saves).
                else
                {
                    useLocalSaves = 0;
                    needMovePurchase = 0;
                }
            }

            if (needMovePurchase != 0)
            {
                await GameManager.UIManager.ShowOkDialog("Purchases transfer", "We have detected purchases made on the old profile\n They will be transferred to recent profiler.").WaitForDialogCloseAsync();
            }

            return useLocalSaves | sameDevice | needMovePurchase;
        }

        /// <summary>
        /// Checks if IAP transfer will be needed in any direction.
        /// </summary>
        /// <param name="profile1"></param>
        /// <param name="profile2"></param>
        /// <returns></returns>
        private bool CheckIfNeedToMoveIAP(PlayerProfile profile1, PlayerProfile profile2)
        {
            if ((profile1.payments == null || profile1.payments.Count == 0) && (profile2.payments == null || profile2.payments.Count == 0))
                return false;

            //Checking IAP intersections.
            if (profile1.payments != null && profile2.payments != null)
            {
                foreach (var profile1Payment in profile1.payments)
                {
                    foreach (var profile2Payment in profile2.payments)
                    {
                        if (profile1Payment.iaId == profile2Payment.iaId)
                        {
                            Debug.LogWarning("Found intersection in IAP: " + profile1Payment.iaId);
                            return false;
                        }
                    }
                }
            }

            return true;

        }

        /// <summary>
        /// Saves profile on device.
        /// </summary>
        /// <param name="saveRemote"> If true also tries to sync profile with storage profile.</param>
        /// <returns>Returns Failure if local storage fails to save profile, over wise returns Success</returns>
        public async Task<PlayerProfileStorage.OperationResult> SaveProfileAsync(bool saveRemote = true)
        {
            Debug.Log("Doing save");

            IsLocalProfileSyncedInCloud = false;

            localStorage.playerProfile.Touch();

            if (await localStorage.SaveAsync() == PlayerProfileStorage.OperationResult.Failure)
            {
                return PlayerProfileStorage.OperationResult.Failure;
            }

            if (saveRemote)
            {
                await LoadAndSyncProfiles();

            }

            Debug.Log("Save done successfully");

            return PlayerProfileStorage.OperationResult.Success;
        }

        /// <summary>
        /// Deletes local profile and creates empty one. 
        /// </summary>
        /// <returns></returns>
        public async Task<PlayerProfileStorage.OperationResult> ClearLocalProfileAsync()
        {
            localStorage.playerProfile = new PlayerProfile();
            localStorage.playerProfile.Touch();

            return await localStorage.SaveAsync();
        }

        /// <summary>
        /// Moves purchases from donorProfile into recipientProfile.
        /// Makes sure all purchases exist only in one instance to avoid purchases multiplication and cloning between profiles.
        /// </summary>
        /// <param name="donorProfile">Profile to move purchases FROM.</param>
        /// <param name="recipientProfile">Profile to move purchases INTO.</param>
        private void MovePurchases(PlayerProfile donorProfile, PlayerProfile recipientProfile)
        {
            Debug.Log("Moving purchases");

            //No payments were done in donor profile. No merge needed.
            if (donorProfile.payments == null || donorProfile.payments.Count == 0)
            {
                return;
            }

            //No payments were done in recepient profile. Copying all payments plus best resources.
            if (recipientProfile.payments == null || recipientProfile.payments.Count == 0)
            {
                recipientProfile.payments = donorProfile.payments;

                recipientProfile.crystalCounter = Math.Max(donorProfile.crystalCounter, recipientProfile.crystalCounter);
                recipientProfile.goldCounter = Math.Max(donorProfile.goldCounter, recipientProfile.goldCounter);
                recipientProfile.kittyBankCrystals = Math.Max(donorProfile.kittyBankCrystals, recipientProfile.kittyBankCrystals);
                recipientProfile.kittyBankGold = Math.Max(donorProfile.kittyBankGold, recipientProfile.kittyBankGold);

                recipientProfile.energyCounter = Math.Max(recipientProfile.energyCounter, donorProfile.energyCounter);
                recipientProfile.energyUnlimEndTime = Math.Max(recipientProfile.energyUnlimEndTime, donorProfile.energyUnlimEndTime);
                recipientProfile.energyNextTopupTime = Math.Min(recipientProfile.energyNextTopupTime, donorProfile.energyNextTopupTime);

                if (recipientProfile.powerUps == null)
                {
                    recipientProfile.powerUps = donorProfile.powerUps;
                }
                else
                {
                    foreach (var booster in donorProfile.boosters)
                    {
                        bool boosterExists = false;

                        foreach (var recipientProfileBooster in recipientProfile.boosters)
                        {
                            if (booster.id == recipientProfileBooster.id)
                            {
                                recipientProfileBooster.count = Math.Max(booster.count, recipientProfileBooster.count);

                                boosterExists = true;

                                break;
                            }
                        }

                        if (!boosterExists)
                        {
                            recipientProfile.boosters.Add(booster);
                        }
                    }
                }

                if (recipientProfile.boosters == null)
                {
                    recipientProfile.boosters = donorProfile.boosters;
                }
                else
                {
                    foreach (var powerUp in donorProfile.powerUps)
                    {
                        bool powerUpExists = false;

                        foreach (var recipientProfilePowerUp in recipientProfile.powerUps)
                        {
                            if (powerUp.id == recipientProfilePowerUp.id)
                            {
                                recipientProfilePowerUp.count += powerUp.count;

                                powerUpExists = true;

                                break;
                            }
                        }

                        if (!powerUpExists)
                        {
                            recipientProfile.powerUps.Add(powerUp);
                        }
                    }
                }

                return;
            }

            if (recipientProfile.payments != null && (donorProfile.payments != null && (donorProfile.payments.Count > 0 && recipientProfile.payments.Count > 0)))
            {
                foreach (var payment in donorProfile.payments)
                {
                    recipientProfile.payments.Add(payment);
                }

                donorProfile.payments.Clear();

                recipientProfile.crystalCounter += donorProfile.crystalCounter;
                recipientProfile.goldCounter += donorProfile.goldCounter;
                recipientProfile.kittyBankCrystals += donorProfile.kittyBankCrystals;
                recipientProfile.kittyBankGold += donorProfile.kittyBankGold;

                recipientProfile.energyCounter = Math.Max(recipientProfile.energyCounter, donorProfile.energyCounter);
                recipientProfile.energyUnlimEndTime = Math.Max(recipientProfile.energyUnlimEndTime, donorProfile.energyUnlimEndTime);
                recipientProfile.energyNextTopupTime = Math.Min(recipientProfile.energyNextTopupTime, donorProfile.energyNextTopupTime);

                //Copying boosters
                foreach (var booster in donorProfile.boosters)
                {
                    bool boosterExists = false;

                    foreach (var recipientProfileBooster in recipientProfile.boosters)
                    {
                        if (booster.id == recipientProfileBooster.id)
                        {
                            recipientProfileBooster.count += booster.count;

                            boosterExists = true;

                            break;
                        }
                    }

                    if (!boosterExists)
                    {
                        recipientProfile.boosters.Add(booster);
                    }
                }

                //Copying powerups
                foreach (var powerUp in donorProfile.powerUps)
                {
                    bool powerUpExists = false;

                    foreach (var recipientProfilePowerUp in recipientProfile.powerUps)
                    {
                        if (powerUp.id == recipientProfilePowerUp.id)
                        {
                            recipientProfilePowerUp.count += powerUp.count;

                            powerUpExists = true;

                            break;
                        }
                    }

                    if (!powerUpExists)
                    {
                        recipientProfile.powerUps.Add(powerUp);
                    }
                }
            }
        }

        /// <summary>
        /// Returns the highest score the player achieved at the specified level.
        /// </summary>
        /// <param name="levelIndex"></param>
        /// <returns>The highest score the player achieved at the specified level or 0 if the level hasn't been played yet.</returns>
        public int GetLevelHighScore(LevelIndex levelIndex)
        {
            if (profile.levelSaveData.TryGetValue(levelIndex, out LevelSaveData levelData))
            {
                return levelData.maxScore;
            }

            return 0;
        }

        /// <summary>
        /// Checks whether the a specified level is completed (played at least once and one or more stars is earned).
        /// </summary>
        /// <param name="levelIndex"></param>
        /// <returns>True if completed, flase otherwise.</returns>
        public bool IsLevelCompleted(LevelIndex levelIndex)
        {
            if (profile.levelSaveData.TryGetValue(levelIndex, out LevelSaveData levelData))
            {
                return levelData.maxStarsEarned > 0;
            }

            return false;
        }

        /// <summary>
        /// Returns deep-cloned copy of an actual LevelSaveData for a specified level.
        /// The result is intended for read-only purposes and changes to the returned object won't affect player profile.
        /// To modify LevelSaveData access it directly via profile access.
        /// </summary>
        /// <param name="levelIndex">LevelIndex of a level to return LevelSaveData for.</param>
        /// <returns>LevelSaveData clone or null if level isn't played yet.</returns>
        public LevelSaveData GetLevelSaveDataCopy(LevelIndex levelIndex)
        {
            if (profile.levelSaveData.TryGetValue(levelIndex, out LevelSaveData levelData))
            {
                return levelData.Clone();
            }

            return null;
        }

        /// <summary>
        /// Checks whether current player profile is empty and can be replaced or deleted without player confirmation.
        /// Profile is considered empty if its playTime is zero (no levels were played) and no purchases were made.
        /// </summary>
        /// <returns>True if current profile is considered empty.</returns>
        public bool IsPlayerProfileEmpty()
        {
            return profile.IsEmpty();
        }

#region Game session methods

        /// <summary>
        /// Starts game level play session.
        /// Must be followed by a call of <see cref="FinishGameLevelSession(LevelResults)"/>.
        /// </summary>
        /// <param name="levelIndex"></param>
        public void StartGameLevelSession(LevelIndex levelIndex)
        {
#if UNITY_EDITOR
            if (currentGameLevel != null)
            {
                throw new  Exception($"The play session for the game level \"{currentGameLevel}\" wasn't finished correctly. " +
                    "Can't start a new session for the level \"{levelIndex}\".");
            }
#endif
            gameLevelSessionStartTime = DateTimeTools.GetUtcTimestampNow();
            currentGameLevel = levelIndex;

            // First game session.
            if (profile.levelSaveData == null)
            {
                profile.levelSaveData = new Dictionary<LevelIndex, LevelSaveData>();
            }

            // Retrieve existing LevelSaveData object or create a new one if none exists.
            if (!profile.levelSaveData.TryGetValue(currentGameLevel, out LevelSaveData currentLevelData))
            {
                currentLevelData = new LevelSaveData(currentGameLevel);
                profile.levelSaveData.Add(currentGameLevel, currentLevelData);
            }

            LevelResults.Init(currentLevelData);
        }

        public bool IsGameLevelSessionStarted()
        {
            return currentGameLevel != null;
        }

        /// <summary>
        /// Finishes game level play session started by a call of <see cref="StartGameLevelSession(LevelIndex)"/>, updates player profile and forces its save.
        /// Must be called when the player finishes a game level whether they won it or loose.
        /// </summary>
        /// <param name="results">Level completion results.</param>
        public async void FinishGameLevelSession(bool ignoreResults = false)
        {
            if (currentGameLevel == null)
            {
                throw new Exception($"The game level play session for hasn't been started.");
            }

            long sessionLength = 0;

            if (!ignoreResults)
            {
                if (!LevelResults.isLevelFinished)
                {
                    throw new Exception($"The game level play session can't be finished before LevelResults.isLevelFinished == true.");
                }

                // Retrieve LevelSaveData.
                if (profile.levelSaveData == null || !profile.levelSaveData.TryGetValue(currentGameLevel, out LevelSaveData currentLevelData))
                {
                    throw new Exception($"LevelSaveData for level \"{currentGameLevel}\" doesn't exist.");
                }

                sessionLength = DateTimeTools.GetUtcTimestampNow() - gameLevelSessionStartTime;
                if (sessionLength > 24 * 60 * 60)
                {
                    throw new Exception("The game level play session is invalid.");
                }

                // Apply level results to the level data from LevelResults static class.
                currentLevelData.ApplyLevelResults((int)sessionLength);
            }

            // Cleaning up
            gameLevelSessionStartTime = 0;
            currentGameLevel = null;

            if (!ignoreResults)
            {
                // Saving player profile.

                // No problem should be here with overflow since Int32 should cover any imaginable session duration.
                profile.LogPlayTime((int)sessionLength);

                if (await SaveProfileAsync() == PlayerProfileStorage.OperationResult.Failure)
                {
                    // TODO: find a way to display here an error dialog.
                    Debug.LogError("Failed to save profile.");
                }
            }

            // Deinitializing LevelResults. This is crucial.
            LevelResults.Deinit();
        }

#endregion
    }
}