namespace Donkeh.Parser.Tests;

public sealed class InternTableTests
{
    [Fact]
    public void InternCharMatchesSpanInternForAscii()
    {
        for (var c = 0; c < 128; c++)
        {
            var byChar = InternTable.Intern((char)c);
            var bySpan = InternTable.Intern(((char)c).ToString().AsSpan());

            Assert.Equal(bySpan, byChar);
            Assert.Equal(((char)c).ToString(), InternTable.GetString(byChar));
        }
    }

    [Fact]
    public void GetStringReturnsNullForUnknownId()
    {
        Assert.Null(InternTable.GetString(-1));
        Assert.Null(InternTable.GetString(int.MaxValue));
        Assert.False(InternTable.TryGetString(int.MaxValue, out _));
    }

    [Fact]
    public void ConcurrentInternReturnsStableIds()
    {
        const int count = 512;
        var ids = new int[count];

        Parallel.For(0, count, i => ids[i] = InternTable.Intern("concurrent-token-" + i));

        var unique = new HashSet<int>();

        for (var i = 0; i < count; i++)
        {
            Assert.True(unique.Add(ids[i]));
            Assert.Equal("concurrent-token-" + i, InternTable.GetString(ids[i]));
        }
    }
}
