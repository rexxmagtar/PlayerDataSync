using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using com.pigsels.tools;
using OdinSerializer;
using UnityEngine;

namespace com.pigsels.BubbleTrouble
{
    /// <summary>
    /// Keeps copy of a PlayerProfile and manages its save and load.
    /// Must be derived to implement local and remote storages.
    /// </summary>
    public abstract class PlayerProfileStorage
    {
        protected const string PlayerProfilePath = "playerProfile";
        protected const string PlayerProfileBackupPath = "playerProfileBackup";

        /// <summary>
        /// Actual player profile version. Must always be equal to the version of the latest IPlayerProfile implementation.
        /// </summary>
        protected const int CurrentPlayerProfileVersion = 2;

        public enum OperationResult
        {
            Success, // operation successfull
            Failure, // operation failed
            EmptyResult // operation successfull but the result is empty
        }

        /// <summary>
        /// Loaded player profile object.
        /// </summary>
        protected PlayerProfile _playerProfile;

        /// <summary>
        /// Is profile loaded and ready to be accessed.
        /// </summary>
        public bool isLoaded { get; protected set; }


        /// <summary>
        /// Loads current PlayerProfile.
        /// </summary>
        public virtual async Task<OperationResult> LoadAsync()
        {
            await Task.Yield();
            return OperationResult.Failure;
        }

        /// <summary>
        /// Saves current PlayerProfile.
        /// </summary>
        public virtual async Task<OperationResult> SaveAsync()
        {
            await Task.Yield();
            return OperationResult.Failure;
        }

        /// <summary>
        /// Retrieve loaded player profile.
        /// Throws an Exception if no profile is loaded.
        /// </summary>
        public virtual PlayerProfile playerProfile
        {
            get
            {
                if (!isLoaded)
                {
                    throw new Exception("The player profile is not loaded yet.");
                }
                return _playerProfile;
            }

            set
            {
                isLoaded = value != null;
                _playerProfile = value;
            }
        }

        /// <summary>
        /// Serializes a PlayerProfile.
        /// </summary>
        /// <param name="playerProfile">Profile to serialize.</param>
        /// <param name="zip">Zip json file</param>
        /// <returns>Serialized profile data.</returns>
        protected static byte[] SerializeProfile(PlayerProfile playerProfile, bool zip = true)
        {
            //Getting instance of current PlayerProfile parent class version.

            //Copying content from saved profile to profile to serialize.
            object playerProfMigratable = Activator.CreateInstance(playerProfile.GetType().BaseType);

            MapClassFields(playerProfile, playerProfMigratable);

            byte[] serializedProf = SerializationUtility.SerializeValue(playerProfMigratable, DataFormat.JSON);

            Debug.Log("Saving JSON: " + Encoding.UTF8.GetString(serializedProf));

            List<byte> resultBytes = new List<byte>();

            byte[] hash = GetHash(serializedProf);

            Debug.Log("Calculating hash for profile: " + playerProfile.ToString());
            Debug.Log("Calculated hash for serialization: " + BitConverter.ToString(hash));

            byte[] updateTime = BitConverter.GetBytes(playerProfile.updateTime);

            byte[] version = BitConverter.GetBytes(playerProfile.Version);

            resultBytes.AddRange(version);
            resultBytes.AddRange(hash);
            resultBytes.AddRange(updateTime);
            resultBytes.AddRange(serializedProf);

            byte[] resultBytesArray = resultBytes.ToArray();

            if (zip)
            {
                resultBytesArray = DataCompressor.Zip(resultBytesArray);
            }

            return resultBytesArray;
        }

