using Microsoft.Azure.ServiceBus;

namespace EventBus照搬Code.EventBusServiceBus
{
    public interface IServiceBusPersisterConnection
    {
        ServiceBusConnectionStringBuilder ServiceBusConnectionStringBuilder { get; }
        string ServiceBusConnectionString { get; }
        bool IsConnected { get; }
        bool TryConnect();
        ITopicClient CreateModel();
        void Dispose();
    }
}