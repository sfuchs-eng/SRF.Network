namespace SRF.Network.Udp;

/// <summary>
/// Event arguments for UDP connection status changes.
/// </summary>
public class UdpConnectionEventArgs : EventArgs
{
    /// <summary>
    /// Indicates whether the connection is currently active.
    /// </summary>
    public bool IsConnected { get; }

    /// <summary>
    /// Optional error message if the connection failed or was interrupted.
    /// </summary>
    public string? ErrorMessage { get; }

    public UdpConnectionEventArgs(bool isConnected, string? errorMessage = null)
    {
        IsConnected = isConnected;
        ErrorMessage = errorMessage;
    }
}
