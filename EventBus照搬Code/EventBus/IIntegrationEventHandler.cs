using System.Threading.Tasks;

namespace EventBus照搬Code
{
    public interface IIntegrationEventHandler<T> where T : IntegrationEvent
    {
        Task Handle(T @event);
    }
}