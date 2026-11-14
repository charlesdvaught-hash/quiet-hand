namespace QuietHand.Core;

/// Swappable file I/O — native targets hit disk; browser/WASM injects an
/// in-memory store preloaded by JS (content) backed by localStorage (saves).
public static class SimIO
{
    public static Func<string, bool> Exists = File.Exists;
    public static Func<string, string> ReadAllText = File.ReadAllText;
    public static Action<string, string> WriteAllText = File.WriteAllText;
}
