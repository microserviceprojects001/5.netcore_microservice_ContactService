using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventBus照搬Code.微服务内部.IntegrationEvents
{
    public class OrderStartedIntegrationEventHandler : IIntegrationEventHandler<OrderStartedIntegrationEvent>
    {
        public Task Handle(OrderStartedIntegrationEvent @event)
        {
            // 处理订单开始的逻辑
            Console.WriteLine($"Order Started: OrderId: {@event.OrderId}, OrderDate: {@event.OrderDate}");
            return Task.CompletedTask;
        }
    }
}