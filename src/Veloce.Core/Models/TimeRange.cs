using System;

namespace Veloce.Core.Models;

public record TimeRange(TimeSpan Start, TimeSpan End)
{
    public TimeSpan Duration => End - Start;

    public bool IsValid => End > Start && Start >= TimeSpan.Zero;
}
