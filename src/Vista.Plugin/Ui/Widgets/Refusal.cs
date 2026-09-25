using System.Diagnostics;
using Dalamud.Interface.ImGuiNotification;
using Vista.Core.Display;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>Tells the player why Vista refused what they asked, in the log and as a notification that fades.</summary>
internal static class Refusal
{
    /// <summary>How long a refusal's notification stays on screen.</summary>
    private static readonly TimeSpan Life = TimeSpan.FromSeconds(4);

    private static readonly RefusalNotices Notices = new();
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>Logs a refusal and shows it unless it was just shown; does nothing if the action went through.</summary>
    public static void Report(string? refusal)
    {
        if (refusal is null)
            return;
        Plugin.Log.Warning("[refused] {Refusal}", refusal);
        if (!Notices.Show(refusal, Clock.Elapsed.TotalSeconds))
            return;
        Plugin.Notifications.AddNotification(
            new Notification
            {
                Title = Plugin.NoticeTitle,
                Content = refusal,
                Type = NotificationType.Warning,
                InitialDuration = Life,
            }
        );
    }
}
