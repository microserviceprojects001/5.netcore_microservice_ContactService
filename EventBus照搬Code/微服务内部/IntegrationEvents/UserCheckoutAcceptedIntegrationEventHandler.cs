using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EventBus照搬Code.微服务内部.IntegrationEvents
{
    public class UserCheckoutAcceptedIntegrationEventHandler : IIntegrationEventHandler<UserCheckoutAcceptedIntegrationEvent>
    {
        public Task Handle(UserCheckoutAcceptedIntegrationEvent @event)
        {
            // 处理用户结账接受的逻辑
            Console.WriteLine($"User Checkout Accepted: UserId: {@event.UserId}, OrderId: {@event.OrderId}");
            return Task.CompletedTask;
        }
    }
}