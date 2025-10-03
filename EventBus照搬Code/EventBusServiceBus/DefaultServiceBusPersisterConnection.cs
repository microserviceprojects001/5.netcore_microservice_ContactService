using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Core;
using Microsoft.Extensions.Logging;

namespace EventBus照搬Code.EventBusServiceBus
{
    public class DefaultServiceBusPersisterConnection : IServiceBusPersisterConnection
    {
        private readonly ServiceBusConnectionStringBuilder _serviceBusConnectionStringBuilder;
        private readonly ILogger<DefaultServiceBusPersisterConnection> _logger;
        private ITopicClient _topicClient;
        private bool _disposed;

        public DefaultServiceBusPersisterConnection(string serviceBusConnectionString,
            ILogger<DefaultServiceBusPersisterConnection> logger)
        {
            _serviceBusConnectionStringBuilder = new ServiceBusConnectionStringBuilder(serviceBusConnectionString);
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _topicClient = new TopicClient(_serviceBusConnectionStringBuilder, RetryPolicy.Default);
        }

        public ServiceBusConnectionStringBuilder ServiceBusConnectionStringBuilder => _serviceBusConnectionStringBuilder;

        public string ServiceBusConnectionString => _serviceBusConnectionStringBuilder.ToString();

        public bool IsConnected => _topicClient != null && !_topicClient.IsClosedOrClosing && !_disposed;

        public ITopicClient CreateModel()
        {
            if (!IsConnected)
            {
                TryConnect();
            }

            return _topicClient;
        }

        public bool TryConnect()
        {
            try
            {
                if (IsConnected) return true;

                // 关闭现有连接
                _topicClient?.CloseAsync().GetAwaiter().GetResult();

                // 创建新连接
                _topicClient = new TopicClient(_serviceBusConnectionStringBuilder, RetryPolicy.Default);

                _logger.LogInformation("ServiceBus connection established to {Endpoint}",
                    _serviceBusConnectionStringBuilder.Endpoint);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to establish ServiceBus connection to {Endpoint}",
                    _serviceBusConnectionStringBuilder.Endpoint);
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            try
            {
                _topicClient?.CloseAsync().GetAwaiter().GetResult();
                _topicClient = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to dispose ServiceBus connection");
            }
        }
    }
}