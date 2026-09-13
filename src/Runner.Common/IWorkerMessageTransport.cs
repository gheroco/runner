using System;
using System.Threading;
using System.Threading.Tasks;

namespace GitHub.Runner.Common
{
    // Message operations are independent of process creation. Remote transports must
    // authenticate both endpoints and fence every execution before delivering a job.
    // This interface does not make Worker or JobRunner resumable.
    public interface IWorkerMessageTransport : IDisposable
    {
        Task SendAsync(MessageType messageType, string body, CancellationToken cancellationToken);
        Task<WorkerMessage> ReceiveAsync(CancellationToken cancellationToken);
    }
}
