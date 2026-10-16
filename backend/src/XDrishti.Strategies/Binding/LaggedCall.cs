namespace XDrishti.Strategies.Binding;

/// <summary>
/// What a <see cref="BoundInvalid"/> keeps of a <c>crosses_*</c>, <c>rising</c> or <c>falling</c> that could not be bound (a type error in an operand, a bad count): the first <see cref="Operands"/> of its parts are the
/// operands of the call, and each would be read <see cref="Lag"/> closed bars further back (1 for a crossing, the count for a run; 0 when the count is not a usable number). The lint reads it so that the call is still
/// checked for the clocks of its operands, and the depth they add up to, together with the fault that made it invalid.
/// </summary>
internal readonly record struct LaggedCall(int Operands, int Lag);
