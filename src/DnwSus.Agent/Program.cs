using System.ServiceProcess;

namespace DnwSus.Agent
{
    /// <summary>Windows-Dienst "DnwSusAgent" (läuft als SYSTEM, wird von der MSI eingerichtet).</summary>
    internal sealed class AgentService : ServiceBase
    {
        private readonly Worker _worker = new Worker();

        public AgentService()
        {
            ServiceName = "DnwSusAgent";
            CanStop = true;
            CanShutdown = true;
        }

        protected override void OnStart(string[] args) => _worker.Start();
        protected override void OnStop() => _worker.Stop();
        protected override void OnShutdown() => _worker.Stop();
    }

    internal static class Program
    {
        private static void Main() => ServiceBase.Run(new AgentService());
    }
}
