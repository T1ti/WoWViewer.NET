using WoWRenderLib.Structs;

namespace WoWRenderLib.DX11.Renderer;

/// <summary>Playback choices owned by one M2 placement.</summary>
public sealed class M2InstanceAnimationState
{
    /// <summary>Sequence table index; -1 selects the model's first Stand sequence.</summary>
    public int SequenceIndex { get; set; } = -1;

    /// <summary>Placement-specific phase relative to the scene animation clock.</summary>
    public long TimeOffsetMilliseconds { get; set; }

    internal M2AnimationFrameKey GetFrameKey(M2Animation animation, long sceneTimeMilliseconds)
    {
        var sequence = (uint)SequenceIndex < animation.Sequences.Length
            ? SequenceIndex
            : animation.DefaultSequenceIndex;
        var time = Math.Max(0, sceneTimeMilliseconds + TimeOffsetMilliseconds);
        return new M2AnimationFrameKey(sequence, time);
    }
}

internal readonly record struct M2AnimationFrameKey(int SequenceIndex, long TimeMilliseconds);
