using PhantomMonitor.App.Linux.DBus;
using Tmds.DBus.Protocol;

namespace PhantomMonitor.App.Services;

/// <summary>Envia notificações ao serviço freedesktop da sessão Linux.</summary>
internal sealed class LinuxDesktopNotificationService : IDisposable
{
    private const string ServiceName = "org.freedesktop.Notifications";
    private const string ObjectPath = "/org/freedesktop/Notifications";
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private DBusConnection? _connection;
    private Notifications? _notifications;
    private uint _lastNotificationId;
    private bool _disposed;

    public async Task NotifyAsync(string message)
    {
        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var notifications = await GetNotificationsProxyAsync().ConfigureAwait(false);
            _lastNotificationId = await notifications.NotifyAsync(
                "Phantom Monitor",
                _lastNotificationId,
                "",
                "Phantom Monitor",
                message,
                Array.Empty<string>(),
                new Dictionary<string, VariantValue>(),
                6000).ConfigureAwait(false);
        }
        catch
        {
            ResetConnection();
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task<Notifications> GetNotificationsProxyAsync()
    {
        if (_notifications is not null) return _notifications;

        var sessionAddress = DBusAddress.Session
            ?? throw new InvalidOperationException("A sessão D-Bus do usuário não está disponível.");

        var connection = new DBusConnection(sessionAddress);
        try
        {
            await connection.ConnectAsync().ConfigureAwait(false);
            _connection = connection;
            _notifications = new Notifications(connection, ServiceName, ObjectPath);
            return _notifications;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private void ResetConnection()
    {
        _notifications = null;
        _connection?.Dispose();
        _connection = null;
        _lastNotificationId = 0;
    }

    public void Dispose()
    {
        _disposed = true;
        ResetConnection();
    }
}
