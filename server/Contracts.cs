using System;

namespace AndroidRpg.Server.Contracts
{
    public sealed class CharacterState
    {
        public Guid Id { get; init; }
        public string DisplayName { get; init; } = string.Empty;
        public int Level { get; set; } = 1;
        public long Balance { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float PositionZ { get; set; }
        public string[] Inventory { get; set; } = Array.Empty<string>();
    }

    public sealed record RegisterRequest(string Email, string Password, string DisplayName);
    public sealed record LoginRequest(string Email, string Password);
    public sealed record SaveCharacterRequest(CharacterState Character);
}
