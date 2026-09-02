using Danslicer.Core.Utilities;

namespace Danslicer.Tests;

public class ExpressionParserTests
{
    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData("10 + 2*3", 16)]
    [InlineData("(4 - 1) / 2", 1.5)]
    [InlineData("-3", -3)]
    [InlineData("--3", 3)]
    [InlineData("1in", 25.4)]
    [InlineData("1in + 5mm", 30.4)]
    [InlineData("2 cm", 20)]
    [InlineData("(1 + 1) cm", 20)]
    [InlineData("1e2", 100)]
    [InlineData("500um", 0.5)]
    public void EvaluatesLengths(string text, double expected)
    {
        Assert.Equal(expected, ExpressionParser.Evaluate(text, UnitKind.Length), 6);
    }

    [Theory]
    [InlineData("90", 90)]
    [InlineData("90deg", 90)]
    [InlineData("45°", 45)]
    [InlineData("3.14159265358979rad", 180)]
    public void EvaluatesAngles(string text, double expected)
    {
        Assert.Equal(expected, ExpressionParser.Evaluate(text, UnitKind.Angle), 6);
    }

    [Theory]
    [InlineData("50%", 0.5)]
    [InlineData("2", 2)]
    public void EvaluatesScalars(string text, double expected)
    {
        Assert.Equal(expected, ExpressionParser.Evaluate(text, UnitKind.Scalar), 6);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1 +")]
    [InlineData("(1")]
    [InlineData("1/0")]
    [InlineData("5 furlongs")]
    [InlineData("1 2")]
    public void RejectsInvalid(string text)
    {
        Assert.False(ExpressionParser.TryEvaluate(text, UnitKind.Length, out _));
    }
}