        /// <summary>
        /// Unserializes PlayerProfile previously serialized with call of SerializeProfile().
        /// </summary>
        /// <param name="serializedProfile">Serialized data.</param>
        /// <param name="unzip">Unzip profile before deserialization</param>
        /// <returns>Unserialized PlayerProfile.</returns>
        protected static PlayerProfile DeserializeProfile(byte[] serializedProfile, bool unzip = true)
        {
            //Debug.Log("Deserializing profile. Bytes count = "+serializedProfile.Length);

            if (unzip)
            {
                serializedProfile = DataCompressor.Unzip(serializedProfile);
            }


            byte[] profileVersionArray = new byte[4];

            MemoryStream stream = new MemoryStream(serializedProfile);

            stream.Read(profileVersionArray, 0, 4);

            int profileVersion = BitConverter.ToInt32(profileVersionArray, 0);

            if (profileVersion > CurrentPlayerProfileVersion)
            {
                GameManager.UIManager.ShowIncompatibleProfiledPopUp("INCOMPATIBLE PROFILE VERSION",
                    "Your remote profile has version higher then current app version. Please, Update your app to proceed synchronization")
                    .OnButtonPressed+= (result, hndl) =>
                {
                    if (result == UIManager.ButtonOkId)
                    {
                        PlatformFunctions.OpenStorePage();
                    }
                };

                throw new Exception($"Profile version ({profileVersion}) is higher than current ({CurrentPlayerProfileVersion}).");
            }

            byte[] hashBytes = new byte[16];

            stream.Read(hashBytes, 0, 16);

            string hash = BitConverter.ToString(hashBytes);


            byte[] updateTimeArray = new byte[8];

            stream.Read(updateTimeArray, 0, 8);

            long updateTime = BitConverter.ToInt64(updateTimeArray, 0);

            byte[] profileBytes = new byte[serializedProfile.Length - 28];

            stream.Read(profileBytes, 0, profileBytes.Length);

            //Getting PlayerProfile parent class instance.
            IPlayerProfile playerProfile = SerializationUtility.DeserializeValue<IPlayerProfile>(profileBytes, DataFormat.JSON);

            //if (playerProfile.Version > CurrentPlayerProfileVersion)
            //{
            //    // TODO: show a dialog requesting a player to download the latest app version and after the dialog is closed, appStore/playMarket should be open.
            //    // Decide where and in which way this error should be handled (by which class).
            //    // Use PlatformFunctions.OpenStorePage() at the point of processing.
            //    throw new Exception($"Profile version ({playerProfile.Version}) is higher than current ({CurrentPlayerProfileVersion}). See TODO in code.");
            //}

            //Doing profile migration to current PlayerProfile parent class version.
            while (playerProfile.Version < CurrentPlayerProfileVersion)
            {
                Debug.Log("Migrating PlayerProfile from version " + playerProfile.Version);
                playerProfile = playerProfile.MigrateToNextVersion();
            }

            Debug.Log("PlayerProfile version: " + CurrentPlayerProfileVersion);

            PlayerProfile actualPlayerProfile = new PlayerProfile();

            MapClassFields(playerProfile, actualPlayerProfile);

            actualPlayerProfile.updateTime = updateTime;

            actualPlayerProfile.hash = hash;

            return actualPlayerProfile;
        }

#if DEBUG
        /// <summary>
        /// Checks if profile is valid, by checking it's fields for null values.
        /// </summary>
        /// <param name="playerProfile">Profile to validate</param>
        /// <returns>True if profile has no null value fields or the error was ignored by user manually.
        /// False if profile contained null fields and error was not ignored by user manually.</returns>
        protected static async Task<bool> ValidateProfile(PlayerProfile playerProfile, params string[] ignoreFields)
        {
            //Cause it was said not to use Linque extensions, we convert array to list to use list's helpful search methods ("Contains" for example).
            List<string> ignoreFieldsList = new List<string>(ignoreFields);

            FieldInfo[] fields = playerProfile.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            List<FieldInfo> nullFields = new List<FieldInfo>();

            string nullFieldsNames = "";

            foreach (var field in fields)
            {
                if (field.GetValue(playerProfile) == null && !ignoreFieldsList.Contains(field.Name))
                {
                    Debug.LogWarning($"Profile {playerProfile}: field {field.Name} is null");

                    nullFieldsNames += $"   {field.Name}\n";

                    nullFields.Add(field);
                }
            }

            if (nullFields.Count > 0)
            {
                string message = "The PlayerProfile has some null values. This might be caused by modification of the profile without changing its version. " +
                                 "Do you want to reset the whole profile? Choosing 'No' will ignore this error and continue working with this profile which may cause exceptions to be thrown. " +
                                 $"Null fields: \n {nullFieldsNames}";

                string result = await GameManager.UIManager.ShowYesNoDialog("Invalid profile error", message, false).WaitForDialogCloseAsync();

                if (result.Equals(UIManager.ButtonYesId))
                {
                    return false;
                }

            }

            return true;
        }
#endif

        /// <summary>
        /// Copies all values from one object to another using reflection
        /// </summary>
        /// <param name="from"></param>
        /// <param name="to"></param>
        public static void MapClassFields(object from, object to)
        {
            FieldInfo[] fields = from.GetType().GetFields();

            foreach (var field in fields)
            {
                field.SetValue(to, field.GetValue(from));
            }
        }


        //Saves backup in the storage
        public virtual async Task<OperationResult> BackupAsync(PlayerProfile profile)
        {
            await Task.Yield();
            return OperationResult.Failure;
        }

        public static byte[] GetHash(byte[] data)
        {
            MD5 md5 = MD5.Create();

            return md5.ComputeHash(data);
        }
    }
}