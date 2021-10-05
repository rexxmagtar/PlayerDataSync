using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

namespace com.pigsels.BubbleTrouble
{
    /// <summary>
    /// Keeps local copy of a PlayerProfile and manages its save and load.
    /// </summary>
    public class PlayerProfileStorageLocal : PlayerProfileStorage
    {
        /// <summary>
        /// Loads current PlayerProfile from local file system.
        /// </summary>
        public override async Task<OperationResult> LoadAsync()
        {
            Debug.Log($"Local storage save path: {Application.persistentDataPath}");

            string savesPath = Application.persistentDataPath + "/" + PlayerProfilePath;

            if (!File.Exists(savesPath))
            {
                isLoaded = false;
                return OperationResult.EmptyResult;
            }

            try
            {
                byte[] profile = File.ReadAllBytes(savesPath);

                playerProfile = DeserializeProfile(profile);

#if DEBUG
                // Make sure the profile has no empty (uninitialized) values.
                // This might happen if PlayerProfile class is changed but its version is left intact and no migration is implemented.
                if (!await ValidateProfile(playerProfile, "lastSolvedConflictProfileHash", "cloudId"))
                {
                    playerProfile = new PlayerProfile();
                }
#endif
            }
            catch (Exception e)
            {
                Debug.Log(e);

                return OperationResult.Failure;
            }

            isLoaded = true;

            await Task.Yield();

            return OperationResult.Success;
        }

        /// <summary>
        /// Saves current PlayerProfile to local file system.
        /// </summary>
        public override async Task<OperationResult> SaveAsync()
        {
            Debug.Assert(isLoaded);
            if (!isLoaded) return OperationResult.Failure;

            string savesPath = Application.persistentDataPath + "/" + PlayerProfilePath;
            string tempSavePath = savesPath + ".temp";

            try
            {
                byte[] profileSerialized = SerializeProfile(playerProfile);

                FileStream saveTempFile = File.Open(tempSavePath, FileMode.Create);

                BinaryWriter writerTemp = new BinaryWriter(saveTempFile);

                writerTemp.Write(profileSerialized);

                writerTemp.Flush();

                writerTemp.Close();

                if (File.Exists(savesPath))
                {
                    File.Delete(savesPath);
                }

                File.Move(tempSavePath, savesPath);


            }
            catch (Exception e)
            {
                Debug.LogError(e);
                return OperationResult.Failure;
            }


            await Task.Yield();

            return OperationResult.Success;
        }
    }
}