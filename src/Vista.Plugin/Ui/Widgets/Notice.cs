using Dalamud.Interface.ImGuiNotification;

namespace Vista.Plugin.Ui.Widgets;

/// <summary>Shows the player a warning notification that fades.</summary>
internal static class Notice
{
    /// <summary>How long a warning notification stays on screen.</summary>
    private static readonly TimeSpan Life = TimeSpan.FromSeconds(4);

    /// <summary>Shows <paramref name="message"/> as a warning notification.</summary>
    public static void Warn(string message) =>
        Plugin.Notifications.AddNotification(
            new Notification
            {
                Title = Plugin.NoticeTitle,
                Content = message,
                Type = NotificationType.Warning,
                InitialDuration = Life,
            }
        );
}
