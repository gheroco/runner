# Coordinator-owned asynchronous execution

With `ACTIONS_ASYNC_BROKER_MODE=1`, `JobRunner` admits only the action named by
`ACTIONS_ASYNC_ACTION_PIN` (exact repository/path@40-character commit). It rejects
shell steps, other actions, background steps, containers, services and runner hooks.
`ActionRunner` intercepts that pinned action and calls the broker over an authenticated
loopback connection. The broker launches disposable submit/resume/cancel pods.

Listener and Worker orchestration remain alive in the broker during the external
wait. Their existing renewal, cancellation, timeline, masks and job-output handling
remain active. There is no serialization or reconstruction of Runner.Worker; an
unexpected broker restart fails closed and does not resume its GitHub jobs.

The default upstream local-runner path is unchanged when the environment flag is
absent. Never enable coordinator mode on an unrestricted runner pool. Per-runner
credentials are installed by the broker supervisor, not by workflow inputs.

See the central deployment runbook and live-runtime acceptance procedure in
`gheroco/github-async-runners` on `feature/async-lease-broker`. Build the customized
image from this source's `_layout` using `images/Dockerfile.custom`.
