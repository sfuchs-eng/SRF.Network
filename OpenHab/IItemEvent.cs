using System;
namespace SRF.Network.OpenHab
{
    public interface IItemEvent : IEvent
    {
        IItemEvent ForItem(string itemName);
    }
}
