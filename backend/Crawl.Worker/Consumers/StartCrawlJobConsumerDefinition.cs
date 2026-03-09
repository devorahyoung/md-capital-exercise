using MassTransit;

namespace Crawl.Worker.Consumers;

/// <summary>
/// Configures the retry policy and Dead Letter Queue (DLQ) for
/// <see cref="StartCrawlJobConsumer"/>.
///
/// Retry schedule (before a message is moved to the DLQ):
///   attempt 1 – immediate
///   attempt 2 – 5 s delay
///   attempt 3 – 30 s delay
///
/// After all retries are exhausted MassTransit routes the message to the
/// RabbitMQ queue "StartCrawlJob_error", which serves as the DLQ.
/// Messages in the DLQ can be inspected via the RabbitMQ management UI
/// (http://localhost:15672) and re-queued manually when appropriate.
/// </summary>
public sealed class StartCrawlJobConsumerDefinition : ConsumerDefinition<StartCrawlJobConsumer>
{
    public StartCrawlJobConsumerDefinition()
    {
        // Pin the queue name so the DLQ name is always predictable:
        // "StartCrawlJob_error" (MassTransit appends "_error" automatically).
        EndpointName = "StartCrawlJob";
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<StartCrawlJobConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Retry up to 3 times with increasing delays before the message is
        // moved to the DLQ.  Covers transient failures such as a momentary
        // DB unavailability or a network blip while marking the job status.
        endpointConfigurator.UseMessageRetry(r =>
            r.Intervals(
                TimeSpan.Zero,            // attempt 1: immediate
                TimeSpan.FromSeconds(5),  // attempt 2:  5 s
                TimeSpan.FromSeconds(30)  // attempt 3: 30 s
            ));
    }
}
