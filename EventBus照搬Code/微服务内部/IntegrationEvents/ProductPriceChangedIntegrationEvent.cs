using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventBus照搬Code.微服务内部.IntegrationEvents
{
    public class ProductPriceChangedIntegrationEvent : IntegrationEvent
    {
        public int ProductId { get; set; }
        public decimal OldPrice { get; set; }
        public decimal NewPrice { get; set; }

        public ProductPriceChangedIntegrationEvent(int productId, decimal oldPrice, decimal newPrice)
        {
            ProductId = productId;
            OldPrice = oldPrice;
            NewPrice = newPrice;
        }
    }
}