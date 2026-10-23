// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Jxl.VarDct;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The encoder's <c>Set</c> of the AC strategy image, which overwrites what is there, as the AC strategy
/// search does when it merges blocks.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlAcStrategyImageTests
{
    [Fact]
    public void Set_OverwritesEveryCoveredBlock_AndMarksOnlyTheFirst()
    {
        JxlAcStrategyImage image = new(4, 3);
        image.FillDct8();

        image.Set(1, 1, JxlAcStrategyType.Dct16X16);

        for (int y = 0; y < 3; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                JxlAcStrategy acs = image.Get(x, y);
                bool covered = x is 1 or 2 && y is 1 or 2;
                Assert.Equal(covered ? JxlAcStrategyType.Dct16X16 : JxlAcStrategyType.Dct, acs.Strategy);
                Assert.Equal(!covered || (x == 1 && y == 1), acs.IsFirstBlock);
            }
        }
    }

    [Fact]
    public void Set_OverAnEarlierStrategy_LeavesTheLaterOne()
    {
        JxlAcStrategyImage image = new(4, 4);
        image.FillInvalid();
        image.Set(0, 0, JxlAcStrategyType.Dct32X32);

        image.Set(2, 2, JxlAcStrategyType.Dct16X16);
        image.Set(0, 0, JxlAcStrategyType.Dct16X8);

        Assert.Equal(new JxlAcStrategy(JxlAcStrategyType.Dct16X8, true), image.Get(0, 0));
        Assert.Equal(new JxlAcStrategy(JxlAcStrategyType.Dct16X8, false), image.Get(0, 1));
        Assert.Equal(new JxlAcStrategy(JxlAcStrategyType.Dct32X32, false), image.Get(1, 0));
        Assert.Equal(new JxlAcStrategy(JxlAcStrategyType.Dct16X16, true), image.Get(2, 2));
        Assert.Equal(new JxlAcStrategy(JxlAcStrategyType.Dct16X16, false), image.Get(3, 3));
    }
}
