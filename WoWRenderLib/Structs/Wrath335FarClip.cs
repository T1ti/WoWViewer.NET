namespace WoWRenderLib.Structs;

/// <summary>World::ValidateFarClip for the 3.3.5 farclip CVar.</summary>
public static class Wrath335FarClip
{
    public const float Default = 350f;
    public const float FixedNearClip = 0.2f;
    public const float Minimum = 183.33333f;
    public const float StandardMaximum = 791.66669f;
    public const float ExpandedMaximum = 1583.3334f;
    public const int ExpansionMapId = 530;
    public const int ExpansionExceptionMapIdA = 575;
    public const int ExpansionExceptionMapIdB = 543;

    /// <param name="hasMoreThanOneGiBPhysicalMemory">Client's memory-based expansion branch.</param>
    public static float Validate(float requested, int mapId, bool overrideEnabled,
        bool hasMoreThanOneGiBPhysicalMemory = true)
    {
        var expansionMap = mapId >= ExpansionMapId &&
                           mapId != ExpansionExceptionMapIdA &&
                           mapId != ExpansionExceptionMapIdB;
        var maximum = (expansionMap && hasMoreThanOneGiBPhysicalMemory) ||
                      (!expansionMap && overrideEnabled)
            ? ExpandedMaximum : StandardMaximum;
        return Math.Clamp(float.IsFinite(requested) ? requested : Default,
            Minimum, maximum);
    }
}
