using Radzen;

namespace ProxysqlAdminUi.Web.Services;

public sealed class AppNotificationService(NotificationService notificationService)
{
    private const double DefaultDurationMilliseconds = 15_000;

    public void Notify(
        NotificationSeverity severity = NotificationSeverity.Info,
        string summary = "",
        string detail = "",
        double duration = DefaultDurationMilliseconds,
        Action<NotificationMessage>? click = null,
        bool closeOnClick = false,
        object? payload = null,
        Action<NotificationMessage>? close = null)
    {
        notificationService.Notify(severity, summary, detail, duration, click, closeOnClick, payload, close);
    }

    public void Notify(
        NotificationSeverity severity,
        string summary,
        string detail,
        TimeSpan duration,
        Action<NotificationMessage>? click = null)
    {
        notificationService.Notify(severity, summary, detail, duration, click);
    }
}
