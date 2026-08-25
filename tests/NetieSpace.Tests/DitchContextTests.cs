using NetieSpace.Models;
using NetieSpace.Services;

namespace NetieSpace.Tests;

public class DitchContextTests
{
    [Fact]
    public void Keeps_system_and_last_user()
    {
        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = "You are Space." }
        };
        for (var i = 0; i < 20; i++)
        {
            messages.Add(new ChatMessage { Role = "user", Content = $"u{i} " + new string('x', 800) });
            messages.Add(new ChatMessage { Role = "assistant", Content = $"a{i} " + new string('y', 800) });
        }

        messages.Add(new ChatMessage { Role = "user", Content = "FINAL USER" });

        var ditched = DitchContext.Apply(messages, budgetChars: 4_000);
        Assert.Contains(ditched, m => m.Role == "system" && m.Content.Contains("You are Space"));
        Assert.Equal("FINAL USER", ditched[^1].Content);
        Assert.True(ditched.Count < messages.Count);
    }
}
