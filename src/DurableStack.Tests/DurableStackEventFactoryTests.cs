using DurableStack.Core.Events;
using DurableStack.Core.Models;
using DurableStack.Core.Options;

namespace DurableStack.Tests;

public sealed class DurableStackEventFactoryTests
{
    [Fact]
    public void Create_omits_error_detail_when_disabled()
    {
        var options = new DurableStackOptions();
        options.Eventing.IncludeErrorDetail = false;

        var factory = new DurableStackEventFactory(options);
        var evt = factory.Create(
            DurableStackEventTypes.JobFailed,
            run: new JobRunRecord { Id = Guid.NewGuid(), JobName = "job-a", Attempt = 1, MaxAttempts = 3 },
            message: "failed",
            errorType: typeof(InvalidOperationException).FullName,
            errorDetail: "secret stack trace details");

        Assert.Equal(typeof(InvalidOperationException).FullName, evt.ErrorType);
        Assert.Null(evt.ErrorDetail);
        Assert.Equal(1, evt.Attempt);
        Assert.Equal(3, evt.MaxAttempts);
    }

    [Fact]
    public void Create_truncates_error_detail_to_max_length()
    {
        var options = new DurableStackOptions();
        options.Eventing.IncludeErrorDetail = true;
        options.Eventing.MaxErrorDetailLength = 8;

        var factory = new DurableStackEventFactory(options);
        var evt = factory.Create(
            DurableStackEventTypes.JobFailed,
            run: new JobRunRecord { Id = Guid.NewGuid(), JobName = "job-a", Attempt = 1, MaxAttempts = 3 },
            message: "failed",
            errorType: typeof(InvalidOperationException).FullName,
            errorDetail: "0123456789");

        Assert.Equal("01234567", evt.ErrorDetail);
        Assert.Equal(1, evt.Attempt);
        Assert.Equal(3, evt.MaxAttempts);
    }
}
