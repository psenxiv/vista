namespace Vista.Core.Editing;

/// <summary>Mouse wheel travel carried between frames and taken in whole notches, so small scrolls add up to a step.</summary>
public sealed class WheelSteps
{
    private float carry;

    /// <summary>Adds <paramref name="travel"/> and returns the whole notches built up, signed, keeping the rest.</summary>
    public int Take(float travel)
    {
        carry += travel;
        var steps = (int)carry;
        carry -= steps;
        return steps;
    }

    /// <summary>Drops any travel not yet taken.</summary>
    public void Reset() => carry = 0f;
}
