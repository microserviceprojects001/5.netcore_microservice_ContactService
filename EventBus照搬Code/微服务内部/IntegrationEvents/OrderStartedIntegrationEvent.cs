using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventBus照搬Code.微服务内部.IntegrationEvents
{
    public class OrderStartedIntegrationEvent : IntegrationEvent
    {
        public int OrderId { get; set; }
        public DateTime OrderDate { get; set; }

        public OrderStartedIntegrationEvent(int orderId, DateTime orderDate)
        {
            OrderId = orderId;
            OrderDate = orderDate;
        }
    }
}