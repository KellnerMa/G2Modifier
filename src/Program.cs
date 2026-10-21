using System;
using System.Threading;
using System.Windows.Forms;

namespace G2Modifier
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);
            Application.Run(new UI.MainForm());
        }

        /// <summary>Jeder unerwartete Fehler landet zusaetzlich in %APPDATA%\G2Modifier\fehler.log (fuer die Fehlersuche).</summary>
        static void ShowError(Exception ex)
        {
            try
            {
                string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "G2Modifier");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "fehler.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + UI.MainForm.Version + "\r\n" + ex + "\r\n\r\n");
            }
            catch { }
            MessageBox.Show(Core.L.T("Unerwarteter Fehler. Es wurde nichts zusätzlich geschrieben.") + "\n\n" + (ex != null ? ex.ToString() : "unbekannt"),
                "Grandia II Modifier", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
