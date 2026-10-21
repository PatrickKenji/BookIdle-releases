namespace IdleDeck.Core.Vault;

public record Credential(string Username, string Password);

public class VaultUnlockException(string message, Exception? inner = null) : Exception(message, inner);
