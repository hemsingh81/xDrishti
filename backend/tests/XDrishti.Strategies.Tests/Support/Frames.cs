using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>Frames made directly from specs, for the tests that look at one small thing.</summary>
internal static class Frames
{
    /// <summary>A frame with only <c>close</c> on 1m.</summary>
    public static FeatureFrame OfClose() => Of(Specs.Close(Timeframe.M1));

    public static FeatureFrame Of(params FeatureSpec[] specs) => Of(new FrameOptions(), specs);

    public static FeatureFrame Of(FrameOptions options, params FeatureSpec[] specs)
    {
        var created = FeatureFrame.Create(specs, options);
        created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : string.Empty);
        return created.Value;
    }
}
