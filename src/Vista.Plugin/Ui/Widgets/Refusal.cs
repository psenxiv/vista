namespace Vista.Plugin.Ui.Widgets;

/// <summary>Logs why the session refused an edit.</summary>
internal static class Refusal
{
    /// <summary>Warns if the edit was refused; does nothing if it went through.</summary>
    public static void Report(string? refusal)
    {
        if (refusal is not null) Plugin.Log.Warning("[ui] {Refusal}", refusal);
    }
}
