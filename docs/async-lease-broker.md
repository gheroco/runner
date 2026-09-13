# Async lease broker branch: implementation status

This branch is a foundation, not a resumable runner release.

Implemented:
- Fix the RunService renewal retry null dereference by retaining `RenewJobResponse.LockedUntil`.
- Regression test: successful renewal, transient transport failure, recovery, terminal not-found.
- Extract `IWorkerMessageTransport`; local `IProcessChannel` inherits its send/receive contract.
- Source-layout container recipe in `images/Dockerfile.custom`; it does not download official runner binaries over the patched build.

`JobDispatcher` still starts a local worker and treats its exit normally. `JobRunner`
does not checkpoint, suspend, or restore. Killing the worker WILL NOT resume a job.
Do not enable the archived `GITHUB_ASYNC_SUSPEND` example: it is not implemented.

Full corrected contracts, known gaps, GHES proof criteria and build/deployment
runbook live in `gheroco/github-async-runners`, branch `feature/async-lease-broker`.
The runner's wire version remains 2.337.0; image revision identifies the fork.
Do not spoof a newer runner version to bypass GHES service requirements.

Build on Linux x64:

```sh
cd src
./dev.sh layout Release linux-x64
./dev.sh test
cd ..
docker build -f images/Dockerfile.custom --build-arg FORK_COMMIT="$(git rev-parse HEAD)" -t custom-runner:lab .
```

The image deliberately contains neither Docker nor container hooks; use ordinary
host shell/JS canaries. Manual registration must use `--disableupdate`. For ARC,
verify the JIT runner settings advertise disabled updates. A custom build remains
subject to the server's accepted runner versions and protocol behavior.
