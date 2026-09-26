using System.Windows.Forms;

namespace ImageToolbox
{
    public abstract class ToolPage : UserControl
    {
        public abstract string ToolName { get; }

        public virtual void Shutdown()
        {
        }
    }
}
