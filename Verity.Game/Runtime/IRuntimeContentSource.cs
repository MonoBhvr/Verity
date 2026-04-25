namespace Verity.Game.Runtime;

public interface IRuntimeContentSource
{
    string PrepareContentRoot();
    string GetLoosePath(string relativePath);
    string? TryReadText(string relativePath);
    byte[]? TryReadBytes(string relativePath);
}
