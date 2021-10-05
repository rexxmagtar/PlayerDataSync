using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.pigsels.BubbleTrouble
{
    public interface IPlayerProfile
    {
        int Version { get; }

        IPlayerProfile MigrateToNextVersion();

    }

}