using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Firebase.Auth;
using Firebase.Database;
using Facebook.Unity;
using HttpMethod = Facebook.Unity.HttpMethod;
using com.pigsels.tools;

namespace com.pigsels.BubbleTrouble
{
    public static class PlayerAuth
    {
        public static bool IsReady { get; private set; }

        /// <summary>
        /// Firebase Facebook auth provider id
        /// </summary>
        public const string FacebookProviderId = "facebook.com";

        /// <summary>
        /// Folder to store current session token
        /// </summary>
        public const string DevicesLogsInRoot = "userLogsIn";

        /// <summary>
        /// Stores reference to session token
        /// </summary>
        private static DatabaseReference LoginReference;

        /// <summary>
        /// Determines if user's Facebook token is valid. Updates when the app starts.
        /// </summary>
        public static bool IsFacebookTokenValid = false;

        /// <summary>
        /// Permissions needed for Facebook login. 
        /// </summary>
        private static string[] FacebookPermissions = new[] {"public_profile", "email"};

        public delegate void NewUserLoggedInHandler();
        public static event NewUserLoggedInHandler NewUserLoggedIn;


#if UNITY_EDITOR
        /// <summary>
        /// Name of he EditorPrefs key for storing debug Facebook auth token.
        /// </summary>
        public static string FBTokenPrefKey => EditorPrefsHelper.GetEditorPrefsKey(typeof(OdinDependeciesDLL), "FBDebugToken");
#endif

        /// <summary>
        /// File name of the TextAsset containing debug Facebook auth token.
        /// </summary>
        public static string FBTokenAssetName => typeof(PlayerAuth).FullName + ".FBDebugToken";


        public static async Task<bool> Init()
        {
            if (IsReady)
            {
                Debug.LogWarning("PlayerAuth has been already initialized.");
                return true;
            }

            FirebaseAuth.DefaultInstance.StateChanged += OnAuthStateChanged;

#if UNITY_IOS || UNITY_ANDROID || UNITY_EDITOR
            // Facebook API doesn't support MacOSX and Windows standalone.
            // So we avoid initializing FB API here to allow the standalone builds run in MacOS and Windows for testing purposes without FB support.

            if (!FB.IsInitialized)
            {
                FB.Init(onInitComplete);
            }

            async void onInitComplete()
            {
                FB.ActivateApp();

                if (IsLoggedInWithFacebook())
                {
                    Debug.Log("Logged in with Facebook.");
                    IsFacebookTokenValid = await IsFbTokenValidAsync(GetFacebookAuthToken());
                    Debug.Log($"The Facebook token {GetFacebookAuthToken()} is " + (IsFacebookTokenValid ? "valid" : "INVALID") + ".");
                }

                IsReady = true;
            }

#else
            IsReady = true;
#endif

            while (!IsReady)
            {
                await Task.Yield();
            }

            return true;
        }

        private static void OnAuthStateChanged(object sender, EventArgs e)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Auth event event still got caught. Unsubscribing subscriber method from this event");
                FirebaseAuth.DefaultInstance.StateChanged -= OnAuthStateChanged;
                return;
            }
#endif
            if (FirebaseAuth.DefaultInstance.CurrentUser != null)
            {
                ResetSessionConfiguration();
            }
        }

        //TODO: change this method's name
        /// <summary>
        /// Changes session token storage location based on current logged in user
        /// </summary>
        private static void ResetSessionConfiguration()
        {
            //Debug.Log("Reseting session configuration");

            if (LoginReference != null)
            {
                LoginReference.ValueChanged -= PlayerAuth_ValueChanged;
            }

            LoginReference = FirebaseDatabase.DefaultInstance.GetReference(DevicesLogsInRoot).Child(GetUserId());

            LoginReference.ValueChanged += PlayerAuth_ValueChanged;

        }

        /// <summary>
        /// Updates current game session token so all other devices that use same profile at this time will get kicked off from there sessions.
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> UpdateSessionAsync()
        {
            //No remote profile - no problems :~)
            if (FirebaseAuth.DefaultInstance.CurrentUser == null)
            {
                return true;
            }

            var task = LoginReference.SetValueAsync(SystemInfo.deviceUniqueIdentifier).ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    Debug.Log("Failed to update session token: " + t.Exception.Message);
                }
                else
                {
                    Debug.Log("Session token updated successfully.");
                }
            });

            //TODO: decide if should await this operation to end here
            //await task;

            Debug.Log("Requested to update session token using current device id. Id: " + SystemInfo.deviceUniqueIdentifier);

            await Task.Yield();

            return true;
        }

        private static void PlayerAuth_ValueChanged(object sender, ValueChangedEventArgs e)
        {

            //TODO: decide where to do such deinit stuff. Smth like OnApplicationQuit method should be inplemented
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Realtime database event still got caught. Unsubscribing subscribed method from this event");
                LoginReference.ValueChanged -= PlayerAuth_ValueChanged;
                return;
            }
