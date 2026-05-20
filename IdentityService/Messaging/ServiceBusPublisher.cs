using Azure.Messaging.ServiceBus;
using System.Text.Json;

namespace IdentityService.Messaging
{
    public class ServiceBusPublisher
    {
        private readonly ServiceBusClient _client;
        private readonly string _verificationQueueName;

        public ServiceBusPublisher(ServiceBusClient client, IConfiguration config)
        {
            _client = client;
            _verificationQueueName = config["ServiceBus:VerificationQueueName"]
                ?? throw new InvalidOperationException("VerificationQueueName not configured.");
        }

        // Published after SignUp — Verification-Service picks this up and sends an email
        public async Task PublishUserRegisteredAsync(Guid userId, string email)
        {
            var sender = _client.CreateSender(_verificationQueueName);

            var payload = new
            {
                UserId = userId.ToString(),
                Email = email,
                CreatedAt = DateTime.UtcNow
            };

            var message = new ServiceBusMessage(JsonSerializer.Serialize(payload))
            {
                ContentType = "application/json",
                Subject = "UserRegistered"
            };

            await sender.SendMessageAsync(message);
            await sender.DisposeAsync();
        }
    }
}
