using System;
using System.Windows.Forms;

namespace WindowsUpdateManager.Helpers
{
    public static class ControlExtensions
    {
        /// <summary>
        /// Invokes the action on the UI thread if required; otherwise runs directly.
        /// </summary>
        public static void InvokeIfRequired(this Control control, Action action)
        {
            if (control == null) throw new ArgumentNullException("control");
            if (action == null)  throw new ArgumentNullException("action");

            if (control.IsDisposed || !control.IsHandleCreated) return;

            if (control.InvokeRequired)
            {
                try
                {
                    control.Invoke(action);
                }
                catch (ObjectDisposedException) { }
                catch (System.ComponentModel.InvalidAsynchronousStateException) { }
            }
            else
            {
                action();
            }
        }

        /// <summary>
        /// Finds the first DataGridViewRow where column index 1 (Server name) equals serverName.
        /// Returns null if not found.
        /// </summary>
        public static DataGridViewRow FindServerRow(this DataGridView dgv, string serverName)
        {
            if (dgv == null)                       throw new ArgumentNullException("dgv");
            if (string.IsNullOrEmpty(serverName)) return null;

            foreach (DataGridViewRow row in dgv.Rows)
            {
                if (row.Cells.Count > 1)
                {
                    string cellValue = row.Cells[1].Value as string;
                    if (string.Equals(cellValue, serverName, StringComparison.OrdinalIgnoreCase))
                        return row;
                }
            }

            return null;
        }
    }
}
