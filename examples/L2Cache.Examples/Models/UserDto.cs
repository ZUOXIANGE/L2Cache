using MemoryPack;

namespace L2Cache.Examples.Models;

[MemoryPackable]
public partial class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public override string ToString()
    {
        return $"Id: {Id}, Username: {Username}, Email: {Email}";
    }
}
