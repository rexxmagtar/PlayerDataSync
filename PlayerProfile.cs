using System.Reflection;
using com.pigsels.BubbleTrouble;
using com.pigsels.tools;
using UnityEngine;

/// <summary>
/// Actual (the latest) version of PlayerProfile.
/// All older versions of the PlayerProfileV{x} class are used for migration purpose.
/// </summary>
public class PlayerProfile : PlayerProfileV2
{
    //public class PowerUp : PlayerProfileV1.PowerUp
    //{
    //}

    //public class Booster : PlayerProfileV1.Booster
    //{
    //}

    //public class Payment : PlayerProfileV1.Payment
    //{
    //}

    public PlayerProfile() : base()
    {
        Touch();
        creationTime = updateTime;
    }

    /// <summary>
    /// Updates profile updateTime field to the current time and device info.
    /// Ref: https://stackoverflow.com/questions/17632584/how-to-get-the-unix-timestamp-in-c-sharp
    /// </summary>
    public void Touch()
    {
        updateTime = DateTimeTools.GetUtcTimestampNow();

        deviceModel = SystemInfo.deviceModel;
        deviceId = SystemInfo.deviceUniqueIdentifier;

        appVersion = "debug version"; // TODO
    }

    /// <summary>
    /// Logs a play session time to the profile.
    /// </summary>
    /// <param name="lastSessionDuration">Duration of a play session being logged (in seconds).</param>
    public void LogPlayTime(int lastSessionDuration)
    {
        Debug.Assert(lastSessionDuration >= 0);

        playTime += Mathf.Max(lastSessionDuration, 0);

        Touch();
    }

    /// <summary>
    /// Checks whether this player profile is empty and can be replaced or deleted without player confirmation.
    /// Profile is considered empty if its playTime is zero (no levels were played) and no purchases were made.
    /// </summary>
    /// <returns>True if the profile is considered empty.</returns>
    public bool IsEmpty()
    {
        return playTime <= 0; // TODO: add && purchases.Count < 1;
    }

    /// <summary>
    /// Clone this instance of PlayerProfile.
    /// </summary>
    /// <returns>Cloned player profile.</returns>
    public new PlayerProfile Clone()
    {
        var clone = new PlayerProfile();
        MapClassFields(base.Clone(), clone);
        return clone;
    }

    private static void MapClassFields(object from, object to)
    {
        FieldInfo[] fields = from.GetType().GetFields();

        foreach (var field in fields)
        {
            field.SetValue(to, field.GetValue(from));
        }
    }
    
}