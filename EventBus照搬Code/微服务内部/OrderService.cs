public class OrderService
{
    private readonly IEventBus _eventBus;

    public OrderService(IEventBus eventBus)
    {
        _eventBus = eventBus;
    }

    public void CreateOrder(CreateOrderRequest request)
    {
        // 创建订单的业务逻辑...

        // 发布订单创建事件
        var orderCreatedEvent = new OrderStartedIntegrationEvent(orderId, DateTime.UtcNow);
        _eventBus.Publish(orderCreatedEvent);
    }
}