#endif

            if (e.DatabaseError != null)
            {
                Debug.Log(e.DatabaseError.Message);
            }
            else
            {
                Debug.Log("New device logged in: " + e.Snapshot.Value);
                Debug.Log("Current logged in device: " + SystemInfo.deviceUniqueIdentifier);

                if (e.Snapshot.Value!= null && !e.Snapshot.Value.Equals(SystemInfo.deviceUniqueIdentifier))
                {
                    Debug.Log("Invoking new device log event");
                    NewUserLoggedIn?.Invoke();

                }
            }
        }

        ////TODO: remove this option from the project
        //public static async Task<bool> AuthorizeAnonymous()
        //{
        //    Task<FirebaseUser> task = FirebaseAuth.DefaultInstance.SignInAnonymouslyAsync();

        //    await task;

        //    if (task.IsFaulted || task.IsCanceled)
        //    {
        //        Debug.Log(task.Exception.ToString());

        //        return false;
        //    }
        //    else
        //    {
        //        Debug.Log("Created anonymous account. User id = " + task.Result.UserId);

        //        return true;
        //    }
        //}

        /// <summary>
        /// Logs in using Facebook authorization:
        ///  - with FB auth token saved in EditorPrefs if in Editor mode;
        ///  - with FB auth token saved in Resources as TEstAsset if in Development build;
        ///  - with normal FB auth otherwise.
        /// </summary>
        /// <returns></returns>
        public static async Task<bool> LoginWithFacebook()
        {

#if UNITY_EDITOR

            // Using Faceboook auth token saved in EditorPrefs.

            string fbToken = GetFacebookAuthToken();
            if (string.IsNullOrEmpty(fbToken))
            {
                Debug.LogError("No Facebook auth debug token was saved in EditorPrefs. Use \"Project actions -> Edit Facebook debug token\" menu command to edit the token.");
                return false;
            }

            Debug.Log("Using Facebook debug auth token from EditorPrefs: " + fbToken);

            IsFacebookTokenValid = await IsFbTokenValidAsync(fbToken);

            if (IsFacebookTokenValid)
            {
                Debug.Log("Facebook auth debug token is valid.");
            }
            else
            {
                Debug.LogWarning("Facebook auth debug token has EXPIRED or is INVAILD.");
            }

            return await LogInUsingFacebookToken(fbToken);

#elif DEVELOPMENT_BUILD
            // Using Faceboook auth token saved as TextAsset.

            string fbToken = GetFacebookAuthToken();
            if (string.IsNullOrEmpty(fbToken))
            {
                Debug.LogError("No Facebook debug auth token was saved in Resources or or the token is empty. Use \"Project actions -> Edit Facebook debug token\" menu command to edit the token.");
                return false;
            }

            IsFacebookTokenValid = await IsFbTokenValidAsync(fbToken);
            return await LogInUsingFacebookToken(fbToken);

#else
            // Logging in with Facebook normally.

            ILoginResult result = null;

            FB.LogInWithReadPermissions(FacebookPermissions, resultLogin =>
            {
                result = resultLogin;
            });

            while (result == null)
            {
                await Task.Yield();
            }

            if (result.Cancelled)
            {
                Debug.Log("User canceled log in");
                return false;
            }

            if (result.Error != null)
            {
                Debug.Log("Failed to login in:\n" + result.Error);
                return false;
            }

            Debug.Log("Got Facebook user token. Permissions:");

            foreach (var permission in result.AccessToken.Permissions)
            {
                Debug.Log(permission);
            }

            IsFacebookTokenValid = await IsFbTokenValidAsync(result.AccessToken.TokenString);

            return await LogInUsingFacebookToken(result.AccessToken.TokenString);
#endif
        }

        /// <summary>
        /// Logins using facebook access token.
        /// </summary>
        /// <param name="facebookToken"></param>
        /// <returns></returns>
        private static async Task<bool> LogInUsingFacebookToken(string facebookToken)
        {
            Debug.Log($"Trying to log using facebook token {{{facebookToken}}}");

            Credential fbCredential = Firebase.Auth.FacebookAuthProvider.GetCredential(facebookToken);

            bool success = false;

            await FirebaseAuth.DefaultInstance.SignInWithCredentialAsync(fbCredential).ContinueWith(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    Debug.Log(task.Exception.ToString());

                    if (FB.IsLoggedIn)
                    {
                        FB.LogOut();
                    }
                }
                else
                {
                    Debug.Log("Logged in account using Facebook. UserId = " + task.Result.UserId);
                    success = true;
                }
            });

            return success;
        }

        /// <summary>
        /// Checks if current logged user has Facebook credentials.
        /// </summary>
        /// <returns></returns>
        public static bool IsLoggedInWithFacebook()
        {
            if (FirebaseAuth.DefaultInstance.CurrentUser == null)
            {
                return false;
            }
            return FirebaseAuth.DefaultInstance.CurrentUser.ProviderData.Any(info => info.ProviderId.Equals(FacebookProviderId));
        }

        /// <summary>
        /// Get's  current logged in user's id.
        /// </summary>
        /// <returns>id string if user is logged in, null overwise</returns>
        public static string GetUserId()
        {
            FirebaseUser user = FirebaseAuth.DefaultInstance.CurrentUser;

            if (user == null)
            {
                return null;
            }
            else
            {
                return FirebaseAuth.DefaultInstance.CurrentUser.UserId;
            }

        }

        /// <summary>
        /// Checks if current user has logged in Firebase for the first time.
        /// </summary>
        /// <returns>True if is called during first user's session. If there is no session or the session is not the first - return false</returns>
        public static bool IsProfileNew()
        {
            FirebaseUser user = FirebaseAuth.DefaultInstance.CurrentUser;

            return user != null
                   && user.Metadata.CreationTimestamp == user.Metadata.LastSignInTimestamp;
        }

        /// <summary>
        /// Gets Facebook token saved on device.
        /// </summary>
        /// <returns>
        /// Facebook token from EditorPrefs if is called in Unity editor.
        /// Facebook token from Resources if is called in Development build (automatically saved in Resources as TextAsset when building the app).
        /// Facebook token from Facebook SDK if is called in Release build.
        /// </returns>
        private static string GetFacebookAuthToken()
        {
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetString(FBTokenPrefKey);
#elif DEVELOPMENT_BUILD
            // Make sure no trailing spaces left.
            return Resources.Load<TextAsset>(FBTokenAssetName)?.text.Trim();
#else
            return AccessToken.CurrentAccessToken.TokenString;
#endif
        }

        /// <summary>
        /// Checks if provided token is valid using Graph API.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public static async Task<bool> IsFbTokenValidAsync(string token)
        {
            bool finished = false;

            string responseStr = "";

            IDictionary<string, object> response = null;

            FB.API("/me", HttpMethod.GET, result =>
            {
                finished = true;

                responseStr = result.RawResult;

                response = result.ResultDictionary;

            }, new Dictionary<string, string> {{"access_token", token}});

            while (!finished)
            {
                await Task.Yield();
            }

            Debug.Log("Result token validation response: " + responseStr);


            return !response.ContainsKey("error");
        }

        //TODO: move this method to another class cause profile photo has nothing in common with authorization.
        /// <summary>
        /// Gets texture of user's profile photo. 
        /// </summary>
        public static async Task<Texture2D> GetUserPhotoAsync()
        {
            if (GetUserId() == null)
            {
                throw new Exception("Local user cannot have profile photo.");
            }

            // string photoUrl = await GetUserProfilePhotoURLAsync();
            //
            // UnityWebRequest photoRequest = UnityWebRequestTexture.GetTexture(photoUrl);
            //
            // photoRequest.SendWebRequest();
            //
            // while (!photoRequest.isDone && !photoRequest.isNetworkError)
            // {
            //     await Task.Yield();
            // }
            //
            // if (photoRequest.isNetworkError)
            // {
            //     Debug.Log("Error loading profile photo: \n" + photoRequest.error);
            //
            //     return null;
            // }
            //
            // Texture2D photoTexture2D = DownloadHandlerTexture.GetContent(photoRequest);

            bool finished = false;

            string responseStr = "";

            Texture2D resultTexture2D = Texture2D.redTexture;

            //IDictionary<string, object> response = null;

            FB.API("/me/picture", HttpMethod.GET, result =>
            {
                resultTexture2D = result.Texture;

                finished = true;

            }, new Dictionary<string, string> {{"access_token", GetFacebookAuthToken()}});

            while (!finished)
            {
                await Task.Yield();
            }

            Debug.Log("Result response: " + responseStr);

            return resultTexture2D;
        }

        public static async Task<string> GetUserProfilePhotoURLAsync()
        {
            bool finished = false;

            string responseStr = "";

            IDictionary<string, object> response = null;

            FB.API("/me/picture", HttpMethod.GET, result =>
            {
                finished = true;

                responseStr = result.RawResult;

                response = result.ResultDictionary;

            }, new Dictionary<string, string> {{"access_token", GetFacebookAuthToken()}});

            while (!finished)
            {
                await Task.Yield();
            }

            Debug.Log("Result response: " + responseStr);

            return ((IDictionary)(response["data"]))["url"] as string;
        }

        public static string GetUserName()
        {
            return FirebaseAuth.DefaultInstance.CurrentUser?.DisplayName;
        }

        /// <summary>
        /// Loges out current user.
        /// </summary>
        public static void Logout()
        {
            FirebaseAuth.DefaultInstance.SignOut();

            if (FB.IsLoggedIn)
            {
                FB.LogOut();
            }
        }

        private static void FBInitCallback()
        {
            // AccessToken class will have session details
            FB.ActivateApp();

        }
    }
}