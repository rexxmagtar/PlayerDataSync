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
    public class PlayerProfileV2 : PlayerProfileV1
    {

        /// <summary>
        /// PlayerProfile version. Used for migration purpose.
        /// </summary>
        public new int Version => 2;


   }
}