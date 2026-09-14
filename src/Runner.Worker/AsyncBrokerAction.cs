using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GitHub.DistributedTask.Pipelines;
using GitHub.DistributedTask.ObjectTemplating.Tokens;
using GitHub.DistributedTask.WebApi;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GitHub.Runner.Worker
{
    // Job orchestration stays in the broker. Only the provider phases run in
    // disposable pods. No workflow code is permitted to execute in this process.
    public static class AsyncBrokerAction
    {
        public static bool Enabled => Environment.GetEnvironmentVariable("ACTIONS_ASYNC_BROKER_MODE") == "1";

        public static bool Matches(ActionStep action, string pin)
        {
            if (action?.Reference is not RepositoryPathReference reference || action.Background)
                return false;
            if (reference.RepositoryType != "GitHub" || string.IsNullOrEmpty(reference.Ref) ||
                reference.Ref.Length != 40 || !reference.Ref.All(Uri.IsHexDigit))
                return false;
            var identity = reference.Name + (string.IsNullOrEmpty(reference.Path) ? "" : "/" + reference.Path) + "@" + reference.Ref;
            return string.Equals(identity, pin, StringComparison.Ordinal);
        }

        public static void ValidateJob(AgentJobRequestMessage message)
        {
            if (!Enabled) return;
            var pin = Environment.GetEnvironmentVariable("ACTIONS_ASYNC_ACTION_PIN");
            if (string.IsNullOrEmpty(pin) || message.Steps.Count == 0 ||
                message.Steps.Any(s => s is not ActionStep step || !Matches(step, pin)) ||
                (message.JobContainer != null && message.JobContainer.Type != TokenType.Null) ||
                (message.JobServiceContainers != null && message.JobServiceContainers.Type != TokenType.Null) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ACTIONS_RUNNER_HOOK_JOB_STARTED")) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ACTIONS_RUNNER_HOOK_JOB_COMPLETED")))
                throw new InvalidOperationException("Async broker accepts only the administrator-pinned async action, without containers, services, background steps or runner hooks.");
        }

        public static async Task RunAsync(ActionStep action, IExecutionContext context)
        {
            if (!Matches(action, Environment.GetEnvironmentVariable("ACTIONS_ASYNC_ACTION_PIN")))
                throw new InvalidOperationException("Action is not admitted for async execution.");
            var inputs = context.ToPipelineTemplateEvaluator().EvaluateStepInputs(action.Inputs, context.ExpressionValues, context.ExpressionFunctions);
            if (!inputs.TryGetValue("profile", out var profile) || string.IsNullOrEmpty(profile))
                throw new InvalidOperationException("The async action requires an administrator-defined profile.");
            var parameters = JObject.Parse(inputs.TryGetValue("parameters", out var value) ? value : "{}");
            var endpoint = new Uri(Environment.GetEnvironmentVariable("ACTIONS_ASYNC_BROKER_URL"));
            if (endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback))
                throw new InvalidOperationException("Async broker requires HTTPS outside loopback.");
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { BaseAddress = endpoint, Timeout = TimeSpan.FromSeconds(30) };
            var token = File.ReadAllText(Environment.GetEnvironmentVariable("ACTIONS_ASYNC_TOKEN_FILE")).Trim();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            string route = "v1/executions/" + action.Id.ToString("N");
            var body = new JObject { ["profile"] = profile, ["parameters"] = parameters };
            try
            {
                // PUT is idempotent: a lost HTTP response cannot replay submit.
                await RequestAsync(client, HttpMethod.Put, route, body, context.CancellationToken);
                string lastPhase = null;
                while (true)
                {
                    var state = await RequestAsync(client, HttpMethod.Get, route, null, context.CancellationToken);
                    string phase = (string)state["phase"];
                    if (phase != lastPhase)
                    {
                        context.Output("Async execution phase: " + phase);
                        lastPhase = phase;
                    }
                    if (phase == "Succeeded")
                    {
                        if (state["outputs"] is JObject outputs)
                            foreach (var output in outputs.Properties())
                                context.SetOutput(output.Name, (string)output.Value, out _);
                        return;
                    }
                    if (phase == "Failed" || phase == "Lost" || phase == "Expired")
                        throw new InvalidOperationException("Async provider execution did not succeed; inspect the broker execution record.");
                    if (phase == "Cancelled")
                    {
                        context.Result = TaskResult.Canceled;
                        return;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);
                }
            }
            finally
            {
                // Also stop remote work after a local transport failure. Broker
                // ownership heartbeat/deadline covers abrupt coordinator death.
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try { await RequestAsync(client, HttpMethod.Delete, route, null, cleanup.Token); }
                catch { context.Warning("Async cleanup could not be confirmed; broker watchdog will reconcile the execution."); }
            }
        }

        private static async Task<JObject> RequestAsync(HttpClient client, HttpMethod method, string path, JObject body, CancellationToken token)
        {
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    using var request = new HttpRequestMessage(method, path);
                    if (body != null) request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
                    using var response = await client.SendAsync(request, token);
                    if ((int)response.StatusCode >= 500 && attempt < 3)
                    {
                        await Task.Delay(1000, token);
                        continue;
                    }
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("Async broker rejected request: HTTP " + (int)response.StatusCode);
                    var json = await response.Content.ReadAsStringAsync(token);
                    return string.IsNullOrEmpty(json) ? new JObject() : JObject.Parse(json);
                }
                catch (HttpRequestException) when (attempt < 3)
                {
                    await Task.Delay(1000, token);
                }
            }
        }
    }
}
