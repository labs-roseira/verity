using Verity.CashFlow.Domain.Results;

namespace Verity.CashFlow.UnitTests.Domain;

public class ResultTests
{
    [Fact]
    public void Success_IsSuccessTrueAndErrorNone()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_IsFailureTrueAndCarriesError()
    {
        var error = new Error("SOME_CODE", "Something went wrong.");

        var result = Result.Failure(error);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SOME_CODE");
        result.Error.Message.ShouldBe("Something went wrong.");
    }

    [Fact]
    public void SuccessOfT_ExposesValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void FailureOfT_ValueAccessThrows()
    {
        var result = Result.Failure<int>(new Error("SOME_CODE", "Failed."));

        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }
}
