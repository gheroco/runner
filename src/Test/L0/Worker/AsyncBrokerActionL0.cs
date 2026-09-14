using GitHub.DistributedTask.Pipelines;
using GitHub.Runner.Worker;
using Xunit;

namespace GitHub.Runner.Common.Tests.Worker
{
    public class AsyncBrokerActionL0
    {
        [Fact]
        [Trait("Level", "L0")]
        public void AdmitsOnlyExactPinnedRepositoryAction()
        {
            string sha = new string('a', 40);
            string pin = "gheroco/github-async-runners/async-job@" + sha;
            var step = new ActionStep { Reference = new RepositoryPathReference {
                RepositoryType = "GitHub", Name = "gheroco/github-async-runners", Path = "async-job", Ref = sha } };
            Assert.True(AsyncBrokerAction.Matches(step, pin));
            step.Background = true;
            Assert.False(AsyncBrokerAction.Matches(step, pin));
            step.Background = false;
            ((RepositoryPathReference)step.Reference).Ref = "main";
            Assert.False(AsyncBrokerAction.Matches(step, pin));
            ((RepositoryPathReference)step.Reference).Ref = sha;
            ((RepositoryPathReference)step.Reference).RepositoryType = "self";
            Assert.False(AsyncBrokerAction.Matches(step, pin));
            step.Reference = new ScriptReference();
            Assert.False(AsyncBrokerAction.Matches(step, pin));
        }
    }
}
