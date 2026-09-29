namespace WoWRenderLib.Structs;

/// <summary>World::ValidateFarClip for the 3.3.5 farclip CVar.</summary>
public static class Wrath335FarClip
{
    public const float Default = 350f;
    public const float FixedNearClip = 0.2f;
    public const float Minimum = 183.33333f;
    public const float StandardMaximum = 791.66669f;
    public const float CVarMaximum = 1277f; // maximum of the client setting UI slider/console variable /console farclip setting
    public const float ExpandedMaximum = 1583.3334f; // maximum in actual engine code
    public const int OutlandMapId = 530; // outland
    public const int ExpansionExceptionMapIdA = 575; // northrend
    public const int ExpansionExceptionMapIdB = 543; // hellfire citadel : rampart, dungeon

    // In game UI setting
    /// <param name="hasMoreThanOneGiBPhysicalMemory">Client's memory-based expansion branch.</param>
    // Setting slider is not affected by this, it is always 183 - 1277 regardless of map.
    // Function below is only to cap at runtime
    public static float Validate(float requested, int mapId, bool overrideEnabled,
        bool hasMoreThanOneGiBPhysicalMemory = true)
    {
        // map is TBC or WOTLK but not northrend/hellfire
        var expansionMap = mapId >= OutlandMapId &&
                           mapId != ExpansionExceptionMapIdA &&
                           mapId != ExpansionExceptionMapIdB;
        var maximum = (expansionMap && hasMoreThanOneGiBPhysicalMemory) ||
                      (!expansionMap && overrideEnabled)
            ? ExpandedMaximum : StandardMaximum;
        return Math.Clamp(float.IsFinite(requested) ? requested : Default,
            Minimum, maximum);
    }
}
