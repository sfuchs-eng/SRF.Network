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
    }
}
