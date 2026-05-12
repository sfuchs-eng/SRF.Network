using System;
using System.Threading;
using System.Threading.Tasks;

namespace SRF.Network.OpenHab
{
    public interface IRestApiClient
    {
        /// <summary>
        /// Obtains the OpenHAB Items inventory
        /// </summary>
        Task<Items.Item[]> GetItemsAsync(CancellationToken cancel);

        /// <summary>
        /// Sets the state of an OpenHAB item.
        /// </summary>
        Task SetItemStateAsync(string itemName, string state, CancellationToken cancel = default);
    }
}
