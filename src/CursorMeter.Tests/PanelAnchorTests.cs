using Xunit;

namespace CursorMeter.Tests;

public sealed class PanelAnchorTests
{
    [Fact]
    public void A_rectangle_is_read_as_it_is()
    {
        Assert.True(PanelAnchor.TryParse("""{"x":820,"y":652,"width":212,"height":69}""", out var anchor));
        Assert.Equal(new PanelAnchor(820, 652, 212, 69), anchor);
    }

    [Fact]
    public void Fractions_are_rounded()
    {
        Assert.True(PanelAnchor.TryParse("""{"x":820.5,"y":651.4,"width":211.6,"height":69}""", out var anchor));
        Assert.Equal(new PanelAnchor(821, 651, 212, 69), anchor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("[1,2,3,4]")]
    [InlineData("""{"x":1,"y":2,"width":3}""")]
    [InlineData("""{"x":"1","y":2,"width":3,"height":4}""")]
    [InlineData("""{"x":1,"y":2,"width":0,"height":4}""")]
    [InlineData("""{"x":1,"y":2,"width":3,"height":-4}""")]
    [InlineData("""{"x":1e99,"y":2,"width":3,"height":4}""")]
    public void Anything_else_is_not_an_anchor(string? text)
    {
        Assert.False(PanelAnchor.TryParse(text, out var anchor));
        Assert.Equal(default, anchor);
    }

    [Fact]
    public void A_screen_to_the_left_of_the_primary_one_has_negative_coordinates()
    {
        Assert.True(PanelAnchor.TryParse("""{"x":-1100,"y":652,"width":212,"height":69}""", out var anchor));
        Assert.Equal(-1100, anchor.X);
    }
}
