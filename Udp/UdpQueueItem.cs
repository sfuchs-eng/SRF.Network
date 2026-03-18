namespace SRF.Network.Udp;

/// <summary>
/// Represents a UDP message queued for transmission.
/// </summary>
public class UdpQueueItem
{
    /// <summary>
    /// The data to send.
    /// </summary>
    public byte[] Data { get; }

    /// <summary>
    /// Number of send attempts made.
    /// </summary>
    public int Attempts { get; internal set; }

    /// <summary>
    /// Timestamp when the item was queued.
    /// </summary>
    public DateTimeOffset QueuedAt { get; }

    /// <summary>
    /// Event raised when the message has been sent successfully.
    /// </summary>
    public event EventHandler<UdpMessageSentEventArgs>? Sent;

    /// <summary>
    /// Event raised when the message failed to send after all retry attempts.
    /// </summary>
    public event EventHandler<UdpMessageFailedEventArgs>? Failed;

    public UdpQueueItem(byte[] data, DateTimeOffset queuedAt)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        QueuedAt = queuedAt;
        Attempts = 0;
    }

    internal void NotifySent(DateTimeOffset sentAt)
    {
        Sent?.Invoke(this, new UdpMessageSentEventArgs(Data, Attempts, sentAt));
    }

    internal void NotifyFailed(DateTimeOffset failedAt, string errorMessage)
    {
        Failed?.Invoke(this, new UdpMessageFailedEventArgs(Data, Attempts, errorMessage, failedAt));
    }
}

/// <summary>
/// Event arguments for successfully sent UDP messages.
/// </summary>
public class UdpMessageSentEventArgs : EventArgs
{
    public byte[] Data { get; }
    public int Attempts { get; }
    public DateTimeOffset SentAt { get; }

    public UdpMessageSentEventArgs(byte[] data, int attempts, DateTimeOffset sentAt)
    {
        Data = data;
        Attempts = attempts;
        SentAt = sentAt;
    }
}

/// <summary>
/// Event arguments for failed UDP message transmission.
/// </summary>
public class UdpMessageFailedEventArgs : EventArgs
{
    public byte[] Data { get; }
    public int Attempts { get; }
    public string ErrorMessage { get; }
    public DateTimeOffset FailedAt { get; }

    public UdpMessageFailedEventArgs(byte[] data, int attempts, string errorMessage, DateTimeOffset failedAt)
    {
        Data = data;
        Attempts = attempts;
        ErrorMessage = errorMessage;
        FailedAt = failedAt;
    }
}
