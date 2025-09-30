namespace SRF.Network.Mqtt;

public class PublishEventArgs(PublishingQueueItem publishingQueueItem) : EventArgs
{
    public PublishingQueueItem PublishingQueueItem { get; } = publishingQueueItem;
}
