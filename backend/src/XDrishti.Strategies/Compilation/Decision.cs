using XDrishti.Indicators;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The compiled form of one condition: a pure function of the feature frame as it is now and of the handles of the condition's features in that frame (<paramref name="handles"/>[i] is the handle of
/// <c>Features[i]</c>), returning the numbers of <see cref="Truth"/> (1 true, 0 false, -1 unknown). One delegate is made per condition and shared by every frame it is bound to; the handles are what is per frame.
/// </summary>
internal delegate int Decision(FeatureFrame frame, int[] handles);
