using System;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using System.Threading.Tasks;

namespace WindowsUpdateManager.Services
{
    public class RemoteSessionService
    {
        public RemoteSessionService() { }

        /// <summary>
        /// Invokes a PowerShell script on a remote computer via WinRM and returns the string output.
        /// </summary>
        public async Task<string> InvokeScriptAsync(string computerName, string script, int timeoutSeconds)
        {
            return await Task.Run(() =>
            {
                var connectionUri = new Uri("http://" + computerName + ":5985/wsman");
                var connectionInfo = new WSManConnectionInfo(connectionUri);
                connectionInfo.OpenTimeout      = 30000;
                connectionInfo.OperationTimeout = timeoutSeconds * 1000;

                using (var runspace = RunspaceFactory.CreateRunspace(connectionInfo))
                {
                    runspace.Open();

                    using (var pipeline = runspace.CreatePipeline())
                    {
                        pipeline.Commands.AddScript(script);

                        var psObjects = pipeline.Invoke();

                        if (pipeline.Error.Count > 0)
                        {
                            var errorSb = new StringBuilder();
                            while (pipeline.Error.Count > 0)
                            {
                                var errObj = pipeline.Error.Read();
                                if (errObj != null)
                                    errorSb.AppendLine(errObj.ToString());
                            }
                            throw new InvalidOperationException(
                                "Fehler auf " + computerName + ": " + errorSb.ToString().Trim());
                        }

                        var sb = new StringBuilder();
                        foreach (PSObject obj in psObjects)
                        {
                            if (obj != null)
                                sb.AppendLine(obj.ToString());
                        }

                        return sb.ToString().Trim();
                    }
                }
            });
        }

        /// <summary>
        /// Tests whether a WinRM connection to the computer can be established.
        /// </summary>
        public async Task<bool> TestConnectionAsync(string computerName)
        {
            try
            {
                await InvokeScriptAsync(computerName, "$env:COMPUTERNAME", 20);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
