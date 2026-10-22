#if MOBILE
namespace DiscordRPC
{
    public class DiscordRpcClient : IDisposable
    {
        public DiscordRpcClient(string applicationID, bool autoEvents = true, int targetPipe = -1) { }
        public bool Initialize() => true;
        public void SetPresence(object presence) { }
        public void Invoke() { }
        public void Dispose() { }
    }

    public class RichPresence
    {
        public string Details { get; set; } = "";
        public string State { get; set; } = "";
        public object Assets { get; set; } = new object();
    }

    public class Assets
    {
        public string LargeImageKey { get; set; } = "";
        public string LargeImageText { get; set; } = "";
    }
}
#endif
