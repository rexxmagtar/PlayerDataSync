using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Firebase.Storage;
using UnityEngine;

namespace com.pigsels.BubbleTrouble
{
    /// <summary>
    /// Keeps remote copy of a PlayerProfile t Firebase and manages its save and load.
    /// </summary>
    public class PlayerProfileStorageFirebase : PlayerProfileStorage
    {
        /// <summary>
        /// Name of the folder of users saves in firebase storage.
        /// </summary>
        private const string UsersDataFolder = "usersData";

        /// <summary>
        /// Loads current PlayerProfile from Firebase storage.
        /// </summary>
        public override async Task<OperationResult> LoadAsync()
        {
            string id = PlayerAuth.GetUserId();

            //No sync is needed for non authorized user
            if (id == null)
            {
                isLoaded = false;
                return OperationResult.Failure;
            }

            StorageReference saveRef = FirebaseStorage.DefaultInstance.RootReference.Child(UsersDataFolder).Child(id).Child(PlayerProfilePath);

            try
            {
                var taskUrl = saveRef.GetDownloadUrlAsync();
                await taskUrl;
            }
            catch (StorageException e)
            {
                isLoaded = false;

                Debug.Log($"Failed to get download url: {e.ToString()}");

                int errorCode = e.ErrorCode;

                Debug.Log("Error " + errorCode);

                //Check if object did not exist in remote storage.
                if (errorCode == StorageException.ErrorObjectNotFound)
                {
                    Debug.Log("Empty remote storage.");
                    return OperationResult.EmptyResult;
                }

                //This was internet connection issue.
                return OperationResult.Failure;
            }
            //For some reason Firebase throws an ApplicationException some times when its API is called without internet connection.
            catch (ApplicationException e)
            {

                Debug.Log("Application exception: " + e.Message);
            }

            Debug.Log("Remote storage save path: " + saveRef);

            Task<Stream> task;

            try
            {
                task = saveRef.GetStreamAsync();
                await task;

                var memStream = new MemoryStream();

                await task.Result.CopyToAsync(memStream);

                byte[] resultBytes = memStream.ToArray();

                playerProfile = DeserializeProfile(resultBytes);

#if DEBUG
                // Make sure the profile has no empty (uninitialized) values.
                // This might happen if PlayerProfile class is changed but its version is left intact and no migration is implemented.
                if (!await ValidateProfile(playerProfile, "lastSolvedConflictProfileHash", "cloudId"))
                {
                    playerProfile = new PlayerProfile();
                }
#endif
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.ToString());

                return OperationResult.Failure;
            }


            isLoaded = true;

            return OperationResult.Success;
        }

        /// <summary>
        /// Saves current PlayerProfile to Firebase storage.
        /// </summary>
        public override async Task<OperationResult> SaveAsync()
        {
            return await LoadToStorage(playerProfile, PlayerProfilePath);
        }

        public override async Task<OperationResult> BackupAsync(PlayerProfile profile)
        {
            Debug.Log("Making profile backup.");
            return await LoadToStorage(profile, PlayerProfileBackupPath);
        }

        private async Task<OperationResult> LoadToStorage(PlayerProfile profile, string fileName)
        {
            Debug.Assert(isLoaded);
            if (!isLoaded) return OperationResult.Failure;

            string id = PlayerAuth.GetUserId();

            if (id == null)
            {
                return OperationResult.Failure;
            }

            StorageReference saveRef = FirebaseStorage.DefaultInstance.RootReference.Child(UsersDataFolder).Child(id).Child(fileName);

            try
            {

                byte[] json = SerializeProfile(profile);

                Task<StorageMetadata> result = saveRef.PutBytesAsync(json);

                await result;

            }
            catch (Exception ex)
            {
                Debug.Log(ex.ToString());

                return OperationResult.Failure;
            }

            return OperationResult.Success;
        }
    }
}