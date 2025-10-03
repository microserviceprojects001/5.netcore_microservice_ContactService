using System.Threading.Tasks;

namespace EventBus照搬Code.EventBus
{
    public interface IDynamicIntegrationEventHandler
    {
        Task Handle(dynamic eventData);
    }
}