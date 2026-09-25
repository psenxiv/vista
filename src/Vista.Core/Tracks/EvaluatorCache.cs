namespace Vista.Core.Tracks;

/// <summary>One track's evaluator, kept while the same track instance is asked for and rebuilt when another is.</summary>
public sealed class EvaluatorCache
{
    private Track? track;
    private TrackEvaluator? evaluator;

    /// <summary>The evaluator for <paramref name="of"/>, built afresh only when it isn't the track last asked for.</summary>
    public TrackEvaluator For(Track of)
    {
        if (!ReferenceEquals(track, of) || evaluator is null)
        {
            evaluator = new TrackEvaluator(of);
            track = of;
        }

        return evaluator;
    }
}
